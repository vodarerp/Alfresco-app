using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Extensions;
using Alfresco.Contracts.Mapper;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.Request;
using Mapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Abstraction.Models;
using SqlServer.Abstraction.Interfaces;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Migration.Infrastructure.Implementation.Services
{
    
    public class DocumentSearchService : IDocumentSearchService
    {
        private readonly IAlfrescoReadApi _alfrescoReadApi;
        private readonly IOptions<MigrationOptions> _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _dbLogger;
        private readonly ILogger _fileLogger;
        private readonly ILogger _uiLogger;
        private readonly IOpisToTipMapper _opisToTipMapper;
        private readonly IClientApi? _clientApi;

        // State tracking
        private Dictionary<string, string>? _dossierFolders;  // { "PI" -> "workspace://SpacesStore/xxx", ... }
        private int _currentFolderTypeIndex = 0;
        private int _currentSkipCount = 0;
        private long _totalDocumentsProcessed = 0;
        private long _totalFoldersInserted = 0;
        private long _totalFailed = 0;
        private int _batchCounter = 0;

        // Cache for processed folders (to avoid duplicate API calls within session)
        private readonly ConcurrentDictionary<string, FolderStaging> _folderCache = new();

        // Runtime override for DocTypes (takes precedence over appsettings)
        private List<string>? _docTypesOverride = null;

        private const string ServiceName = "DocumentSearch";

        public DocumentSearchService(
            IAlfrescoReadApi alfrescoReadApi,
            IOptions<MigrationOptions> options,
            IServiceScopeFactory scopeFactory,
            ILoggerFactory logger,
            IOpisToTipMapper opisToTipMapper,
            IClientApi? clientApi = null)
        {
            _alfrescoReadApi = alfrescoReadApi ?? throw new ArgumentNullException(nameof(alfrescoReadApi));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _opisToTipMapper = opisToTipMapper ?? throw new ArgumentNullException(nameof(opisToTipMapper));
            _clientApi = clientApi;

            _dbLogger = logger.CreateLogger("DbLogger");
            _fileLogger = logger.CreateLogger("FileLogger");
            _uiLogger = logger.CreateLogger("UiLogger");
        }

        public void SetDocTypes(List<string> docTypes)
        {
            _docTypesOverride = docTypes;
            _fileLogger.LogInformation("DocTypes override set: {DocTypes}", string.Join(", ", docTypes));
        }

        public List<string> GetCurrentDocTypes()
        {
            return _docTypesOverride ?? _options.Value.DocumentTypeDiscovery.DocTypes;
        }

        public async Task<DocumentSearchBatchResult> RunBatchAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var result = new DocumentSearchBatchResult();

            using var batchScope = _fileLogger.BeginScope(new Dictionary<string, object>
            {
                ["Service"] = nameof(DocumentSearchService),
                ["Operation"] = "RunBatch"
            });

            var batchSize = _options.Value.DocumentTypeDiscovery.BatchSize;
            var docTypes = _docTypesOverride ?? _options.Value.DocumentTypeDiscovery.DocTypes;

            _fileLogger.LogInformation("DocumentSearch batch started - BatchSize: {BatchSize}, DocTypes: {DocTypes}",
                batchSize, string.Join(", ", docTypes));

            // Initialize DOSSIER folders if not done yet
            if (_dossierFolders == null)
            {
                _dossierFolders = await FindDossierSubfoldersAsync(ct).ConfigureAwait(false);

                if (_dossierFolders.Count == 0)
                {
                    _fileLogger.LogWarning("No DOSSIER subfolders found matching criteria");
                    return result;
                }

                _fileLogger.LogInformation("Found {Count} DOSSIER subfolders: {Types}",
                    _dossierFolders.Count, string.Join(", ", _dossierFolders.Keys));
            }

            // Get current DOSSIER folder to process
            var folderTypes = _dossierFolders.Keys.OrderBy(k => k).ToList();
            if (_currentFolderTypeIndex >= folderTypes.Count)
            {
                _fileLogger.LogInformation("All DOSSIER subfolders processed");
                return result;
            }

            var currentType = folderTypes[_currentFolderTypeIndex];
            var currentFolderId = _dossierFolders[currentType];

            _fileLogger.LogDebug("Processing DOSSIER-{Type} folder, SkipCount: {Skip}", currentType, _currentSkipCount);

            // Search documents by ecm:docType
            var searchResult = await SearchDocumentsByTypeAsync(currentFolderId, docTypes, _currentSkipCount, batchSize, ct)
                .ConfigureAwait(false);

            if (currentType == "D") currentType = "DE";
            var regex = new Regex($"^{Regex.Escape(currentType)}[0-9]", RegexOptions.IgnoreCase);
            var finalDocuments = searchResult.Documents.Where(o =>
                                                        {
                                                            var lastParentName = o.Entry.Path?.Elements?.LastOrDefault()?.Name;
                                                            return lastParentName != null && regex.IsMatch(lastParentName);
                                                        }).ToList();



            result.DocumentsFound = finalDocuments.Count;
            result.HasMore = searchResult.HasMore;

            if (finalDocuments.Count == 0)
            {
                _fileLogger.LogInformation("No documents found in DOSSIER-{Type}, moving to next folder", currentType);

                // Move to next folder type
                _currentFolderTypeIndex++;
                _currentSkipCount = 0;

                // Recursively process next folder
                if (_currentFolderTypeIndex < folderTypes.Count)
                {
                    return await RunBatchAsync(ct).ConfigureAwait(false);
                }

                return result;
            }

            _fileLogger.LogInformation("Found {Count} documents in DOSSIER-{Type}", finalDocuments.Count, currentType);

            // Extract unique parent folders from documents
            var uniqueFolders = ExtractUniqueFolders(finalDocuments, currentType);
            result.FoldersFound = uniqueFolders.Count;

            _fileLogger.LogInformation("Extracted {Count} unique folders from batch", uniqueFolders.Count);

            // Insert folders (ignore duplicates)
            var foldersInserted = await InsertFoldersAsync(uniqueFolders.Values.ToList(), ct).ConfigureAwait(false);
            result.FoldersInserted = foldersInserted;
            Interlocked.Add(ref _totalFoldersInserted, foldersInserted);

            // Process documents - apply mapping and insert
            var docsToInsert = new List<DocStaging>();
            foreach (var doc in finalDocuments)
            {
                var parentFolderId = GetParentFolderIdFromPath(doc.Entry);
                if (string.IsNullOrEmpty(parentFolderId))
                {
                    _fileLogger.LogWarning("Could not extract parent folder for document {Name}", doc.Entry.Name);
                    continue;
                }

                // Get folder info from cache or uniqueFolders
                if (!uniqueFolders.TryGetValue(parentFolderId, out var folder))
                {
                    _folderCache.TryGetValue(parentFolderId, out folder);
                }

                if (folder == null)
                {
                    _fileLogger.LogWarning("Folder not found for document {Name}, parentId: {ParentId}",
                        doc.Entry.Name, parentFolderId);
                    continue;
                }

                // Convert to DocStaging and apply mapping
                var docStaging = doc.Entry.ToDocStagingInsert();
                docStaging.Status = MigrationStatus.Ready.ToDbString();
                docStaging.ToPath = string.Empty;

                await ApplyDocumentMappingAsync(docStaging, folder, doc.Entry, ct).ConfigureAwait(false);

                docsToInsert.Add(docStaging);
            }

            // Insert documents
            var docsInserted = await InsertDocumentsAsync(docsToInsert, ct).ConfigureAwait(false);
            result.DocumentsInserted = docsInserted;
            Interlocked.Add(ref _totalDocumentsProcessed, docsInserted);

            // Update skip count for next batch
            if (searchResult.HasMore)
            {
                _currentSkipCount += batchSize;
            }
            else
            {
                // Move to next folder type
                _currentFolderTypeIndex++;
                _currentSkipCount = 0;
            }

            Interlocked.Increment(ref _batchCounter);

            sw.Stop();
            _fileLogger.LogInformation(
                "DocumentSearch batch completed: {DocsFound} docs found, {DocsInserted} inserted, " +
                "{FoldersFound} folders found, {FoldersInserted} inserted in {Elapsed}ms " +
                "(Total: {TotalDocs} docs, {TotalFolders} folders)",
                result.DocumentsFound, result.DocumentsInserted,
                result.FoldersFound, result.FoldersInserted, sw.ElapsedMilliseconds,
                _totalDocumentsProcessed, _totalFoldersInserted);

            return result;
        }

        public async Task<bool> RunLoopAsync(CancellationToken ct)
        {
            return await RunLoopAsync(ct, null).ConfigureAwait(false);
        }

        public async Task<bool> RunLoopAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback)
        {
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;
            var batchSize = _options.Value.DocumentTypeDiscovery.BatchSize;
            var completedSuccessfully = false;

            _fileLogger.LogInformation("DocumentSearch service started - IdleDelay: {IdleDelay}ms, MaxEmptyResults: {MaxEmptyResults}",
                delay, maxEmptyResults);
            _dbLogger.LogInformation("DocumentSearch service started");
            _uiLogger.LogInformation("Document Search (by docType) started");

            // Initial progress report
            var progress = new WorkerProgress
            {
                TotalItems = 0,
                ProcessedItems = _totalDocumentsProcessed,
                BatchSize = batchSize,
                CurrentBatch = 0,
                Message = "Starting document search by ecm:docType..."
            };
            progressCallback?.Invoke(progress);

            while (!ct.IsCancellationRequested)
            {
                using var batchScope = _fileLogger.BeginScope(new Dictionary<string, object>
                {
                    ["BatchCounter"] = _batchCounter + 1
                });

                try
                {
                    _fileLogger.LogDebug("Starting batch {BatchCounter}", _batchCounter + 1);

                    var result = await RunBatchAsync(ct).ConfigureAwait(false);

                    // Update progress
                    progress.ProcessedItems = _totalDocumentsProcessed;
                    progress.CurrentBatch = _batchCounter;
                    progress.CurrentBatchCount = result.DocumentsInserted;
                    progress.SuccessCount = result.DocumentsInserted;
                    progress.FailedCount = (int)_totalFailed;
                    progress.Timestamp = DateTimeOffset.UtcNow;
                    progress.Message = result.DocumentsFound > 0
                        ? $"Processed {result.DocumentsInserted} documents, {result.FoldersInserted} new folders in batch {_batchCounter}"
                        : "No more documents to process";

                    progressCallback?.Invoke(progress);

                    if (result.DocumentsFound == 0)
                    {
                        emptyResultCounter++;
                        _fileLogger.LogDebug("Empty result ({Counter}/{Max})", emptyResultCounter, maxEmptyResults);

                        if (emptyResultCounter >= maxEmptyResults)
                        {
                            _fileLogger.LogInformation("Breaking after {Count} consecutive empty results", emptyResultCounter);
                            _dbLogger.LogInformation("Breaking after {Count} consecutive empty results", emptyResultCounter);

                            progress.Message = $"Completed: {_totalDocumentsProcessed} documents, {_totalFoldersInserted} folders processed";
                            progressCallback?.Invoke(progress);
                            completedSuccessfully = true;
                            break;
                        }

                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        emptyResultCounter = 0;

                        var betweenDelay = _options.Value.DocumentTypeDiscovery.DelayBetweenBatchesInMs;
                        if (betweenDelay > 0)
                        {
                            await Task.Delay(betweenDelay, ct).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _fileLogger.LogInformation("DocumentSearch service cancelled by user");
                    _dbLogger.LogInformation("DocumentSearch service cancelled");
                    _uiLogger.LogInformation("Document Search cancelled");
                    progress.Message = $"Cancelled after processing {_totalDocumentsProcessed} documents";
                    progressCallback?.Invoke(progress);
                    throw;
                }
                catch (Exception ex)
                {
                    _fileLogger.LogError("Critical error in batch {BatchCounter}: {Error}", _batchCounter, ex.Message);
                    _dbLogger.LogError(ex, "Error in batch {BatchCounter}", _batchCounter);
                    _uiLogger.LogError("Error in batch {BatchCounter}", _batchCounter);

                    progress.Message = $"Error in batch {_batchCounter}: {ex.Message}";
                    progressCallback?.Invoke(progress);

                    await Task.Delay(delay * 2, ct).ConfigureAwait(false);
                }
            }

            _fileLogger.LogInformation(
                "DocumentSearch service completed after {Count} batches. Total: {Docs} documents, {Folders} folders",
                _batchCounter, _totalDocumentsProcessed, _totalFoldersInserted);
            _dbLogger.LogInformation(
                "DocumentSearch service completed - Total: {Docs} documents, {Folders} folders",
                _totalDocumentsProcessed, _totalFoldersInserted);
            _uiLogger.LogInformation("Document Search completed: {Docs} documents processed", _totalDocumentsProcessed);

            return completedSuccessfully;
        }

        #region Private Methods

        /// <summary>
        /// Finds DOSSIER-{type} subfolders in the root discovery folder
        /// </summary>
        private async Task<Dictionary<string, string>> FindDossierSubfoldersAsync(CancellationToken ct)
        {
            var rootId = _options.Value.RootDiscoveryFolderId;
            var folderTypes = _options.Value.DocumentTypeDiscovery.FolderTypes;

            _fileLogger.LogDebug("Finding DOSSIER subfolders in root {RootId}", rootId);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var skipCount = 0;
            const int pageSize = 100;
            const string dossierPrefix = "DOSSIERS-";

            var safeRootId = SanitizeAFTS(rootId);
            var query = $"PARENT:\"{safeRootId}\" AND TYPE:\"cm:folder\" AND =cm:name:{dossierPrefix}*";

            while (true)
            {
                var req = new PostSearchRequest
                {
                    Query = new QueryRequest
                    {
                        Language = "afts",
                        Query = query
                    },
                    Paging = new PagingRequest
                    {
                        MaxItems = pageSize,
                        SkipCount = skipCount
                    },
                    Sort = new List<SortRequest>
                    {
                        new SortRequest { Type = "FIELD", Field = "cm:name", Ascending = true }
                    }
                };

                var response = await _alfrescoReadApi.SearchAsync(req, ct).ConfigureAwait(false);
                var folders = response?.List?.Entries ?? new List<ListEntry>();

                if (folders.Count == 0)
                    break;

                foreach (var folder in folders)
                {
                    var folderName = folder.Entry?.Name;
                    if (string.IsNullOrEmpty(folderName) || !folderName.StartsWith(dossierPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var type = folderName.Substring(dossierPrefix.Length);

                    // Filter by configured folder types
                    if (folderTypes != null && folderTypes.Count > 0)
                    {
                        if (!folderTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                            continue;
                    }

                    var folderId = $"workspace://SpacesStore/{folder.Entry?.Id}";
                    result[type] = folderId;
                    _fileLogger.LogDebug("Added DOSSIER-{Type} folder: {FolderId}", type, folderId);
                }

                if (folders.Count < pageSize)
                    break;

                skipCount += pageSize;
            }

            return result;
        }

        /// <summary>
        /// Searches documents by ecm:docType within a DOSSIER folder
        /// </summary>
        private async Task<(List<ListEntry> Documents, bool HasMore)> SearchDocumentsByTypeAsync(
            string ancestorId,
            List<string> docTypes,
            int skipCount,
            int maxItems,
            CancellationToken ct)
        {
            var query = BuildDocumentSearchQuery(ancestorId, docTypes);

            _fileLogger.LogDebug("AFTS Query: {Query}, Skip: {Skip}, Max: {Max}", query, skipCount, maxItems);

            var req = new PostSearchRequest
            {
                Query = new QueryRequest
                {
                    Language = "afts",
                    Query = query
                },
                Paging = new PagingRequest
                {
                    MaxItems = maxItems,
                    SkipCount = skipCount
                },
                Sort = new List<SortRequest>
                {
                    new SortRequest { Type = "FIELD", Field = "cm:created", Ascending = true },
                    new SortRequest { Type = "FIELD", Field = "cm:name", Ascending = true }
                },
                Include = new[] { "properties", "path" }  // Include path for parent folder extraction
            };

            var response = await _alfrescoReadApi.SearchAsync(req, ct).ConfigureAwait(false);
            var documents = response?.List?.Entries ?? new List<ListEntry>();            
            var hasMore = response?.List?.Pagination?.HasMoreItems ?? false;

            return (documents, hasMore);
        }

        /// <summary>
        /// Builds AFTS query for searching documents by ecm:docType
        /// </summary>
        private string BuildDocumentSearchQuery(string ancestorId, List<string> docTypes)
        {
            // Build: (=ecm\:docType:"00099" OR =ecm\:docType:"00824")
            var docTypeConditions = string.Join(" OR ",
                docTypes.Select(t => $"=ecm\\:docType:\"{t}\""));

            var query = $"({docTypeConditions}) " +
                        $"AND ANCESTOR:\"{ancestorId}\" " +
                        $"AND TYPE:\"cm:content\"";

            // Add date filter if enabled
            if (_options.Value.DocumentTypeDiscovery.UseDateFilter)
            {
                var dateFrom = _options.Value.DocumentTypeDiscovery.DateFrom;
                var dateTo = _options.Value.DocumentTypeDiscovery.DateTo;

                if (!string.IsNullOrWhiteSpace(dateFrom))
                {
                    // Parse and format date
                    if (DateTime.TryParse(dateFrom, out var fromDate))
                    {
                        query += $" AND ecm\\:docCreationDate:[{fromDate:yyyy-MM-dd} TO MAX]";
                    }
                }

                if (!string.IsNullOrWhiteSpace(dateTo))
                {
                    if (DateTime.TryParse(dateTo, out var toDate))
                    {
                        query += $" AND ecm\\:docCreationDate:[MIN TO {toDate:yyyy-MM-dd}]";
                    }
                }
            }

            return query;
        }

        /// <summary>
        /// Extracts unique parent folders from a batch of documents
        /// </summary>
        private Dictionary<string, FolderStaging> ExtractUniqueFolders(List<ListEntry> documents, string dossierType)
        {
            var uniqueFolders = new Dictionary<string, FolderStaging>();

            foreach (var doc in documents)
            {
                var parentFolderId = GetParentFolderIdFromPath(doc.Entry);
                var parentFolderName = GetParentFolderNameFromPath(doc.Entry);

                if (string.IsNullOrEmpty(parentFolderId))
                {
                    // Fallback to ParentId if path not available
                    parentFolderId = doc.Entry.ParentId;
                }

                if (string.IsNullOrEmpty(parentFolderId))
                    continue;

                // Skip if already in cache
                if (_folderCache.ContainsKey(parentFolderId))
                    continue;

                // Skip if already processed in this batch
                if (uniqueFolders.ContainsKey(parentFolderId))
                    continue;

                // Create FolderStaging from path info
                var folder = new FolderStaging
                {
                    NodeId = parentFolderId,
                    Name = parentFolderName ?? "Unknown",
                    Status = MigrationStatus.Ready.ToDbString(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    ClientType = DetermineClientTypeFromDossierType(dossierType),
                    ClientSegment = dossierType
                };

                // Try to extract CoreId from folder name
                if (!string.IsNullOrEmpty(parentFolderName))
                {
                    folder.CoreId = ClientPropertiesExtensions.TryExtractCoreIdFromName(parentFolderName);
                }

                uniqueFolders[parentFolderId] = folder;

                // Add to cache
                _folderCache.TryAdd(parentFolderId, folder);
            }

            return uniqueFolders;
        }

        /// <summary>
        /// Gets the parent folder ID from document path (last element before document)
        /// </summary>
        private string? GetParentFolderIdFromPath(Entry entry)
        {
            if (entry.Path?.Elements == null || entry.Path.Elements.Count == 0)
                return null;

            // Last element in path is the immediate parent folder
            var parentElement = entry.Path.Elements.LastOrDefault();
            return parentElement?.Id;
        }

        /// <summary>
        /// Gets the parent folder name from document path
        /// </summary>
        private string? GetParentFolderNameFromPath(Entry entry)
        {
            if (entry.Path?.Elements == null || entry.Path.Elements.Count == 0)
                return null;

            var parentElement = entry.Path.Elements.LastOrDefault();
            return parentElement?.Name;
        }

        /// <summary>
        /// Determines client type from dossier type
        /// </summary>
        private string? DetermineClientTypeFromDossierType(string dossierType)
        {
            return dossierType?.ToUpperInvariant() switch
            {
                "PI" => "FL",  // Personal Individual → Fizičko Lice
                "LE" => "PL",  // Legal Entity → Pravno Lice
                "FL" => "FL",
                "PL" => "PL",
                "D" => null,   // Deposit - no specific client type
                _ => null
            };
        }

        /// <summary>
        /// Inserts folders into FolderStaging, ignoring duplicates
        /// </summary>
        private async Task<int> InsertFoldersAsync(List<FolderStaging> folders, CancellationToken ct)
        {
            if (folders.Count == 0)
                return 0;

            // Enrich folders with ClientAPI data if needed
            await EnrichFoldersAsync(folders, ct).ConfigureAwait(false);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                var inserted = await folderRepo.InsertManyIgnoreDuplicatesAsync(folders, ct).ConfigureAwait(false);
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                _fileLogger.LogInformation("Inserted {Inserted}/{Total} folders (duplicates ignored)",
                    inserted, folders.Count);

                return inserted;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("Failed to insert folders: {Error}", ex.Message);
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Enriches folders with ClientAPI data
        /// </summary>
        private async Task EnrichFoldersAsync(List<FolderStaging> folders, CancellationToken ct)
        {
            if (_clientApi == null)
            {
                _fileLogger.LogDebug("ClientAPI not configured, skipping folder enrichment");
                return;
            }

            foreach (var folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder.CoreId))
                    continue;

                try
                {
                    var clientData = await _clientApi.GetClientDataAsync(folder.CoreId, ct).ConfigureAwait(false);

                    if (clientData != null)
                    {
                        folder.ClientName = clientData.ClientName;
                        folder.MbrJmbg = clientData.MbrJmbg;
                        folder.Segment = clientData.Segment;
                        folder.Residency = clientData.Residency;
                        // Add other enrichments as needed
                    }
                }
                catch (Exception ex)
                {
                    _fileLogger.LogWarning("Failed to enrich folder {Name} with ClientAPI: {Error}",
                        folder.Name, ex.Message);
                }
            }
        }

        /// <summary>
        /// Inserts documents into DocStaging
        /// </summary>
        private async Task<int> InsertDocumentsAsync(List<DocStaging> documents, CancellationToken ct)
        {
            if (documents.Count == 0)
                return 0;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                var inserted = await docRepo.InsertManyAsync(documents, ct).ConfigureAwait(false);
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                _fileLogger.LogInformation("Inserted {Inserted}/{Total} documents", inserted, documents.Count);

                return inserted;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("Failed to insert documents: {Error}", ex.Message);
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Applies document mapping (same logic as DocumentDiscoveryService)
        /// </summary>
        private async Task ApplyDocumentMappingAsync(DocStaging doc, FolderStaging folder, Entry alfrescoEntry, CancellationToken ct)
        {
            try
            {
                // Extract properties from Alfresco
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

                if (alfrescoEntry.Properties != null)
                {
                    if (alfrescoEntry.Properties.TryGetValue("ecm:docDesc", out var docDescObj))
                        docDesc = docDescObj?.ToString();

                    if (alfrescoEntry.Properties.TryGetValue("ecm:docType", out var docTypeObj))
                        existingDocType = docTypeObj?.ToString();

                    if (alfrescoEntry.Properties.TryGetValue("ecm:docStatus", out var statusObj))
                        existingStatus = statusObj?.ToString();

                    if (alfrescoEntry.Properties.TryGetValue("ecm:coreId", out var coreIdObj))
                        coreIdFromDoc = coreIdObj?.ToString();

                    if (alfrescoEntry.Properties.TryGetValue("ecm:docDossierType", out var dossierTypeObj))
                        docDossierType = dossierTypeObj?.ToString();

                    if (alfrescoEntry.Properties.TryGetValue("ecm:docClientType", out var clientTypeObj))
                        docClientType = clientTypeObj?.ToString();

                    if (alfrescoEntry.Properties.TryGetValue("ecm:source", out var sourceObj))
                        sourceFromDoc = sourceObj?.ToString();

                    if (alfrescoEntry.Properties.TryGetValue("ecm:docCreationDate", out var creationDateObj))
                    {
                        if (creationDateObj is DateTime dt)
                            docCreationDate = dt;
                        else if (DateTime.TryParse(creationDateObj?.ToString(), out var parsedDate))
                            docCreationDate = parsedDate;
                    }

                    if (alfrescoEntry.Properties.TryGetValue("ecm:bnkNumberOfContract", out var contractObj))
                        contractNumber = contractObj?.ToString();

                    if (alfrescoEntry.Properties.TryGetValue("ecm:bnkTypeOfProduct", out var productObj))
                        productType = productObj?.ToString();

                    if (alfrescoEntry.Properties.TryGetValue("ecm:bnkAccountNumber", out var accountsObj))
                        accountNumbers = accountsObj?.ToString();
                }

                // Populate extracted properties
                doc.DocDescription = docDesc;
                doc.OriginalDocumentCode = existingDocType;
                doc.OldAlfrescoStatus = existingStatus;
                doc.ContractNumber = contractNumber;
                doc.ProductType = productType;
                doc.AccountNumbers = accountNumbers;
                doc.OriginalCreatedAt = docCreationDate ?? alfrescoEntry.CreatedAt.DateTime;
                doc.CoreId = coreIdFromDoc ?? folder.CoreId;
                doc.ClientSegment = docClientType ?? folder.ClientSegment ?? folder.Segment;

                // Map using OpisToTipMapper
                string? mappedDocType = null;
                string? mappedDocName = null;
                DocumentMapping? fullMapping = null;

                if (!string.IsNullOrWhiteSpace(docDesc))
                {
                    fullMapping = await _opisToTipMapper.GetFullMappingAsync(docDesc, existingDocType, ct).ConfigureAwait(false);

                    if (fullMapping != null)
                    {
                        mappedDocType = fullMapping.SifraDokumentaMigracija;
                        mappedDocName = fullMapping.NazivDokumentaMigracija;
                    }
                }

                doc.TipDosijea = fullMapping?.TipDosijea ?? docDossierType ?? folder.TipDosijea ?? "";
                doc.DocumentType = mappedDocType ?? existingDocType;
                doc.NewDocumentName = mappedDocName ?? "";
                doc.ProductType = fullMapping?.TipProizvoda ?? "";

                // Determine status
                var statusInfo = DocumentStatusDetectorV3.DetermineStatus(fullMapping, existingStatus);
                doc.IsActive = statusInfo.IsActive;
                doc.NewAlfrescoStatus = statusInfo.Status;
                doc.NewDocumentCode = statusInfo.MappingCode;
                if (string.IsNullOrWhiteSpace(doc.OriginalDocumentCode))
                    doc.OriginalDocumentCode = statusInfo.OriginalCode;

                // Determine destination
                var destinationType = DestinationRootFolderDeterminator.DetermineAndResolve(
                    doc.DocumentType,
                    doc.TipDosijea,
                    doc.ClientSegment);

                // Fallback from folder name
                if (destinationType == DossierType.Unknown && !string.IsNullOrWhiteSpace(folder.Name))
                {
                    var prefix = DossierIdFormatter.ExtractPrefix(folder.Name);
                    destinationType = prefix.ToUpperInvariant() switch
                    {
                        "PI" => DossierType.ClientFL,
                        "FL" => DossierType.ClientFL,
                        "LE" => DossierType.ClientPL,
                        "PL" => DossierType.ClientPL,
                        "ACC" => DossierType.AccountPackage,
                        "DE" => DossierType.Deposit,
                        "D" => DossierType.Deposit,
                        _ => DossierType.Unknown
                    };
                }

                doc.TargetDossierType = (int)destinationType;
                doc.Source = sourceFromDoc ?? SourceDetector.GetSource(destinationType);

                // Format destination dossier ID
                if (!string.IsNullOrWhiteSpace(folder.Name))
                {
                    doc.DossierDestFolderId = DossierIdFormatter.ConvertForTargetType(
                        folder.Name,
                        doc.TargetDossierType ?? (int)DossierType.Unknown,
                        folder.ContractNumber,
                        folder.ProductType,
                        folder.CoreId);
                }

                // Determine version
                doc.Version = 1.1m;
                doc.IsSigned = false;

                if (!string.IsNullOrWhiteSpace(alfrescoEntry.Name))
                {
                    var nameLower = alfrescoEntry.Name.ToLowerInvariant();
                    if (nameLower.Contains("signed") || nameLower.Contains("potpisano") || nameLower.Contains("potpisan"))
                    {
                        doc.Version = 1.2m;
                        doc.IsSigned = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex, "Error applying document mapping for document {Name}", alfrescoEntry.Name);

                // Set safe defaults
                doc.IsActive = false;
                doc.NewAlfrescoStatus = "poništen";
                doc.Source = "Heimdall";
                doc.TipDosijea = folder.TipDosijea;
                doc.TargetDossierType = (int)DossierType.Unknown;
                doc.ClientSegment = folder.ClientSegment ?? folder.Segment;
                doc.CoreId = folder.CoreId;
                doc.DossierDestFolderId = folder.Name?.Replace("-", "");
            }
        }

        /// <summary>
        /// Sanitizes input for AFTS queries
        /// </summary>
        private string SanitizeAFTS(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("+", "\\+")
                .Replace("-", "\\-")
                .Replace("&", "\\&")
                .Replace("|", "\\|")
                .Replace("!", "\\!")
                .Replace("(", "\\(")
                .Replace(")", "\\)")
                .Replace("{", "\\{")
                .Replace("}", "\\}")
                .Replace("[", "\\[")
                .Replace("]", "\\]")
                .Replace("^", "\\^")
                .Replace("~", "\\~")
                .Replace("*", "\\*")
                .Replace("?", "\\?")
                .Replace(":", "\\:");
        }

        #endregion
    }
}
