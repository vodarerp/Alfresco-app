using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Mapper;
using Alfresco.Contracts.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Abstraction.Models;
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
    public class PreviewFolderPreparationService : IPreviewFolderPreparationService
    {
        private readonly IAlfrescoReadApi _alfrescoReadApi;
        private readonly IOptions<MigrationOptions> _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IClientApiEnricher _clientApiEnricher;
        private readonly ILogger _fileLogger;
        private readonly ILogger _dbLogger;
        private readonly ILogger _uiLogger;

        // Cache za DOSSIERS-* folder ID-eve (ne menjaju se tokom rada)
        private readonly ConcurrentDictionary<string, string?> _dossierParentCache = new();

        // Mapa: prefix foldera → ime DOSSIERS-* foldera
        private static readonly Dictionary<string, string> _prefixToDossierFolder = new(StringComparer.OrdinalIgnoreCase)
        {
            ["FL"] = "DOSSIERS-FL",
            ["PL"] = "DOSSIERS-PL",
            ["ACC"] = "DOSSIERS-ACC",
            ["D"] = "DOSSIERS-D",
        };

        public PreviewFolderPreparationService(
            IAlfrescoReadApi alfrescoReadApi,
            IOptions<MigrationOptions> options,
            IServiceScopeFactory scopeFactory,
            IClientApiEnricher clientApiEnricher,
            ILoggerFactory loggerFactory)
        {
            _alfrescoReadApi = alfrescoReadApi ?? throw new ArgumentNullException(nameof(alfrescoReadApi));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _clientApiEnricher = clientApiEnricher ?? throw new ArgumentNullException(nameof(clientApiEnricher));
            _fileLogger = loggerFactory.CreateLogger("FileLogger");
            _dbLogger = loggerFactory.CreateLogger("DbLogger");
            _uiLogger = loggerFactory.CreateLogger("UiLogger");
        }

        public async Task<bool> RunAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback = null)
        {
            var sw = Stopwatch.StartNew();
            var rootDestId = _options.Value.RootDestinationFolderId;
            const int folderBatchSize = 200;

            if (string.IsNullOrWhiteSpace(rootDestId))
            {
                _uiLogger.LogWarning("PreviewFolderPreparationService: RootDestinationFolderId nije konfigurisan.");
                return false;
            }

            _fileLogger.LogInformation("PreviewFolderPreparationService: Start. RootDestId={RootDestId}", rootDestId);
            _uiLogger.LogInformation("PreviewFolderPreparationService: Pokretanje Faze 2...");

            long totalProcessed = 0;
            long totalExists = 0;
            long totalPending = 0;
            long totalFailed = 0;
            int batchNum = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                // Atomično uzimamo sledeći batch folder-a (status → IN_PROGRESS)
                IEnumerable<string> folderNames;
                await using (var scope = _scopeFactory.CreateAsyncScope())
                {
                    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                    await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                    try
                    {
                        folderNames = (await repo.GetDistinctPendingFoldersAsync(folderBatchSize, ct).ConfigureAwait(false)).ToList();
                        await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                        throw;
                    }
                }

                var batch = (folderNames as IList<string>) ?? folderNames.ToList();
                if (batch.Count == 0)
                {
                    _fileLogger.LogInformation("PreviewFolderPreparationService: Nema vise PENDING foldera, zavrseno.");
                    break;
                }

                batchNum++;
                _fileLogger.LogInformation(
                    "PreviewFolderPreparationService: Batch {Batch} - {Count} foldera za proveru",
                    batchNum, batch.Count);

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 5,
                    CancellationToken = ct
                };

                await Parallel.ForEachAsync(batch, parallelOptions, async (folderName, token) =>
                {
                    try
                    {
                        var result = await CheckFolderInAlfrescoAsync(folderName, rootDestId, token).ConfigureAwait(false);
                        await PersistFolderResultAsync(folderName, result, token).ConfigureAwait(false);

                        Interlocked.Increment(ref totalProcessed);
                        if (result.Exists) Interlocked.Increment(ref totalExists);
                        else Interlocked.Increment(ref totalPending);

                        _fileLogger.LogInformation(
                            "PreviewFolderPreparationService: '{Folder}' → {Status}",
                            folderName, result.Exists ? "FOLDER_EXISTS" : "FOLDER_PENDING_CREATION");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref totalFailed);
                        _fileLogger.LogError(
                            "PreviewFolderPreparationService: Greska za folder '{Folder}': {Error}",
                            folderName, ex.Message);
                        _dbLogger.LogError(ex, "PreviewFolderPreparationService: Folder '{Folder}'", folderName);

                        // Vracamo status na PENDING da se moze ponoviti
                        await TryResetFolderStatusAsync(folderName, token).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);

                progressCallback?.Invoke(new WorkerProgress
                {
                    ProcessedItems = totalProcessed,
                    SuccessCount = (int)(totalExists + totalPending),
                    FailedCount = (int)totalFailed,
                    Message = $"Batch {batchNum}: provjereno {totalProcessed} foldera (postoji={totalExists}, kreira={totalPending})",
                    Timestamp = DateTimeOffset.UtcNow
                });
            }

            var summary = $"Faza 2 zavrsena za {sw.Elapsed.TotalSeconds:F1}s — " +
                          $"ukupno={totalProcessed}, postoji={totalExists}, kreirati={totalPending}, greske={totalFailed}";

            _fileLogger.LogInformation("PreviewFolderPreparationService: {Summary}", summary);
            _uiLogger.LogInformation("PreviewFolderPreparationService: {Summary}", summary);

            progressCallback?.Invoke(new WorkerProgress
            {
                ProcessedItems = totalProcessed,
                SuccessCount = (int)(totalExists + totalPending),
                FailedCount = (int)totalFailed,
                Message = summary,
                Timestamp = DateTimeOffset.UtcNow
            });

            return true;
        }

        // ──────────────────────────────────────────────────────────────────
        // Provera postojanja foldera u Alfresci
        // ──────────────────────────────────────────────────────────────────

        private async Task<FolderCheckResult> CheckFolderInAlfrescoAsync(
            string folderName, string rootDestId, CancellationToken ct)
        {
            // Određujemo DOSSIERS-* parent po prefiksu foldera (FL-102206 → DOSSIERS-FL)
            var prefix = DossierIdFormatter.ExtractPrefix(folderName)?.ToUpperInvariant() ?? "";
            var dossierParentFolderName = _prefixToDossierFolder.GetValueOrDefault(prefix);

            if (string.IsNullOrWhiteSpace(dossierParentFolderName))
            {
                _fileLogger.LogWarning(
                    "PreviewFolderPreparationService: Nepoznat prefix '{Prefix}' za folder '{Folder}', tretira se kao nepostojeci",
                    prefix, folderName);
                return await BuildPendingResultAsync(folderName, ct).ConfigureAwait(false);
            }

            // Dobijamo (ili kešujemo) ID DOSSIERS-* foldera
            var dossierParentId = await GetOrCacheDossierParentIdAsync(rootDestId, dossierParentFolderName, ct)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(dossierParentId))
            {
                _fileLogger.LogInformation(
                    "PreviewFolderPreparationService: DOSSIERS folder '{DossierFolder}' ne postoji u root-u — '{FolderName}' → PENDING_CREATION",
                    dossierParentFolderName, folderName);
                return await BuildPendingResultAsync(folderName, ct).ConfigureAwait(false);
            }

            // Tražimo dossier folder pod DOSSIERS-* parent-om
            var nodeResponse = await _alfrescoReadApi.GetFolderByNameAsync(dossierParentId, folderName, ct)
                .ConfigureAwait(false);

            if (nodeResponse?.Entry != null)
            {
                _fileLogger.LogInformation(
                    "PreviewFolderPreparationService: '{FolderName}' POSTOJI (NodeId={NodeId})",
                    folderName, nodeResponse.Entry.Id);
                return new FolderCheckResult { Exists = true, NodeId = nodeResponse.Entry.Id };
            }

            // Folder ne postoji → pozivamo ClientAPI
            return await BuildPendingResultAsync(folderName, ct).ConfigureAwait(false);
        }

        private async Task<FolderCheckResult> BuildPendingResultAsync(string folderName, CancellationToken ct)
        {
            ClientData? clientData = null;
            try
            {
                clientData = await _clientApiEnricher.EnrichFromFolderNameAsync(folderName, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning(
                    "PreviewFolderPreparationService: ClientAPI greska za '{FolderName}': {Error}",
                    folderName, ex.Message);
            }

            return new FolderCheckResult { Exists = false, ClientData = clientData };
        }

        private async Task<string?> GetOrCacheDossierParentIdAsync(
            string rootDestId, string dossierParentFolderName, CancellationToken ct)
        {
            if (_dossierParentCache.TryGetValue(dossierParentFolderName, out var cached))
                return cached;

            var response = await _alfrescoReadApi.GetFolderByNameAsync(rootDestId, dossierParentFolderName, ct)
                .ConfigureAwait(false);

            var id = response?.Entry?.Id;
            _dossierParentCache[dossierParentFolderName] = id;

            if (id != null)
                _fileLogger.LogInformation(
                    "PreviewFolderPreparationService: Kesirano '{DossierFolder}' → {Id}",
                    dossierParentFolderName, id);
            else
                _fileLogger.LogWarning(
                    "PreviewFolderPreparationService: '{DossierFolder}' nije pronađen pod root-om {RootId}",
                    dossierParentFolderName, rootDestId);

            return id;
        }

        // ──────────────────────────────────────────────────────────────────
        // Upisivanje rezultata u bazu
        // ──────────────────────────────────────────────────────────────────

        private async Task PersistFolderResultAsync(string folderName, FolderCheckResult result, CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                if (result.Exists)
                {
                    await repo.UpdateFolderDataAndClientApiAsync(
                        folderName,
                        folderId: result.NodeId,
                        isCreated: 1,
                        status: "FOLDER_EXISTS",
                        clientData: null,
                        ct).ConfigureAwait(false);
                }
                else
                {
                    var clientData = result.ClientData is { HasError: false } ? result.ClientData : null;
                    await repo.UpdateFolderDataAndClientApiAsync(
                        folderName,
                        folderId: null,
                        isCreated: 0,
                        status: "FOLDER_PENDING_CREATION",
                        clientData: clientData,
                        ct).ConfigureAwait(false);
                }

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);
            }
            catch
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        private async Task TryResetFolderStatusAsync(string folderName, CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                await repo.UpdateFolderDataAsync(folderName, null, 0, "PENDING", ct).ConfigureAwait(false);
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("PreviewFolderPreparationService: Ne mogu da resetujem status za '{Folder}': {Error}",
                    folderName, ex.Message);
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Pomoćna klasa za rezultat provere
        // ──────────────────────────────────────────────────────────────────

        private sealed class FolderCheckResult
        {
            public bool Exists { get; set; }
            public string? NodeId { get; set; }
            public ClientData? ClientData { get; set; }
        }
    }
}
