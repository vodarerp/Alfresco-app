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
        private readonly ILogger _logger;

        public KdpDocumentProcessingService(
            IAlfrescoReadApi alfrescoReadApi,
            IServiceScopeFactory scopeFactory,
            IOptions<MigrationOptions> options,
            ILoggerFactory logger)
        {
            _alfrescoReadApi = alfrescoReadApi ?? throw new ArgumentNullException(nameof(alfrescoReadApi));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger.CreateLogger("UiLogger");
        }

        /// <summary>
        /// Učitava sve KDP dokumente (00824 i 00099) iz Alfresca i puni staging tabelu
        /// Koristi hibridni pristup: parallel fetch + streaming insert za optimalno korišćenje memorije
        /// </summary>
        public async Task<int> LoadKdpDocumentsToStagingAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Pocetak učitavanja KDP dokumenata iz Alfresca (hibridni pristup)...");

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

               
                var skipValues = Enumerable
                    .Range(0, (totalCount + batchSize - 1) / batchSize)
                    .Select(i => i * batchSize)
                    .ToList();

                _logger.LogInformation("Broj batch-eva za obradu: {Count}", skipValues.Count);

               
                var pendingBatches = new ConcurrentQueue<List<KdpDocumentStaging>>();

               
                var dbWriteLock = new SemaphoreSlim(1, 1);

               
                const int batchThreshold = 5;

               
                var totalInserted = 0;

              

                var maxParallelism = kdpOptions.MaxDegreeOfParallelism > 0 ? kdpOptions.MaxDegreeOfParallelism : 3;
                

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxParallelism,
                    CancellationToken = ct
                };

                await Parallel.ForEachAsync(skipValues, parallelOptions, async (skipCount, token) =>
                {
                   
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
                            new() { Field = "cm:created", Ascending = true }
                        }
                    };

                    var response = await _alfrescoReadApi.SearchAsync(request, token);
                    var entries = response.List.Entries.Select(e => e.Entry).ToList();

                    if (entries.Count == 0) return;

                   
                    var stagingDocs = entries.Select(MapToKdpDocumentStaging).ToList();

                    
                    pendingBatches.Enqueue(stagingDocs);

                    _logger.LogDebug("Batch skip={Skip} učitan ({Count} dok.), queue size: {Size}",
                        skipCount, stagingDocs.Count, pendingBatches.Count);

                    
                    if (pendingBatches.Count >= batchThreshold)
                    {
                        // TryWait sa timeout 0 = ne blokiraj, samo proveri dostupnost
                        var acquired = await dbWriteLock.WaitAsync(0, token);

                        if (acquired)
                        {
                            
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

      
        public Task ExportToExcelAsync(string filePath, CancellationToken ct = default)
        {
            // TODO: Implementirati Excel export korišćenjem ClosedXML ili EPPlus
            // SELECT * FROM KdpExportResult -> Excel
            _logger.LogWarning("ExportToExcelAsync nije još implementirana");
            throw new NotImplementedException("Excel export će biti implementiran kasnije");
        }

       
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

        
        private string BuildKdpQuery()
        {
            var kdpOptions = _options.Value.KdpProcessing;
            var docTypes = kdpOptions.DocTypes;// ?? new List<string> { "00824", "00099" };

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

       
        private string? ExtractAccFolderFromPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var match = Regex.Match(path, @"ACC-\d+");
            return match.Success ? match.Value : null;
        }

       
        private string? ExtractCoreId(string? accFolderName)
        {
            if (string.IsNullOrEmpty(accFolderName))
                return null;

            return accFolderName.Replace("ACC-", "");
        }

       
        private string? ExtractParentFolderName(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var parts = path.Split('/');
            return parts.Length > 1 ? parts[^2] : null;
        }
    }
}
