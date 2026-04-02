using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Mapper;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.Request;
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
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    public class PreviewLoadService : IPreviewLoadService
    {
        private readonly IAlfrescoReadApi _alfrescoReadApi;
        private readonly IOptions<MigrationOptions> _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _dbLogger;
        private readonly ILogger _fileLogger;
        private readonly ILogger _uiLogger;
        private readonly IOpisToTipMapper _opisToTipMapper;

        // State tracking
        private Dictionary<string, string>? _dossierFolders;
        private int _currentFolderTypeIndex = 0;
        private long _totalDocumentsProcessed = 0;
        private long _totalFailed = 0;
        private int _batchCounter = 0;

        private ConcurrentDictionary<string, long> _fetchedCountsPerFolder = new();

        public PreviewLoadService(
            IAlfrescoReadApi alfrescoReadApi,
            IOptions<MigrationOptions> options,
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory,
            IOpisToTipMapper opisToTipMapper)
        {
            _alfrescoReadApi = alfrescoReadApi ?? throw new ArgumentNullException(nameof(alfrescoReadApi));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _opisToTipMapper = opisToTipMapper ?? throw new ArgumentNullException(nameof(opisToTipMapper));
            _dbLogger = loggerFactory.CreateLogger("DbLogger");
            _fileLogger = loggerFactory.CreateLogger("FileLogger");
            _uiLogger = loggerFactory.CreateLogger("UiLogger");
        }

        public async Task<bool> RunLoopAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback)
        {
            var sw = Stopwatch.StartNew();
            var batchSize = _options.Value.DocumentTypeDiscovery.BatchSize;
            var maxDocs = _options.Value.MaxDocumentsToProcess;

            _fileLogger.LogInformation("PreviewLoadService started");
            _uiLogger.LogInformation("PreviewLoadService started");

            await LoadCheckpointAsync(ct).ConfigureAwait(false);

            var progress = new WorkerProgress
            {
                TotalItems = maxDocs,
                ProcessedItems = _totalDocumentsProcessed,
                CurrentBatch = 0,
                BatchSize = batchSize,
                CurrentBatchCount = 0,
                SuccessCount = 0,
                FailedCount = 0,
                Message = "Starting preview load...",
                Timestamp = DateTimeOffset.UtcNow
            };
            progressCallback?.Invoke(progress);

            try
            {
                _dossierFolders = GetSubDossiersFolders();

                if (_dossierFolders == null || !_dossierFolders.Any())
                {
                    _uiLogger.LogWarning("PreviewLoadService: No dossier folders configured. Check RootPIFolderId/RootLEFolderId in appsettings.");
                    return false;
                }

                var folderTypes = _dossierFolders.Keys.OrderBy(k => k).ToList();

                for (int i = _currentFolderTypeIndex; i < folderTypes.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    if (maxDocs > 0 && Interlocked.Read(ref _totalDocumentsProcessed) >= maxDocs)
                    {
                        _fileLogger.LogInformation(
                            "PreviewLoadService: Reached MaxDocumentsToProcess={MaxDocs}. Total processed: {TotalProcessed}",
                            maxDocs, _totalDocumentsProcessed);
                        break;
                    }

                    var currFolderType = folderTypes[i];
                    _currentFolderTypeIndex = i;
                    var folderPath = _dossierFolders[currFolderType];

                    var startSkipCount = 0;
                    if (_fetchedCountsPerFolder.TryGetValue(currFolderType, out var fetchedCount))
                    {
                        startSkipCount = (int)fetchedCount;
                        _fileLogger.LogInformation(
                            "PreviewLoadService: Resuming {Type} from skipCount={StartSkip} ({FetchedCount} previously fetched)",
                            currFolderType, startSkipCount, fetchedCount);
                    }

                    await ParralelProccesDocumentsAsync(currFolderType, folderPath, startSkipCount, ct).ConfigureAwait(false);

                    // Save checkpoint after each folder type completes
                    var totalFetched = _fetchedCountsPerFolder.GetValueOrDefault(currFolderType, 0);
                    await SaveCheckpointAsync(currFolderType, totalFetched, ct).ConfigureAwait(false);

                    progressCallback?.Invoke(new WorkerProgress
                    {
                        TotalItems = maxDocs,
                        ProcessedItems = Interlocked.Read(ref _totalDocumentsProcessed),
                        CurrentBatch = _batchCounter,
                        BatchSize = batchSize,
                        SuccessCount = (int)Interlocked.Read(ref _totalDocumentsProcessed),
                        FailedCount = (int)Interlocked.Read(ref _totalFailed),
                        Message = $"Folder {currFolderType} done",
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }

                _fileLogger.LogInformation(
                    "PreviewLoadService: Completed. Total inserted: {Total}, failed: {Failed}, elapsed: {Elapsed}s",
                    _totalDocumentsProcessed, _totalFailed, sw.Elapsed.TotalSeconds);
                _uiLogger.LogInformation(
                    "PreviewLoadService: Done. Inserted={Total}, Failed={Failed}",
                    _totalDocumentsProcessed, _totalFailed);
            }
            catch (OperationCanceledException)
            {
                _fileLogger.LogWarning("PreviewLoadService: Cancelled after {Elapsed}s", sw.Elapsed.TotalSeconds);
                _uiLogger.LogWarning("PreviewLoadService: Cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex, "PreviewLoadService: Fatal error after {Elapsed}s", sw.Elapsed.TotalSeconds);
                _uiLogger.LogError("PreviewLoadService: Fatal error - {Message}", ex.Message);
                throw;
            }

            return true;
        }

        private async Task ParralelProccesDocumentsAsync(string currFolderType, string folderPath, int startSkipCount, CancellationToken ct)
        {
            var batchSize = _options.Value.DocumentTypeDiscovery.BatchSize;
            var maxParallelism = _options.Value.DocumentTypeDiscovery.MaxDegreeOfParallelism > 0
                ? _options.Value.DocumentTypeDiscovery.MaxDegreeOfParallelism
                : 5;
            var maxDocs = _options.Value.MaxDocumentsToProcess;
            var regex = new Regex($"^{Regex.Escape(currFolderType)}[0-9]", RegexOptions.IgnoreCase);
            var query = BuildPreviewSearchQuery(folderPath);
            var totalCount = await GetTotalDocumentCountAsync(query, ct).ConfigureAwait(false);

            _fileLogger.LogInformation(
                "PreviewLoadService DOSSIER-{Type}: totalCount={TotalCount}, batchSize={BatchSize}, parallelism={Parallelism}, startSkip={StartSkip}",
                currFolderType, totalCount, batchSize, maxParallelism, startSkipCount);

            if (totalCount == 0)
            {
                _fileLogger.LogInformation("PreviewLoadService DOSSIER-{Type}: No documents found, skipping", currFolderType);
                return;
            }

            if (startSkipCount >= totalCount)
            {
                _fileLogger.LogInformation(
                    "PreviewLoadService DOSSIER-{Type}: Already fully fetched (startSkip={StartSkip} >= totalCount={TotalCount}), skipping",
                    currFolderType, startSkipCount, totalCount);
                return;
            }

            var remainingInFolder = totalCount - startSkipCount;
            var remainingDocs = maxDocs > 0
                ? Math.Max(0, maxDocs - Interlocked.Read(ref _totalDocumentsProcessed))
                : remainingInFolder;
            var effectiveTotal = (int)Math.Min(remainingInFolder, remainingDocs > 0 ? remainingDocs : remainingInFolder);

            if (maxDocs > 0 && remainingDocs <= 0)
            {
                _fileLogger.LogInformation("PreviewLoadService DOSSIER-{Type}: MaxDocuments limit reached, skipping", currFolderType);
                return;
            }

            var skipValues = Enumerable
                .Range(0, (effectiveTotal + batchSize - 1) / batchSize)
                .Select(i => startSkipCount + i * batchSize)
                .ToList();

            _fileLogger.LogInformation(
                "PreviewLoadService DOSSIER-{Type}: {BatchCount} batches to process (effectiveTotal={EffectiveTotal} of {TotalCount})",
                currFolderType, skipValues.Count, effectiveTotal, totalCount);

            var pendingBatches = new ConcurrentQueue<List<PreviewDocStaging>>();
            var dbWriteLock = new SemaphoreSlim(1, 1);
            const int batchThreshold = 5;

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = ct
            };

            await Parallel.ForEachAsync(skipValues, parallelOptions, async (skipCount, token) =>
            {
                if (maxDocs > 0 && Interlocked.Read(ref _totalDocumentsProcessed) >= maxDocs)
                    return;

                try
                {
                    var searchResult = await SearchDocumentsAsync(folderPath, skipCount, batchSize, token).ConfigureAwait(false);

                    searchResult.Documents.RemoveAll(o =>
                    {
                        var lastParentName = o.Entry.Path?.Elements?.LastOrDefault()?.Name;

                        // Brišemo element ako je ime null ILI ako se NE poklapa sa regexom
                        return lastParentName == null || !regex.IsMatch(lastParentName);
                    });
                    //searchResult.Documents.Where(o =>
                    //{
                    //    var lastParentName = o.Entry.Path?.Elements?.LastOrDefault()?.Name;
                    //    return lastParentName != null && regex.IsMatch(lastParentName);
                    //}).ToList();

                    var fetchedInBatch = searchResult.Documents.Count;
                    if (fetchedInBatch > 0)
                    {
                        _fetchedCountsPerFolder.AddOrUpdate(currFolderType, fetchedInBatch, (_, existing) => existing + fetchedInBatch);
                    }

                    if (fetchedInBatch == 0) return;

                    // Trim to remaining maxDocs budget
                    var docs = searchResult.Documents;
                    if (maxDocs > 0)
                    {
                        var remaining = maxDocs - Interlocked.Read(ref _totalDocumentsProcessed);
                        if (remaining <= 0) return;
                        if (docs.Count > remaining)
                            docs = docs.Take((int)remaining).ToList();
                    }

                    // Apply mapping
                    var docsToInsert = new List<PreviewDocStaging>();
                    foreach (var doc in docs)
                    {
                        try
                        {
                            var previewDoc = await ApplyPreviewDocumentMappingAsync(doc.Entry, currFolderType, token).ConfigureAwait(false);
                            docsToInsert.Add(previewDoc);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref _totalFailed);
                            _fileLogger.LogWarning(
                                "PreviewLoadService DOSSIER-{Type} skip={Skip}: Mapping failed for {Name}: {Error}",
                                currFolderType, skipCount, doc.Entry.Name, ex.Message);
                        }
                    }

                    if (docsToInsert.Count == 0) return;

                    pendingBatches.Enqueue(docsToInsert);

                    _fileLogger.LogInformation(
                        "PreviewLoadService DOSSIER-{Type} skip={Skip}: {DocsCount} docs queued",
                        currFolderType, skipCount, docsToInsert.Count);

                    if (pendingBatches.Count >= batchThreshold)
                    {
                        var acquired = await dbWriteLock.WaitAsync(0, token).ConfigureAwait(false);
                        if (acquired)
                        {
                            try
                            {
                                await FlushPendingBatchesAsync(pendingBatches, currFolderType, token).ConfigureAwait(false);
                                await SaveCheckpointAsync(currFolderType, _fetchedCountsPerFolder.GetValueOrDefault(currFolderType, 0), token).ConfigureAwait(false);
                            }
                            finally
                            {
                                dbWriteLock.Release();
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _fileLogger.LogError(
                        "PreviewLoadService DOSSIER-{Type} skip={Skip}: Batch error: {Error}",
                        currFolderType, skipCount, ex.Message);
                }
            }).ConfigureAwait(false);

            // Final flush
            if (!pendingBatches.IsEmpty)
            {
                _fileLogger.LogInformation("PreviewLoadService DOSSIER-{Type}: Final flush - {Count} batches remaining",
                    currFolderType, pendingBatches.Count);
                await FlushPendingBatchesAsync(pendingBatches, currFolderType, ct).ConfigureAwait(false);
            }

            _fileLogger.LogInformation(
                "PreviewLoadService DOSSIER-{Type}: Parallel processing done. Running total: {TotalDocs} docs",
                currFolderType, _totalDocumentsProcessed);
        }

        private async Task FlushPendingBatchesAsync(ConcurrentQueue<List<PreviewDocStaging>> pendingBatches, string folderType, CancellationToken ct)
        {
            var allDocs = new List<PreviewDocStaging>();
            while (pendingBatches.TryDequeue(out var batch))
                allDocs.AddRange(batch);

            if (allDocs.Count == 0) return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                //var inserted = await repo.InsertBatchAsync(allDocs, ct).ConfigureAwait(false);
                var inserted = await repo.InsertManyAsync(allDocs, ct).ConfigureAwait(false);
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                Interlocked.Add(ref _totalDocumentsProcessed, inserted);
                Interlocked.Increment(ref _batchCounter);

                _fileLogger.LogInformation(
                    "PreviewLoadService DOSSIER-{Type}: Flushed {DocCount} docs to PreviewDocStaging",
                    folderType, inserted);
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("PreviewLoadService: Failed to flush batch: {Error}", ex.Message);
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        private async Task<PreviewDocStaging> ApplyPreviewDocumentMappingAsync(Entry alfrescoEntry, string folderType, CancellationToken ct)
        {
            var doc = new PreviewDocStaging
            {
                NodeId = alfrescoEntry.Id,
                Name = alfrescoEntry.Name,
                NodeType = alfrescoEntry.NodeType,
                Status = "PENDING",
                DossierType = folderType,
                DossierDestinationFolderIsCreated = 0,
                RecordInserted = DateTime.UtcNow
            };

            // Extract Alfresco properties
            string? docDesc = null;
            string? existingDocType = null;
            string? existingStatus = null;
            string? coreIdFromDoc = null;
            string? docDossierType = null;
            string? docClientType = null;
            string? sourceFromDoc = null;
            DateTime? docCreationDate = null;
            string? contractNumber = null;
            string? productType = null;
            string? accountNumbers = null;
            string? jsonProperties = null;
            if (alfrescoEntry.Properties != null)
            {
                string GetStr(string key) => alfrescoEntry!.Properties!.TryGetValue(key, out var value) ? value?.ToString() ?? "" : "";

                docDesc = GetStr("ecm:docDesc");
                existingDocType = GetStr("ecm:docType");
                existingStatus = GetStr("ecm:docStatus");
                coreIdFromDoc = GetStr("ecm:coreId");
                docDossierType = GetStr("ecm:docDossierType");
                docClientType = GetStr("ecm:docClientType");
                sourceFromDoc = GetStr("ecm:source");
                contractNumber = GetStr("ecm:bnkNumberOfContract");
                productType = GetStr("ecm:bnkTypeOfProduct");
                accountNumbers = GetStr("ecm:bnkAccountNumber");
                jsonProperties = JsonSerializer.Serialize(alfrescoEntry.Properties);
                //if (alfrescoEntry.Properties.TryGetValue("ecm:docDesc", out var v)) docDesc = v?.ToString();
                //if (alfrescoEntry.Properties.TryGetValue("ecm:docType", out var v2)) existingDocType = v2?.ToString();
                //if (alfrescoEntry.Properties.TryGetValue("ecm:docStatus", out var v3)) existingStatus = v3?.ToString();
                //if (alfrescoEntry.Properties.TryGetValue("ecm:coreId", out var v4)) coreIdFromDoc = v4?.ToString();
                //if (alfrescoEntry.Properties.TryGetValue("ecm:docDossierType", out var v5)) docDossierType = v5?.ToString();
                //if (alfrescoEntry.Properties.TryGetValue("ecm:docClientType", out var v6)) docClientType = v6?.ToString();
                //if (alfrescoEntry.Properties.TryGetValue("ecm:source", out var v7)) sourceFromDoc = v7?.ToString();
                //if (alfrescoEntry.Properties.TryGetValue("ecm:bnkNumberOfContract", out var v8)) contractNumber = v8?.ToString();
                //if (alfrescoEntry.Properties.TryGetValue("ecm:bnkTypeOfProduct", out var v9)) productType = v9?.ToString();
                //if (alfrescoEntry.Properties.TryGetValue("ecm:bnkAccountNumber", out var v10)) accountNumbers = v10?.ToString();
                if (alfrescoEntry.Properties.TryGetValue("ecm:datumKreiranja", out var v11))
                {
                    if (v11 is DateTime dt) docCreationDate = dt;
                    else if (DateTime.TryParse(v11?.ToString(), out var parsed)) docCreationDate = parsed;
                }
            }

            // Extract parent folder name from path
            var parentFolderName = alfrescoEntry.Path?.Elements?.LastOrDefault()?.Name;
            var parentFolderId = alfrescoEntry.Path?.Elements?.LastOrDefault()?.Id ?? alfrescoEntry.ParentId;

            doc.Properties = jsonProperties;
            doc.ParentFolderName = parentFolderName;
            doc.ParentId = parentFolderId;
            doc.DocDescription = docDesc;
            doc.OriginalDocumentCode = existingDocType;
            doc.OldAlfrescoStatus = existingStatus;
            doc.ContractNumber = contractNumber;
            doc.AccountNumbers = accountNumbers;
            doc.OriginalCreatedAt = docCreationDate ?? alfrescoEntry.CreatedAt.DateTime;
            doc.OriginalDocumentName = alfrescoEntry.Name;
            doc.ClientSegment = docClientType;

            // CoreId: from doc property, or extract from parent folder name
            doc.CoreId = coreIdFromDoc;
            if (string.IsNullOrWhiteSpace(doc.CoreId) && !string.IsNullOrWhiteSpace(parentFolderName))
                doc.CoreId = DossierIdFormatter.ExtractCoreId(parentFolderName);

            // Apply mapping
            DocumentMapping? fullMapping = null;
            if (!string.IsNullOrWhiteSpace(docDesc))
                fullMapping = await _opisToTipMapper.GetFullMappingAsync(docDesc, existingDocType, ct).ConfigureAwait(false);

            var tipDosijea = fullMapping?.TipDosijea ?? docDossierType ?? "";
            doc.DocumentType = fullMapping?.SifraDokumentaMigracija ?? existingDocType;
            doc.DocumentTypeMigration = fullMapping?.SifraDokumentaMigracija;
            doc.NewDocumentName = fullMapping?.NazivDokumentaMigracija ?? string.Empty;
            doc.ProductType = fullMapping?.TipProizvoda ?? productType;
            doc.CategoryCode = fullMapping?.OznakaKategorije;
            doc.CategoryName = fullMapping?.NazivKategorije;

            // Status
            var statusInfo = DocumentStatusDetectorV3.DetermineStatus(fullMapping, existingStatus);
            doc.IsActive = statusInfo.IsActive ? 1 : 0;
            doc.NewAlfrescoStatus = statusInfo.Status;
            doc.NewDocumentCode = statusInfo.MappingCode;
            if (string.IsNullOrWhiteSpace(doc.OriginalDocumentCode))
                doc.OriginalDocumentCode = statusInfo.OriginalCode;

            // Destination dossier type
            var destinationType = DestinationRootFolderDeterminator.DetermineAndResolve(
                doc.DocumentType,
                tipDosijea,
                doc.ClientSegment);

            // Fallback from parent folder name prefix
            if (destinationType == DossierType.Unknown && !string.IsNullOrWhiteSpace(parentFolderName))
            {
                var prefix = DossierIdFormatter.ExtractPrefix(parentFolderName);
                destinationType = prefix.ToUpperInvariant() switch
                {
                    "PI" => DossierType.ClientFL,
                    "FL" => DossierType.ClientFL,
                    "LE" => DossierType.ClientPL,
                    "PL" => DossierType.ClientPL,
                    _ => DossierType.Unknown
                };
            }

            doc.TargetDossierType = ((int)destinationType).ToString();
            doc.Source = sourceFromDoc ?? SourceDetector.GetSource(destinationType);

            // Destination folder name (used by Faza 2 to find/create the folder)
            if (!string.IsNullOrWhiteSpace(parentFolderName))
            {
                string? productTypeToUse = doc.ProductType;
                if (destinationType == DossierType.Deposit)
                    productTypeToUse = DossierIdFormatter.MapClientSegmentToProductType(doc.ClientSegment);

                doc.DossierDestinationFolderName = DossierIdFormatter.ConvertForTargetType(
                    parentFolderName,
                    (int)destinationType,
                    contractNumber,
                    productTypeToUse,
                    doc.CoreId,
                    doc.OriginalCreatedAt);
            }

            return doc;
        }

        private string BuildPreviewSearchQuery(string ancestorId)
        {
            

            var query = $"ANCESTOR:\"{ancestorId}\" " +
                        $"AND TYPE:\"cm:content\"";

            // Add date filter if enabled
            if (_options.Value.DocumentTypeDiscovery.UseDateFilter)
            {
                var dateFrom = _options.Value.DocumentTypeDiscovery.DateFrom;
                var dateTo = _options.Value.DocumentTypeDiscovery.DateTo;

                if (!string.IsNullOrWhiteSpace(dateFrom) && !string.IsNullOrWhiteSpace(dateTo))
                {
                    // Parse and format date
                    if (DateTime.TryParse(dateFrom, out var fromDate) && DateTime.TryParse(dateTo, out var toDate))
                    {
                        //query += $" AND ecm\\:docCreationDate:[{fromDate:yyyy-MM-dd} TO {toDate:yyyy-MM-dd}]";
                        query += $" AND cm\\:created:[{fromDate:yyyy-MM-dd} TO {toDate:yyyy-MM-dd}]";
                    }
                }
            }

                return query;
        }

        private async Task<int> GetTotalDocumentCountAsync(string query, CancellationToken ct)
        {
            var request = new PostSearchRequest
            {
                Query = new QueryRequest { Query = query, Language = "afts" },
                Paging = new PagingRequest { MaxItems = 1, SkipCount = 0 }
            };
            var response = await _alfrescoReadApi.SearchAsync(request, ct).ConfigureAwait(false);
            return response.List.Pagination.TotalItems;
        }

        private async Task<(List<ListEntry> Documents, bool HasMore)> SearchDocumentsAsync(
            string ancestorId, int skipCount, int maxItems, CancellationToken ct)
        {
            var query = BuildPreviewSearchQuery(ancestorId);

            _fileLogger.LogInformation("PreviewLoadService AFTS Query: {Query}, Skip: {Skip}, Max: {Max}", query, skipCount, maxItems);

            var req = new PostSearchRequest
            {
                Query = new QueryRequest { Language = "afts", Query = query },
                Paging = new PagingRequest { MaxItems = maxItems, SkipCount = skipCount },
                Sort = new List<SortRequest>
                {
                    new SortRequest { Type = "FIELD", Field = "created", Ascending = true },
                    new SortRequest { Type = "FIELD", Field = "name", Ascending = true }
                },
                Include = new[] { "properties", "path" }
            };

            var response = await _alfrescoReadApi.SearchAsync(req, ct).ConfigureAwait(false);
            var documents = response?.List?.Entries ?? new List<ListEntry>();
            var hasMore = response?.List?.Pagination?.HasMoreItems ?? false;

            return (documents, hasMore);
        }

        private Dictionary<string, string> GetSubDossiersFolders()
        {
            var result = new Dictionary<string, string>();

            if (!string.IsNullOrWhiteSpace(_options.Value.RootPIFolderId))
                result["PI"] = _options.Value.RootPIFolderId!;

            if (!string.IsNullOrWhiteSpace(_options.Value.RootLEFolderId))
                result["LE"] = _options.Value.RootLEFolderId!;

            return result;
        }

        private async Task LoadCheckpointAsync(CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IPreviewLoadCheckpointRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            try
            {
                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                var piFetched = await repo.GetFetchedCountAsync("PI", ct).ConfigureAwait(false);
                var leFetched = await repo.GetFetchedCountAsync("LE", ct).ConfigureAwait(false);

                if (piFetched > 0)
                {
                    _fetchedCountsPerFolder["PI"] = piFetched;
                    _fileLogger.LogInformation("PreviewLoadService checkpoint: PI={PiFetched} previously fetched", piFetched);
                }

                if (leFetched > 0)
                {
                    _fetchedCountsPerFolder["LE"] = leFetched;
                    _fileLogger.LogInformation("PreviewLoadService checkpoint: LE={LeFetched} previously fetched", leFetched);
                }
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

            }
            catch (Exception ex)
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);

                _fileLogger.LogWarning("PreviewLoadService: Could not load checkpoint, starting fresh. Error: {Error}", ex.Message);
            }
        }

        private async Task SaveCheckpointAsync(string folderType, long totalFetched, CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<IPreviewLoadCheckpointRepository>();
                await repo.UpsertAsync(folderType, totalFetched, ct).ConfigureAwait(false);

                _fileLogger.LogInformation("PreviewLoadService checkpoint saved: {FolderType}={TotalFetched}", folderType, totalFetched);
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning("PreviewLoadService: Could not save checkpoint for {FolderType}: {Error}", folderType, ex.Message);
            }
        }

        #region Not Implemented
        public Task<DocumentSearchBatchResult> RunBatchAsync(CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task<bool> RunLoopAsync(CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
