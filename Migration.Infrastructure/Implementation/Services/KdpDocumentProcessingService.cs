using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Request;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Concurrent;

namespace Migration.Infrastructure.Implementation.Services
{
    /// <summary>
    /// Servis za obradu KDP dokumenata (tipovi 00824 i 00099)
    /// </summary>
    public class KdpDocumentProcessingService : IKdpDocumentProcessingService
    {
        private readonly IAlfrescoReadApi _alfrescoReadApi;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptions<MigrationOptions> _options;
        private readonly ILogger<KdpDocumentProcessingService> _logger;

        public KdpDocumentProcessingService(
            IAlfrescoReadApi alfrescoReadApi,
            IServiceScopeFactory scopeFactory,
            IOptions<MigrationOptions> options,
            ILogger<KdpDocumentProcessingService> logger)
        {
            _alfrescoReadApi = alfrescoReadApi ?? throw new ArgumentNullException(nameof(alfrescoReadApi));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Učitava sve KDP dokumente (00824 i 00099) iz Alfresca i puni staging tabelu
        /// Koristi hibridni pristup: parallel fetch + streaming insert za optimalno korišćenje memorije
        /// </summary>
        public async Task<int> LoadKdpDocumentsToStagingAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Početak učitavanja KDP dokumenata iz Alfresca (hibridni pristup)...");

            try
            {
                // Očisti staging tabelu
                await ClearStagingAsync(ct);

                var kdpOptions = _options.Value.KdpProcessing;
                var batchSize = kdpOptions.BatchSize;
                var query = BuildKdpQuery();

                // 1. Dobij ukupan broj dokumenata
                var totalCount = await GetTotalDocumentCountAsync(query, ct);
                _logger.LogInformation("Ukupno dokumenata za učitavanje: {Count}", totalCount);

                if (totalCount == 0)
                {
                    _logger.LogWarning("Nema KDP dokumenata za učitavanje");
                    return 0;
                }
                batchSize = 10;

                // 2. Pre-kalkuliši sve skip vrednosti (svaki element je JEDINSTVEN)
                var skipValues = Enumerable
                    .Range(0, (totalCount + batchSize - 1) / batchSize)
                    .Select(i => i * batchSize)
                    .ToList();

                _logger.LogInformation("Broj batch-eva za obradu: {Count}", skipValues.Count);

                // ═══════════════════════════════════════════════════════════════
                // KLJUČNE STRUKTURE ZA HIBRIDNI PRISTUP
                // ═══════════════════════════════════════════════════════════════

                // Thread-safe queue - svi PARALLEL PROCESI ovde DODAJU batch-eve
                var pendingBatches = new ConcurrentQueue<List<KdpDocumentStaging>>();

                // SEMAFOR - GARANTUJE DA SAMO 1 PROCES RADI UPIS U BAZU
                var dbWriteLock = new SemaphoreSlim(1, 1);

                // Prag za bulk insert (akumuliraj 5 batch-eva pre inserta)
                const int batchThreshold = 5;

                // Ukupan broj upisanih dokumenata (thread-safe)
                var totalInserted = 0;

                // ═══════════════════════════════════════════════════════════════
                // PARALLEL PROCESSING SA STREAMING INSERT
                // ═══════════════════════════════════════════════════════════════

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 5,  // Max 5 simultanih API poziva ka Alfrescu
                    CancellationToken = ct
                };

                await Parallel.ForEachAsync(skipValues, parallelOptions, async (skipCount, token) =>
                {
                    // ───────────────────────────────────────────────────────────
                    // KORAK 1: FETCH (paralelno - do 5 thread-ova istovremeno)
                    // ───────────────────────────────────────────────────────────
                    var request = new PostSearchRequest
                    {
                        Query = new QueryRequest { Query = query, Language = "afts" },
                        Include = new[] { "properties", "path" },
                        Paging = new PagingRequest
                        {
                            MaxItems = batchSize,
                            SkipCount = skipCount
                        },
                        Sort = new List<SortRequest>
                        {
                            new() { Field = "sys:node-uuid", Ascending = true }
                        }
                    };

                    var response = await _alfrescoReadApi.SearchAsync(request, token);
                    var entries = response.List.Entries.Select(e => e.Entry).ToList();

                    if (entries.Count == 0) return;

                    // ───────────────────────────────────────────────────────────
                    // KORAK 2: MAP (paralelno - svaki thread mapira svoj batch)
                    // ───────────────────────────────────────────────────────────
                    var stagingDocs = entries.Select(MapToKdpDocumentStaging).ToList();

                    // ───────────────────────────────────────────────────────────
                    // KORAK 3: ENQUEUE (thread-safe, lock-free)
                    // ───────────────────────────────────────────────────────────
                    pendingBatches.Enqueue(stagingDocs);

                    _logger.LogDebug("Batch skip={Skip} učitan ({Count} dok.), queue size: {Size}",
                        skipCount, stagingDocs.Count, pendingBatches.Count);

                    // ───────────────────────────────────────────────────────────
                    // KORAK 4: POKUŠAJ BULK INSERT (samo ako ima dovoljno batch-eva)
                    // ───────────────────────────────────────────────────────────
                    if (pendingBatches.Count >= batchThreshold)
                    {
                        // TryWait sa timeout 0 = ne blokiraj, samo proveri dostupnost
                        var acquired = await dbWriteLock.WaitAsync(0, token);

                        if (acquired)
                        {
                            // ═══════════════════════════════════════════════════
                            // KRITIČNA SEKCIJA - SAMO 1 THREAD OVDE U ISTO VREME
                            // ═══════════════════════════════════════════════════
                            try
                            {
                                // PRAŽNJENJE QUEUE-a u lokalnu listu
                                var toInsert = new List<KdpDocumentStaging>();

                                while (pendingBatches.TryDequeue(out var batch))
                                {
                                    toInsert.AddRange(batch);
                                }

                                if (toInsert.Count > 0)
                                {
                                    // BULK INSERT
                                    await using var scope = _scopeFactory.CreateAsyncScope();
                                    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                                    var repo = scope.ServiceProvider.GetRequiredService<IKdpDocumentStagingRepository>();

                                    await uow.BeginAsync(ct: token).ConfigureAwait(false);
                                    try
                                    {
                                        var inserted = await repo.InsertManyAsync(toInsert, token);
                                        await uow.CommitAsync(ct: token).ConfigureAwait(false);

                                        Interlocked.Add(ref totalInserted, inserted);

                                        _logger.LogInformation(
                                            "Bulk insert: {Count} dokumenata, ukupno upisano: {Total}",
                                            inserted, totalInserted);
                                    }
                                    catch
                                    {
                                        await uow.RollbackAsync(ct: token).ConfigureAwait(false);
                                        throw;
                                    }
                                }
                                // toInsert izlazi iz scope-a → MEMORIJA OSLOBOĐENA
                            }
                            finally
                            {
                                // OSLOBODI SEMAFOR - sledeći thread može da uđe
                                dbWriteLock.Release();
                            }
                        }
                        // else: Drugi thread već radi insert, ovaj nastavlja sa fetch-om
                    }
                });

                // ═══════════════════════════════════════════════════════════════
                // FINAL FLUSH - upiši preostale batch-eve nakon što su svi upisani
                // ═══════════════════════════════════════════════════════════════
                if (!pendingBatches.IsEmpty)
                {
                    _logger.LogInformation("Final flush - preostalo {Count} batch-eva u queue-u", pendingBatches.Count);

                    var remaining = new List<KdpDocumentStaging>();

                    while (pendingBatches.TryDequeue(out var batch))
                    {
                        remaining.AddRange(batch);
                    }

                    if (remaining.Count > 0)
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                        var repo = scope.ServiceProvider.GetRequiredService<IKdpDocumentStagingRepository>();

                        await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                        try
                        {
                            var inserted = await repo.InsertManyAsync(remaining, ct);
                            await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                            totalInserted += inserted;

                            _logger.LogInformation("Final flush upisao: {Count} dokumenata", inserted);
                        }
                        catch
                        {
                            await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                            throw;
                        }
                    }
                }

                _logger.LogInformation("Završeno učitavanje. Ukupno upisano: {Total} dokumenata", totalInserted);

                return totalInserted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Greška pri učitavanju KDP dokumenata");
                throw;
            }
        }

        /// <summary>
        /// Procesuira staging podatke pozivom sp_ProcessKdpDocuments
        /// </summary>
        public async Task<(int totalCandidates, int totalDocuments)> ProcessKdpDocumentsAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Pokretanje obrade KDP dokumenata (sp_ProcessKdpDocuments)...");

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var kdpExportRepo = scope.ServiceProvider.GetRequiredService<IKdpExportResultRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var result = await kdpExportRepo.ProcessKdpDocumentsAsync(ct);
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    _logger.LogInformation(
                        "Obrada završena: {Candidates} kandidata, {Documents} dokumenata",
                        result.totalCandidates,
                        result.totalDocuments);

                    return result;
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Greška pri obradi KDP dokumenata");
                throw;
            }
        }

        /// <summary>
        /// Eksportuje rezultate u Excel fajl (placeholder za buduću implementaciju)
        /// </summary>
        public Task ExportToExcelAsync(string filePath, CancellationToken ct = default)
        {
            // TODO: Implementirati Excel export korišćenjem ClosedXML ili EPPlus
            // SELECT * FROM KdpExportResult -> Excel
            _logger.LogWarning("ExportToExcelAsync nije još implementirana");
            throw new NotImplementedException("Excel export će biti implementiran kasnije");
        }

        /// <summary>
        /// Briše staging tabelu
        /// </summary>
        public async Task ClearStagingAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Čišćenje staging tabele...");

            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var kdpStagingRepo = scope.ServiceProvider.GetRequiredService<IKdpDocumentStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                await kdpStagingRepo.ClearStagingAsync(ct);
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                _logger.LogInformation("Staging tabela očišćena");
            }
            catch
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Vraća statistiku obrade
        /// </summary>
        public async Task<KdpProcessingStatistics> GetStatisticsAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Učitavanje statistike...");

            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var kdpStagingRepo = scope.ServiceProvider.GetRequiredService<IKdpDocumentStagingRepository>();
            var kdpExportRepo = scope.ServiceProvider.GetRequiredService<IKdpExportResultRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                var stagingCount = await kdpStagingRepo.CountAsync(ct);
                var exportCount = await kdpExportRepo.CountAsync(ct);

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                // TODO: Dodati detaljnije statistike ako je potrebno
                // (npr. COUNT po statusu, COUNT po tipu, MIN/MAX datum, itd.)

                return new KdpProcessingStatistics
                {
                    TotalDocumentsInStaging = stagingCount,
                    TotalCandidateFolders = exportCount,
                    TotalDocumentsInCandidateFolders = 0, // Može se izvući iz stored procedure rezultata
                    OldestDocumentDate = null,
                    NewestDocumentDate = null,
                    InactiveDocumentsCount = 0,
                    ActiveDocumentsCount = 0,
                    Type00824Count = 0,
                    Type00099Count = 0
                };
            }
            catch
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        // ============================================
        // PRIVATE HELPER METHODS
        // ============================================

        /// <summary>
        /// Gradi AFTS query za KDP dokumente na osnovu konfiguracije
        /// </summary>
        private string BuildKdpQuery()
        {
            var kdpOptions = _options.Value.KdpProcessing;
            var docTypes = kdpOptions.DocTypes ?? new List<string> { "00824", "00099" };

            // Build AFTS query for KDP documents
            var docTypeConditions = string.Join(" OR ", docTypes.Select(t => $"=ecm\\:docType:\"{t}\""));
            var query = $"({docTypeConditions}) AND TYPE:\"cm:content\"";

            // Add ANCESTOR condition if configured
            if (!string.IsNullOrWhiteSpace(kdpOptions.AncestorFolderId))
            {
                var ancestorId = kdpOptions.AncestorFolderId;

                // Ensure the ancestor ID is in the correct format (workspace://SpacesStore/xxx)
                if (!ancestorId.StartsWith("workspace://", StringComparison.OrdinalIgnoreCase))
                {
                    ancestorId = $"workspace://SpacesStore/{ancestorId}";
                }

                query += $" AND ANCESTOR:\"{ancestorId}\"";
                _logger.LogInformation("Using ANCESTOR filter: {AncestorId}", ancestorId);
            }

            _logger.LogInformation("AFTS query za KDP dokumente: {Query}", query);

            return query;
        }

        /// <summary>
        /// Dobija ukupan broj dokumenata koji odgovaraju query-ju (jedan API poziv)
        /// </summary>
        private async Task<int> GetTotalDocumentCountAsync(string query, CancellationToken ct)
        {
            var request = new PostSearchRequest
            {
                Query = new QueryRequest { Query = query, Language = "afts" },
                Paging = new PagingRequest { MaxItems = 1, SkipCount = 0 }
            };

            var response = await _alfrescoReadApi.SearchAsync(request, ct);
            return response.List.Pagination.TotalItems;
        }

        /// <summary>
        /// Mapira Alfresco Entry objekat u KdpDocumentStaging entitet
        /// </summary>
        private KdpDocumentStaging MapToKdpDocumentStaging(Entry entry)
        {
            var documentPath = entry.Path?.Name;
            var accFolderName = ExtractAccFolderFromPath(documentPath);
            var coreId = ExtractCoreId(accFolderName);

            // Extract CreatedDate from ecm:docCreationDate property
            var createdDate = GetDateTimePropertyValue(entry, "ecm:docCreationDate")                             
                              ?? null;

            var sysCreated = entry.CreatedAt.DateTime;


            return new KdpDocumentStaging
            {
                NodeId = entry.Id,
                DocumentName = entry.Name,
                DocumentPath = documentPath,
                ParentFolderId = entry.ParentId,
                ParentFolderName = ExtractParentFolderName(documentPath),
                DocumentType = GetPropertyValue(entry, "ecm:docType"),
                DocumentStatus = GetPropertyValue(entry, "ecm:docStatus"),
                CreatedDate = sysCreated,
                AccountNumbers = GetPropertyValue(entry, "ecm:bnkAccountNumber"),
                AccFolderName = accFolderName,
                CoreId = coreId,
                ProcessedDate = DateTime.Now,
                Source = GetPropertyValue(entry, "ecm:docSource"),
                Properties = entry.Properties == null ? null : JsonSerializer.Serialize(entry.Properties, new JsonSerializerOptions { WriteIndented = false }),
                MigrationCreationDate = createdDate
            };
        }

        /// <summary>
        /// Ekstrahuje vrednost property-ja iz Entry objekta
        /// </summary>
        private string? GetPropertyValue(Entry entry, string propertyName)
        {
            if (entry.Properties == null)
                return null;

            if (entry.Properties.TryGetValue(propertyName, out var value))
            {
                return value?.ToString();
            }

            return null;
        }

        /// <summary>
        /// Ekstrahuje DateTime vrednost property-ja iz Entry objekta
        /// </summary>
        private DateTime? GetDateTimePropertyValue(Entry entry, string propertyName)
        {
            if (entry.Properties == null)
                return null;

            if (entry.Properties.TryGetValue(propertyName, out var value))
            {
                if (value is DateTime dt)
                    return dt;

                if (DateTime.TryParse(value?.ToString(), out var parsedDate))
                    return parsedDate;
            }

            return null;
        }

        /// <summary>
        /// Ekstrahuje ACC folder iz putanje
        /// Primer: /Company Home/Sites/bank/documentLibrary/ACC-123456/DOSSIERS-FL/... -> ACC-123456
        /// </summary>
        private string? ExtractAccFolderFromPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var match = Regex.Match(path, @"ACC-\d+");
            return match.Success ? match.Value : null;
        }

        /// <summary>
        /// Ekstrahuje Core ID iz ACC folder name
        /// Primer: ACC-123456 -> 123456
        /// </summary>
        private string? ExtractCoreId(string? accFolderName)
        {
            if (string.IsNullOrEmpty(accFolderName))
                return null;

            return accFolderName.Replace("ACC-", "");
        }

        /// <summary>
        /// Ekstrahuje naziv parent foldera iz putanje
        /// </summary>
        private string? ExtractParentFolderName(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var parts = path.Split('/');
            return parts.Length > 1 ? parts[^2] : null;
        }
    }
}
