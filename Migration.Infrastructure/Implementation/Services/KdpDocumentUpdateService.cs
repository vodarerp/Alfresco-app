using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Oracle.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    /// <summary>
    /// Servis za ažuriranje KDP dokumenata u Alfrescu
    /// Procesira dokumente iz KdpExportResult tabele i ažurira njihove property-je
    /// </summary>
    public class KdpDocumentUpdateService : IKdpDocumentUpdateService
    {
        private readonly IAlfrescoWriteApi _alfrescoWriteApi;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        // Progress tracking
        private KdpUpdateProgress _currentProgress = new();
        private readonly object _progressLock = new();

        public KdpDocumentUpdateService(
            IAlfrescoWriteApi alfrescoWriteApi,
            IServiceScopeFactory scopeFactory,
            ILoggerFactory logger)
        {
            _alfrescoWriteApi = alfrescoWriteApi ?? throw new ArgumentNullException(nameof(alfrescoWriteApi));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger.CreateLogger("FileLogger");
        }

        /// <summary>
        /// Pokreće proces ažuriranja dokumenata u batch-evima
        /// </summary>
        public async Task<KdpUpdateResult> UpdateDocumentsAsync(
            int batchSize = 500,
            int maxDegreeOfParallelism = 5,
            Action<KdpUpdateProgress>? progressCallback = null,
            CancellationToken ct = default)
        {
            var result = new KdpUpdateResult
            {
                StartTime = DateTime.Now
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogInformation(
                    "Početak ažuriranja KDP dokumenata. BatchSize: {BatchSize}, MaxParallelism: {MaxParallelism}",
                    batchSize, maxDegreeOfParallelism);

                // Inicijalizuj progress
                var totalCount = await GetTotalUnupdatedCountAsync(ct);

                lock (_progressLock)
                {
                    _currentProgress = new KdpUpdateProgress
                    {
                        TotalDocuments = totalCount,
                        StatusMessage = "Pokretanje ažuriranja..."
                    };
                }

                progressCallback?.Invoke(_currentProgress);

                if (totalCount == 0)
                {
                    _logger.LogInformation("Nema dokumenata za ažuriranje");
                    result.EndTime = DateTime.Now;
                    return result;
                }

                _logger.LogInformation("Ukupno dokumenata za ažuriranje: {Count}", totalCount);

                int batchNumber = 0;
                int totalSuccessful = 0;
                int totalFailed = 0;

                // Procesiranje u batch-evima
                while (!ct.IsCancellationRequested)
                {
                    batchNumber++;

                    // Uzmi batch neažuriranih dokumenata
                    var batch = await GetUnupdatedBatchAsync(batchSize, ct);

                    if (batch.Count == 0)
                    {
                        _logger.LogInformation("Nema više dokumenata za ažuriranje");
                        break;
                    }

                    _logger.LogInformation(
                        "Batch {BatchNumber}: Procesiranje {Count} dokumenata...",
                        batchNumber, batch.Count);

                    // Thread-safe counters za ovaj batch
                    var batchSuccessful = 0;
                    var batchFailed = 0;
                    var updateResults = new ConcurrentDictionary<long, (bool Success, string Message)>();

                    // Parallel procesiranje batch-a
                    var parallelOptions = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = maxDegreeOfParallelism,
                        CancellationToken = ct
                    };

                    await Parallel.ForEachAsync(batch, parallelOptions, async (document, token) =>
                    {
                        try
                        {
                            var (success, message) = await UpdateSingleDocumentAsync(document, token);

                            updateResults[document.Id] = (success, message);

                            if (success)
                            {
                                Interlocked.Increment(ref batchSuccessful);
                            }
                            else
                            {
                                Interlocked.Increment(ref batchFailed);
                            }

                            // Update progress
                            lock (_progressLock)
                            {
                                _currentProgress.ProcessedDocuments++;
                                _currentProgress.SuccessfulUpdates = totalSuccessful + batchSuccessful;
                                _currentProgress.FailedUpdates = totalFailed + batchFailed;
                                _currentProgress.CurrentBatch = batchNumber;
                                _currentProgress.ElapsedTime = stopwatch.Elapsed;

                                // Izračunaj procenjeno preostalo vreme
                                if (_currentProgress.ProcessedDocuments > 0)
                                {
                                    var docsPerSecond = _currentProgress.ProcessedDocuments / stopwatch.Elapsed.TotalSeconds;
                                    _currentProgress.DocumentsPerSecond = Math.Round(docsPerSecond, 2);

                                    var remainingDocs = _currentProgress.TotalDocuments - _currentProgress.ProcessedDocuments;
                                    if (docsPerSecond > 0)
                                    {
                                        _currentProgress.EstimatedTimeRemaining = TimeSpan.FromSeconds(remainingDocs / docsPerSecond);
                                    }
                                }

                                _currentProgress.StatusMessage = $"Batch {batchNumber}: {_currentProgress.ProcessedDocuments}/{_currentProgress.TotalDocuments}";
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Greška pri ažuriranju dokumenta {DocumentId} (NodeId: {NodeId})",
                                document.Id, document.ReferencaDokumenta);

                            updateResults[document.Id] = (false, $"Exception: {ex.Message}");
                            Interlocked.Increment(ref batchFailed);
                        }
                    });

                    // Ažuriraj status dokumenata u bazi
                    await MarkDocumentsAsUpdatedAsync(updateResults, ct);

                    totalSuccessful += batchSuccessful;
                    totalFailed += batchFailed;

                    _logger.LogInformation(
                        "Batch {BatchNumber} završen: {Successful} uspešno, {Failed} neuspešno. Ukupno: {TotalProcessed}/{TotalDocuments}",
                        batchNumber, batchSuccessful, batchFailed,
                        totalSuccessful + totalFailed, totalCount);

                    // Prijavi progress
                    progressCallback?.Invoke(_currentProgress);

                    // Kratka pauza između batch-eva da se ne preoptereti Alfresco
                    await Task.Delay(500, ct);
                }

                result.TotalSuccessful = totalSuccessful;
                result.TotalFailed = totalFailed;
                result.WasCancelled = ct.IsCancellationRequested;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Ažuriranje dokumenata je otkazano");
                result.WasCancelled = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kritična greška pri ažuriranju dokumenata");
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                result.EndTime = DateTime.Now;

                _logger.LogInformation(
                    "Ažuriranje završeno. Uspešno: {Successful}, Neuspešno: {Failed}, Trajanje: {Duration}",
                    result.TotalSuccessful, result.TotalFailed, result.Duration);
            }

            return result;
        }

        /// <summary>
        /// Vraća trenutni status ažuriranja
        /// </summary>
        public Task<KdpUpdateProgress> GetProgressAsync(CancellationToken ct = default)
        {
            lock (_progressLock)
            {
                return Task.FromResult(new KdpUpdateProgress
                {
                    TotalDocuments = _currentProgress.TotalDocuments,
                    ProcessedDocuments = _currentProgress.ProcessedDocuments,
                    SuccessfulUpdates = _currentProgress.SuccessfulUpdates,
                    FailedUpdates = _currentProgress.FailedUpdates,
                    CurrentBatch = _currentProgress.CurrentBatch,
                    EstimatedTimeRemaining = _currentProgress.EstimatedTimeRemaining,
                    ElapsedTime = _currentProgress.ElapsedTime,
                    DocumentsPerSecond = _currentProgress.DocumentsPerSecond,
                    StatusMessage = _currentProgress.StatusMessage
                });
            }
        }

        /// <summary>
        /// Ažurira pojedinačni dokument u Alfrescu
        /// </summary>
        private async Task<(bool Success, string Message)> UpdateSingleDocumentAsync(
            KdpExportResult document,
            CancellationToken ct)
        {
            var nodeId = document.ReferencaDokumenta;

            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return (false, "NodeId je prazan");
            }

            if (document?.Action != null && document?.Action == 0)
            {
                return (true, "Akcija 0. Ne treba update");
            }
            if ((document?.Action != null && document?.Action == 1) && (document.Izuzetak != null && document.Izuzetak == 1))
            {
                return (true, "Dokument izuzetak. Ne treba update");
            }

            try
            {
                // Pripremi properties za update na osnovu Action vrednosti
                var properties = BuildUpdateProperties(document);

                if (properties.Count == 0)
                {
                    return (false, "Nema properties za update");
                }

                _logger.LogDebug(
                    "Ažuriranje dokumenta {NodeId}, Action: {Action}, Properties: {PropertyCount}",
                    nodeId, document.Action, properties.Count);

                // Pozovi Alfresco API
                var success = await _alfrescoWriteApi.UpdateNodePropertiesAsync(nodeId, properties, ct);

                if (success)
                {
                    return (true, "OK Updated");
                }
                else
                {
                    return (false, "Alfresco API false");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Greška pri ažuriranju dokumenta {NodeId}: {Message}",
                    nodeId, ex.Message);

                return (false, $"Error: {ex.Message}");
            }
        }

       
        private Dictionary<string, object> BuildUpdateProperties(KdpExportResult document)
        {
            var properties = new Dictionary<string, object>();

            switch (document.Action)
            {
                case 1:
                    properties["ecm:docStatus"] = "1";
                    properties["ecm:docType"] = "00099";
                    properties["ecm:docTypeCode"] = "00099";
                    properties["ecm:docTypeName"] = "KDP za fizička lica";
                    if (!string.IsNullOrWhiteSpace(document.ListaRacuna))
                    {
                        properties["ecm:docAccountNumbers"] = document.ListaRacuna;
                    }
                    break;
                case 2:
                    // Samo update statusa na 2
                    properties["ecm:docStatus"] = "2";
                    break;
                default:
                    _logger.LogWarning(
                        "Nepoznata Action vrednost {Action} za dokument {DocumentId}",
                        document.Action, document.Id);
                    break;
            }

            return properties;
        }

        
        private async Task<long> GetTotalUnupdatedCountAsync(CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IKdpExportResultRepository>();

            await uow.BeginAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            try
            {
                var count = await repo.CountUnupdatedAsync(ct);
                await uow.CommitAsync(ct);
                return count;
            }
            catch
            {
                await uow.RollbackAsync(ct);
                throw;
            }
        }

        /// <summary>
        /// Vraća batch neažuriranih dokumenata
        /// </summary>
        private async Task<IReadOnlyList<KdpExportResult>> GetUnupdatedBatchAsync(int batchSize, CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IKdpExportResultRepository>();

            await uow.BeginAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            try
            {
                var batch = await repo.GetUnupdatedBatchAsync(batchSize, ct);
                await uow.CommitAsync(ct);
                return batch;
            }
            catch
            {
                await uow.RollbackAsync(ct);
                throw;
            }
        }

        /// <summary>
        /// Označava dokumente kao ažurirane u bazi
        /// VAŽNO: Svi obrađeni dokumenti se označavaju kao IsUpdated=true,
        /// bez obzira na uspeh/neuspeh API poziva. UpdateMessage sadrži rezultat.
        /// </summary>
        private async Task MarkDocumentsAsUpdatedAsync(
            ConcurrentDictionary<long, (bool Success, string Message)> results,
            CancellationToken ct)
        {
            if (results.IsEmpty)
                return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IKdpExportResultRepository>();

            await uow.BeginAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            try
            {
                // Ažuriraj svaki dokument pojedinačno sa njegovim statusom
                // VAŽNO: IsUpdated je UVEK true - označava da je dokument OBRAĐEN
                // Success/Failure se vidi u UpdateMessage polju
                foreach (var kvp in results)
                {
                    var id = kvp.Key;
                    var (success, message) = kvp.Value;

                    // Prefiks za poruku da se zna da li je uspelo ili ne
                    var finalMessage = success ? $"OK: {message}" : $"FAILED: {message}";

                    // IsUpdated = true UVEK - dokument je obrađen, nema potrebe ponovo ga procesirati
                    await repo.UpdateDocumentStatusAsync(id, isUpdated: true, finalMessage, ct);
                }

                await uow.CommitAsync(ct);
            }
            catch
            {
                await uow.RollbackAsync(ct);
                throw;
            }
        }
    }
}
