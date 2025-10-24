using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Mapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Abstraction.Models;
using Migration.Infrastructure.Implementation.Helpers;
//using Oracle.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
using System.Collections.Concurrent;
//using Migration.Extensions.Oracle;
using Migration.Extensions.SqlServer;
using System.Diagnostics;
using System.Text.Json;


namespace Migration.Infrastructure.Implementation.Services
{
    public class DocumentDiscoveryService : IDocumentDiscoveryService
    {
        private readonly IDocumentIngestor _ingestor;
        private readonly IDocStagingRepository _docRepo;
        private readonly IFolderStagingRepository _folderRepo;
        private readonly IDocumentReader _reader;
        private readonly IDocumentResolver _resolver;
        private readonly IAlfrescoReadApi _alfrescoReadApi;
        private readonly IOptions<MigrationOptions> _options;
        private readonly IServiceProvider _sp;
        //private readonly ILogger<DocumentDiscoveryService> _logger;
        //private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger _dbLogger;
        private readonly ILogger _fileLogger;
        private readonly ILogger _uiLogger;

        private readonly ConcurrentDictionary<string, string> _resolvedFoldersCache = new();
        private long _totalProcessed = 0;
        private long _totalFailed = 0;
        private int _batchCounter = 0;

        private const string ServiceName = "DocumentDiscovery";

        public DocumentDiscoveryService(IDocumentIngestor ingestor, IDocumentReader reader, IDocumentResolver resolver, IDocStagingRepository docRepo, IFolderStagingRepository folderRepo, IAlfrescoReadApi alfrescoReadApi, IOptions<MigrationOptions> options, IServiceProvider sp, IUnitOfWork unitOfWork,ILoggerFactory logger)
        {
            _ingestor = ingestor;
            _reader = reader;
            _resolver = resolver;
            _docRepo = docRepo;
            _folderRepo = folderRepo;
            _alfrescoReadApi = alfrescoReadApi;
            _options = options;
            _sp = sp;
            _dbLogger = logger.CreateLogger("DbLogger");
            _fileLogger = logger.CreateLogger("FileLogger");
            _uiLogger = logger.CreateLogger("UiLogger");
           // _logger = logger;
            // _unitOfWork = unitOfWork;
        }

        public async Task<DocumentBatchResult> RunBatchAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            using var batchScope = _fileLogger.BeginScope(new Dictionary<string, object>
            {
                ["Service"] = nameof(DocumentDiscoveryService),
                ["Operation"] = "RunBatch"
            });

            _fileLogger.LogInformation("DocumentDiscovery batch started");

            var batch = _options.Value.DocumentDiscovery.BatchSize ?? _options.Value.BatchSize;
            var dop = _options.Value.DocumentDiscovery.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;

            var folders = await AcquireFoldersForProcessingAsync(batch, ct).ConfigureAwait(false);

            if (folders.Count == 0)
            {
                _fileLogger.LogInformation("No folders ready for processing");
                return new DocumentBatchResult(0);
            }

            var processedCount = 0;

            var errors = new ConcurrentBag<(long folderId, Exception error)>();


           // var x = await ResolveDestinationFolder(folders[0], ct).ConfigureAwait(false);

            await Parallel.ForEachAsync(folders, new ParallelOptions
            {
                MaxDegreeOfParallelism = dop,
                CancellationToken = ct
            },
            async (folder, token) =>
            {
                try
                {
                    await ProcessSingleFolderAsync(folder, ct).ConfigureAwait(false);
                    Interlocked.Increment(ref processedCount);
                    Interlocked.Increment(ref _totalProcessed);
                }
                catch (Exception ex)
                {
                    _dbLogger.LogError(ex, "Failed to process folder {FolderId} ({Name})",
                           folder.Id, folder.Name);
                    errors.Add((folder.Id, ex));
                }
            });

            if (!ct.IsCancellationRequested && !errors.IsEmpty)
            {
                await MarkFoldersAsFailedAsync(errors, ct).ConfigureAwait(false);
                Interlocked.Add(ref _totalFailed, errors.Count);
            }

            // Save checkpoint after successful batch
            if (!ct.IsCancellationRequested)
            {
                Interlocked.Increment(ref _batchCounter);
                await SaveCheckpointAsync(ct).ConfigureAwait(false);
            }

            sw.Stop();
            _fileLogger.LogInformation(
                "DocumentDiscovery batch completed: {Processed} processed, {Failed} failed in {Elapsed}ms " +
                "(Total: {TotalProcessed} processed, {TotalFailed} failed)",
                processedCount, errors.Count, sw.ElapsedMilliseconds, _totalProcessed, _totalFailed);

            return new DocumentBatchResult(processedCount);


        }
        public async Task RunLoopAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback)
        {
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;
            var batchSize = _options.Value.DocumentDiscovery.BatchSize ?? _options.Value.BatchSize;

            _fileLogger.LogInformation("DocumentDiscovery worker started");

            // Reset stuck folders from previous crashed run
            await ResetStuckItemsAsync(ct).ConfigureAwait(false);

            // Load checkpoint to resume from last position
            await LoadCheckpointAsync(ct).ConfigureAwait(false);

            // Start from next batch after checkpoint
            var batchCounter = _batchCounter + 1;

            // Try to get total count of folders to process
            long totalCount = 0;
            try
            {
                _fileLogger.LogInformation("Attempting to count total folders to process...");

                await using var scope = _sp.CreateAsyncScope();
                var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

                totalCount = await folderRepo.CountReadyForProcessingAsync(ct).ConfigureAwait(false);

                if (totalCount >= 0)
                {
                    _fileLogger.LogInformation("Total folders to process: {TotalCount}", totalCount);
                }
                else
                {
                    _fileLogger.LogWarning("Count not available, progress will show processed items only");
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning(ex, "Failed to count total folders, continuing without total count");
                totalCount = 0;
            }

            // Initial progress report
            var progress = new WorkerProgress
            {
                TotalItems = totalCount, // Will be 0 if count failed
                ProcessedItems = _totalProcessed,
                BatchSize = batchSize,
                CurrentBatch = 0,
                Message = totalCount > 0
                    ? $"Starting document discovery... (Total folders: {totalCount})"
                    : "Starting document discovery..."
            };
            progressCallback?.Invoke(progress);

            while (!ct.IsCancellationRequested)
            {
                using var batchScope = _fileLogger.BeginScope(new Dictionary<string, object>
                {
                    ["BatchCounter"] = batchCounter
                });

                try
                {
                    _fileLogger.LogDebug("Starting batch {BatchCounter}", batchCounter);

                    var result = await RunBatchAsync(ct).ConfigureAwait(false);

                    // Update progress after each batch
                    progress.ProcessedItems = _totalProcessed;
                    progress.CurrentBatch = batchCounter;
                    progress.CurrentBatchCount = result.PlannedCount;
                    progress.SuccessCount = result.PlannedCount;
                    progress.FailedCount = (int)_totalFailed;
                    progress.Timestamp = DateTimeOffset.UtcNow;
                    progress.Message = result.PlannedCount > 0
                        ? $"Processed {result.PlannedCount} folders in batch {batchCounter}"
                        : "No more folders to process";

                    progressCallback?.Invoke(progress);

                    if (result.PlannedCount == 0)
                    {
                        emptyResultCounter++;
                        _fileLogger.LogDebug(
                                "Empty result ({Counter}/{Max})",
                                emptyResultCounter, maxEmptyResults);

                        if (emptyResultCounter >= maxEmptyResults)
                        {
                            _fileLogger.LogInformation(
                                "Breaking after {Count} consecutive empty results",
                                emptyResultCounter);
                            _dbLogger.LogInformation(
                                "Breaking after {Count} consecutive empty results",
                                emptyResultCounter);

                            progress.Message = $"Completed: {_totalProcessed} folders processed, {_totalFailed} failed";
                            progressCallback?.Invoke(progress);
                            break;
                        }

                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        emptyResultCounter = 0;
                        var betweenDelay = _options.Value.DocumentDiscovery.DelayBetweenBatchesInMs
                            ?? _options.Value.DelayBetweenBatchesInMs;

                        if (betweenDelay > 0)
                        {
                            await Task.Delay(betweenDelay, ct).ConfigureAwait(false);
                        }
                    }

                    batchCounter++;
                }
                catch (OperationCanceledException)
                {
                    _dbLogger.LogInformation("DocumentDiscovery worker cancelled");
                    progress.Message = $"Cancelled after processing {_totalProcessed} folders ({_totalFailed} failed)";
                    progressCallback?.Invoke(progress);
                    throw;
                }
                catch (Exception ex)
                {
                    _dbLogger.LogError(ex, "Error in batch {BatchCounter}", batchCounter);
                    _uiLogger.LogError(ex, "Error in batch {BatchCounter}", batchCounter);

                    progress.Message = $"Error in batch {batchCounter}: {ex.Message}";
                    progressCallback?.Invoke(progress);

                    // Exponential backoff on error
                    await Task.Delay(delay * 2, ct).ConfigureAwait(false);
                    batchCounter++;
                }
            }

            _fileLogger.LogInformation(
                "DocumentDiscovery worker completed after {Count} batches. " +
                "Total: {Processed} processed, {Failed} failed",
                batchCounter - 1, _totalProcessed, _totalFailed);
            _dbLogger.LogInformation(
                "DocumentDiscovery worker completed after {Count} batches. " +
                "Total: {Processed} processed, {Failed} failed",
                batchCounter - 1, _totalProcessed, _totalFailed);
        }


        public async Task RunLoopAsync(CancellationToken ct)
        {
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;

            // Reset stuck folders from previous crashed run
            await ResetStuckItemsAsync(ct).ConfigureAwait(false);

            // Load checkpoint to resume from last position
            await LoadCheckpointAsync(ct).ConfigureAwait(false);

            // Start from next batch after checkpoint
            var batchCounter = _batchCounter + 1;

            while (!ct.IsCancellationRequested)
            {
                using var batchScope = _fileLogger.BeginScope(new Dictionary<string, object>
                {
                    ["BatchCounter"] = batchCounter
                });

                try
                {

                    _fileLogger.LogDebug("Starting batch {BatchCounter}", batchCounter);

                    var result = await RunBatchAsync(ct).ConfigureAwait(false);

                    if (result.PlannedCount == 0)
                    {
                        emptyResultCounter++;
                        _fileLogger.LogDebug(
                                "Empty result ({Counter}/{Max})",
                                emptyResultCounter, maxEmptyResults);
                        if (emptyResultCounter >= maxEmptyResults)
                        {
                            _fileLogger.LogInformation(
                                "Breaking after {Count} consecutive empty results",
                                emptyResultCounter);
                            break;
                        }
                        await Task.Delay(delay,ct).ConfigureAwait(false);
                    }
                    else
                    {
                        emptyResultCounter = 0;
                        var betweenDelay = _options.Value.DocumentDiscovery.DelayBetweenBatchesInMs
                            ?? _options.Value.DelayBetweenBatchesInMs;

                        if (betweenDelay > 0)
                        {
                            await Task.Delay(betweenDelay, ct).ConfigureAwait(false);
                        }
                    }
                    batchCounter++;
                }
                catch (Exception ex)
                {

                    _dbLogger.LogError(ex, "Error in batch {BatchCounter}", batchCounter);

                    // Exponential backoff on error
                    await Task.Delay(delay * 2, ct).ConfigureAwait(false);
                    batchCounter++;
                } 
            }
            _fileLogger.LogInformation(
                "DocumentDiscovery worker completed after {Count} batches. " +
                "Total: {Processed} processed, {Failed} failed",
                batchCounter - 1, _totalProcessed, _totalFailed);
            _dbLogger.LogInformation(
                "DocumentDiscovery worker completed after {Count} batches. " +
                "Total: {Processed} processed, {Failed} failed",
                batchCounter - 1, _totalProcessed, _totalFailed);
            
        }

        #region Private metods

        private async Task ResetStuckItemsAsync(CancellationToken ct)
        {
            try
            {
                var timeout = TimeSpan.FromMinutes(_options.Value.StuckItemsTimeoutMinutes);

                await using var scope = _sp.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var resetCount = await folderRepo.ResetStuckFolderAsync(
                        uow.Connection,
                        uow.Transaction,
                        timeout,
                        ct).ConfigureAwait(false);

                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    if (resetCount > 0)
                    {
                        _fileLogger.LogWarning(
                            "Reset {Count} stuck folders that were IN PROGRESS for more than {Minutes} minutes",
                            resetCount, _options.Value.StuckItemsTimeoutMinutes);
                    }
                    else
                    {
                        _fileLogger.LogInformation("No stuck folders found");
                    }
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _dbLogger.LogError(ex, "Failed to reset stuck folders");
            }
        }

        private async Task LoadCheckpointAsync(CancellationToken ct)
        {
            try
            {
                await using var scope = _sp.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var checkpointRepo = scope.ServiceProvider.GetRequiredService<IMigrationCheckpointRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var checkpoint = await checkpointRepo.GetByServiceNameAsync(ServiceName, ct).ConfigureAwait(false);
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    if (checkpoint != null)
                    {
                        _totalProcessed = checkpoint.TotalProcessed;
                        _totalFailed = checkpoint.TotalFailed;
                        _batchCounter = checkpoint.BatchCounter;

                        _fileLogger.LogInformation(
                            "Checkpoint loaded: {TotalProcessed} processed, {TotalFailed} failed, batch {BatchCounter}",
                            _totalProcessed, _totalFailed, _batchCounter);
                    }
                    else
                    {
                        _fileLogger.LogInformation("No checkpoint found, starting fresh");
                        _totalProcessed = 0;
                        _totalFailed = 0;
                        _batchCounter = 0;
                    }
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning(ex, "Failed to load checkpoint, starting fresh");
                _dbLogger.LogError(ex, "Failed to load checkpoint, starting fresh");
                _totalProcessed = 0;
                _totalFailed = 0;
                _batchCounter = 0;
            }
        }

        private async Task SaveCheckpointAsync(CancellationToken ct)
        {
            try
            {
                await using var scope = _sp.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var checkpointRepo = scope.ServiceProvider.GetRequiredService<IMigrationCheckpointRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var checkpoint = new MigrationCheckpoint
                    {
                        ServiceName = ServiceName,
                        TotalProcessed = _totalProcessed,
                        TotalFailed = _totalFailed,
                        BatchCounter = _batchCounter
                    };

                    await checkpointRepo.UpsertAsync(checkpoint, ct).ConfigureAwait(false);
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    _fileLogger.LogDebug("Checkpoint saved: {TotalProcessed} processed, {TotalFailed} failed, batch {BatchCounter}",
                        _totalProcessed, _totalFailed, _batchCounter);
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _dbLogger.LogWarning(ex, "Failed to save checkpoint");
            }
        }

        private async Task<IReadOnlyList<FolderStaging>> AcquireFoldersForProcessingAsync(int batch, CancellationToken ct)
        {
            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                var folders = await folderRepo.TakeReadyForProcessingAsync(batch, ct).ConfigureAwait(false);

                // Batch update instead of N individual updates
                var updates = folders.Select(f => (
                    f.Id,
                    MigrationStatus.InProgress.ToDbString(),
                    (string?)null
                ));

                await folderRepo.BatchSetFolderStatusAsync_v1(
                    uow.Connection,
                    uow.Transaction,
                    updates,
                    ct).ConfigureAwait(false);

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                return folders;

            }
            catch (Exception ex)
            {
                _dbLogger.LogWarning(ex, "Failed AcquireFoldersForProcessingAsync");

                await uow.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }

        }

        private async Task ProcessSingleFolderAsync(FolderStaging folder, CancellationToken ct)
        {
            using var logScope = _fileLogger.BeginScope(new Dictionary<string, object>
            {
                ["FolderId"] = folder.Id ,
                ["FolderName"] = folder.Name ?? "unknown",
                ["NodeId"] = folder.NodeId ?? "unknown"
            });


            _fileLogger.LogDebug("Processing folder {FolderId} ({Name}, NodeId: {NodeId})",
                folder.Id, folder.Name, folder.NodeId);

            var documents = await _reader.ReadBatchAsync(folder.NodeId!, ct).ConfigureAwait(false);

            if (documents == null || documents.Count == 0)
            {
                _fileLogger.LogInformation(
                    "No documents found in folder {FolderId} ({Name}, NodeId: {NodeId}) - marking as PROCESSED",
                    folder.Id, folder.Name, folder.NodeId);
                await MarkFolderAsProcessedAsync(folder.Id, ct).ConfigureAwait(false);
                return;
            }
            _fileLogger.LogInformation("Found {Count} documents in folder {FolderId} ({Name})",
                documents.Count, folder.Id, folder.Name);

            var desFolderId = await ResolveDestinationFolder(folder, ct).ConfigureAwait(false);
            _fileLogger.LogDebug("Resolved destination folder: {DestFolderId}", desFolderId);

            var docsToInsert = new List<DocStaging>(documents.Count);

            foreach (var d in documents)
            {
                var item = d.Entry.ToDocStagingInsert();
                item.ToPath = desFolderId;
                item.Status = MigrationStatus.Ready.ToDbString();
                docsToInsert.Add(item);
            }

            _fileLogger.LogInformation(
                "Prepared {Count} documents for insertion (folder {FolderId})",
                docsToInsert.Count, folder.Id);

            await InsertDocsAndMarkFolderAsync(docsToInsert, folder.Id, ct).ConfigureAwait(false);

            _fileLogger.LogInformation(
                "Successfully processed folder {FolderId} ({Name}): {Count} documents inserted",
                folder.Id, folder.Name, docsToInsert.Count);


        }

        private async Task InsertDocsAndMarkFolderAsync(List<DocStaging> docsToInsert, long folderId, CancellationToken ct)
        {

            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();
            var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

            await uow.BeginAsync().ConfigureAwait(false);

            try
            {
                int inserted = 0;
                if (docsToInsert.Count > 0)
                {
                    _fileLogger.LogDebug("Inserting {Count} documents for folder {FolderId}",
                        docsToInsert.Count, folderId);

                    inserted = await docRepo.InsertManyAsync(docsToInsert, ct).ConfigureAwait(false);

                    _fileLogger.LogInformation(
                        "Successfully inserted {Inserted}/{Total} documents for folder {FolderId}",
                        inserted, docsToInsert.Count, folderId);
                }
                else
                {
                    _fileLogger.LogWarning(
                        "docsToInsert is empty for folder {FolderId} - this should have been caught earlier!",
                        folderId);
                }

                await folderRepo.SetStatusAsync(folderId, MigrationStatus.Processed.ToDbString(), null, ct).ConfigureAwait(false);
                _fileLogger.LogDebug("Marked folder {FolderId} as PROCESSED", folderId);

                await uow.CommitAsync().ConfigureAwait(false);
                _fileLogger.LogDebug("Transaction committed for folder {FolderId}", folderId);
            }
            catch (Exception ex)
            {
                _dbLogger.LogError(ex,
                    "Failed to insert documents and mark folder {FolderId} as PROCESSED. " +
                    "Attempted to insert {Count} documents. Rolling back transaction.",
                    folderId, docsToInsert.Count);

                await uow.RollbackAsync().ConfigureAwait(false);
                throw;
            }

            //throw new NotImplementedException();
        }

        private async Task<string> ResolveDestinationFolder(FolderStaging folder, CancellationToken ct)
        {
            var normalizedName = folder.Name?.NormalizeName()
                    ?? throw new InvalidOperationException($"Folder {folder.Id} has null name");

            string parentPath = string.Empty;

            // FIRST: Try to use DossierDestFolderId populated by FolderDiscoveryService
            if (!string.IsNullOrEmpty(folder.DossierDestFolderId))
            {
                parentPath = folder.DossierDestFolderId;
                _fileLogger.LogDebug("Using pre-populated DossierDestFolderId={DossierDestFolderId} for folder {FolderId}",
                    parentPath, folder.Id);
            }
            else
            {
                // FALLBACK: Use cache or build parent path (for backwards compatibility)
                _fileLogger.LogWarning("DossierDestFolderId is null for folder {FolderId}, falling back to legacy path resolution", folder.Id);

                var cacheKey = $"node:{folder.ParentId}";

                if(_resolvedFoldersCache.TryGetValue(cacheKey, out var cachedId))
                {
                    _fileLogger.LogDebug("Using cached destination folder ID for ParentId {ParentId}", folder.ParentId);
                    parentPath = cachedId;
                }
                else
                {
                    // Build parent path (DOSSIER folder) and cache it BEFORE resolving child
                    parentPath = await BuildParentPathFromDossierAsync(folder, ct).ConfigureAwait(false);

                    // Cache the parent path immediately so subsequent folders with same parent use it
                    if (!string.IsNullOrEmpty(parentPath))
                    {
                        _resolvedFoldersCache.TryAdd(cacheKey, parentPath);
                        _fileLogger.LogDebug("Cached parent destination folder ID {ParentPath} for ParentId {ParentId}",
                            parentPath, folder.ParentId);
                    }
                }
            }

            if (string.IsNullOrEmpty(parentPath))
            {
                _fileLogger.LogWarning("Could not determine parent path for folder {FolderId}, using root destination", folder.Id);
                var fallbackId = await _resolver.ResolveAsync(_options.Value.RootDestinationFolderId, normalizedName, ct).ConfigureAwait(false);
                return fallbackId;
            }

            // Now resolve the target folder under the parent (DOSSIER folder)
            var newFolderId = await _resolver.ResolveAsync(parentPath, normalizedName, ct).ConfigureAwait(false);

            _fileLogger.LogDebug("Resolved destination folder '{Name}' -> {Id} under parent {ParentPath}",
                normalizedName, newFolderId, parentPath);

            if (_resolvedFoldersCache.Count > 10000)
            {
                _resolvedFoldersCache.Clear();
                _fileLogger.LogWarning("Cache cleared due to size limit (10000 entries)");
            }

            return newFolderId;
        }

        /// <summary>
        /// Builds the parent path from DOSSIER folder down to the folder's immediate parent
        /// Returns list ordered from DOSSIER folder to immediate parent
        /// </summary>
        
        private async Task<string> BuildParentPathFromDossierAsync(FolderStaging folder, CancellationToken ct)
        {
            string toRet = "";

            // Get the parent folder from Alfresco (should be DOSSIER-{folderType})
            _fileLogger.LogDebug("Getting parent folder info from Alfresco for ParentId {ParentId}", folder.ParentId);
            var nodeResponse = await _alfrescoReadApi.GetNodeByIdAsync(folder.ParentId!, ct).ConfigureAwait(false);

            if (nodeResponse?.Entry == null)
            {
                _fileLogger.LogWarning("Could not retrieve parent folder from Alfresco for ParentId {ParentId}, folder {FolderId}",
                    folder.ParentId, folder.Id);
                return toRet;
            }

            var parentFolderName = nodeResponse.Entry.Name;
            _fileLogger.LogDebug("Parent folder name from Alfresco: '{ParentName}' for folder {FolderId}",
                parentFolderName, folder.Id);

            // Resolve/create the DOSSIER folder under RootDestinationFolderId
            // This creates: RootDestinationFolderId/DOSSIER-{folderType}
            toRet = await _resolver.ResolveAsync(_options.Value.RootDestinationFolderId, parentFolderName!, ct).ConfigureAwait(false);

            _fileLogger.LogInformation(
                "Built parent path: DOSSIER folder '{ParentName}' resolved to {ParentFolderId} under root {RootId}",
                parentFolderName, toRet, _options.Value.RootDestinationFolderId);

            return toRet;
        }
        //private async Task<List<(string NormalizedName, string DestinationId)>?> BuildParentPathFromDossierAsync(FolderStaging folder, CancellationToken ct)
        //{
        //    if (string.IsNullOrEmpty(folder.ParentId))
        //    {
        //        return null;
        //    }

        //    try
        //    {
        //        var path = new List<(string Name, string ParentId)>();
        //        var currentNodeId = folder.ParentId;
        //        var maxDepth = 20;
        //        var depth = 0;

        //        // Walk up the parent chain until we hit DOSSIER folder
        //        while (depth < maxDepth)
        //        {
        //            var nodeResponse = await _alfrescoReadApi.GetNodeByIdAsync(currentNodeId, ct).ConfigureAwait(false);

        //            if (nodeResponse?.Entry == null)
        //            {
        //                _fileLogger.LogDebug("Reached end of parent chain at NodeId {NodeId}", currentNodeId);
        //                break;
        //            }

        //            var parentEntry = nodeResponse.Entry;
        //            path.Add((parentEntry.Name, parentEntry.ParentId));

        //            // Check if this is a DOSSIER folder
        //            if (!string.IsNullOrEmpty(parentEntry.Name) &&
        //                parentEntry.Name.StartsWith("DOSSIER-", StringComparison.OrdinalIgnoreCase))
        //            {
        //                // Found DOSSIER folder - this is the root of our path
        //                break;
        //            }

        //            if (string.IsNullOrEmpty(parentEntry.ParentId))
        //            {
        //                break;
        //            }

        //            currentNodeId = parentEntry.ParentId;
        //            depth++;
        //        }

        //        if (path.Count == 0)
        //        {
        //            return null;
        //        }

        //        // The last item in path should be DOSSIER folder
        //        var dossierFolder = path[^1];
        //        if (string.IsNullOrEmpty(dossierFolder.Name) ||
        //            !dossierFolder.Name.StartsWith("DOSSIER-", StringComparison.OrdinalIgnoreCase))
        //        {
        //            _fileLogger.LogWarning("Parent chain did not end at DOSSIER folder for {FolderId}", folder.Id);
        //            return null;
        //        }

        //        // Get or create DOSSIER folder in destination
        //        var folderType = dossierFolder.Name.Substring("DOSSIER-".Length);
        //        var dossierFolderName = $"DOSSIER-{folderType}";

        //        var dossierDestId = await _resolver.ResolveAsync(
        //            _options.Value.RootDestinationFolderId,
        //            dossierFolderName,
        //            ct).ConfigureAwait(false);

        //        // Build result list (reversed - from DOSSIER down to immediate parent)
        //        var result = new List<(string NormalizedName, string DestinationId)>();

        //        // First item is DOSSIER folder
        //        result.Add((dossierFolderName, dossierDestId));

        //        // Add remaining folders in reverse order (from DOSSIER down to immediate parent)
        //        for (int i = path.Count - 2; i >= 0; i--)
        //        {
        //            var f = path[i];
        //            var normalizedName = f.Name?.NormalizeName() ?? throw new InvalidOperationException($"Folder has null name");
        //            result.Add((normalizedName, string.Empty)); // DestinationId will be resolved during path creation
        //        }

        //        _fileLogger.LogDebug("Built parent path with {Count} levels for folder {FolderId}", result.Count, folder.Id);

        //        return result;
        //    }
        //    catch (Exception ex)
        //    {
        //        _fileLogger.LogError(ex, "Error building parent path for folder {FolderId}", folder.Id);
        //        return null;
        //    }
        //}

        private async Task MarkFolderAsProcessedAsync(long id, CancellationToken ct)
        {
            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                await folderRepo.SetStatusAsync(
                    id,
                    MigrationStatus.Processed.ToDbString(),
                    null,
                    ct).ConfigureAwait(false);

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);
            }
            catch
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        private async Task MarkFoldersAsFailedAsync(
           ConcurrentBag<(long FolderId, Exception Error)> errors,
           CancellationToken ct)
        {
            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                // Koristi batch extension method
                var updates = errors.Select(e => (
                    e.FolderId,
                    MigrationStatus.Error.ToDbString(),
                    e.Error.Message.Length > 4000
                        ? e.Error.Message[..4000]
                        : e.Error.Message
                ));

                await folderRepo.BatchSetFolderStatusAsync_v1(
                    uow.Connection,
                    uow.Transaction,
                    updates,
                    ct).ConfigureAwait(false);

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                _fileLogger.LogWarning("Marked {Count} folders as failed", errors.Count);
            }
            catch (Exception ex)
            {
                _dbLogger.LogError(ex, "Failed to mark folders as failed");
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
            }
        }

     

        #endregion

        #region Old Version - working (commented)
        //public async Task<DocumentBatchResult> RunBatchAsync(CancellationToken ct)
        //{
        //    _logger.LogInformation("RunBatchAsync started!");
        //    IReadOnlyList<FolderStaging> folders = null;
        //    int procesed = 0;
        //    var batch = _options.Value.DocumentDiscovery.BatchSize ?? _options.Value.BatchSize;
        //    var dop = _options.Value.DocumentDiscovery.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;
        //    List<DocStaging> docsToInser = new();

        //    await using (var scope = _sp.CreateAsyncScope())
        //    {
        //        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        //        var fr = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();
        //        await uow.BeginAsync(ct: ct);
        //        try
        //        {
        //            _logger.LogInformation("TakeReadyForProcessingAsync calling!");

        //            folders = await fr.TakeReadyForProcessingAsync(batch, ct);

        //            _logger.LogInformation($"TakeReadyForProcessingAsync returned {folders.Count}. Setitng up to status in prog!");

        //            foreach (var f in folders)
        //                await fr.SetStatusAsync(f.Id, MigrationStatus.InProgress.ToString(), null, ct);


        //            await uow.CommitAsync(ct: ct);
        //            _logger.LogInformation($"Statuses changed. Commit DOne!");

        //        }
        //        catch (Exception ex)
        //        {
        //            _logger.LogError($"Exception: {ex.Message}!");

        //            await uow.RollbackAsync(ct: ct);
        //            throw;
        //        }
        //    }

        //    if (folders != null && folders.Count > 0)
        //    {
        //        await Parallel.ForEachAsync(folders, new ParallelOptions
        //        {
        //            MaxDegreeOfParallelism = dop,
        //            CancellationToken = ct
        //        },
        //        async (folder, token) =>
        //        {
        //            using (_logger.BeginScope(new Dictionary<string, object> { ["FolderId"] = folder.Id }))
        //            {
        //                try
        //                {

        //                    _logger.LogInformation($"Geting docs for folder.");
        //                    var documents = await _reader.ReadBatchAsync(folder.NodeId, ct);


        //                    //if (documents == null || !documents.Any())
        //                    //{

        //                    //    await _unitOfWork.BeginAsync(ct: ct);
        //                    //    await _folderRepo.SetStatusAsync(folder.Id, "PROCESSED", null, ct);
        //                    //    await _unitOfWork.CommitAsync(ct: ct);
        //                    //    Interlocked.Increment(ref procesed); //thread safe folderProcesed++
        //                    //    return;
        //                    //}

        //                    var docBag = new ConcurrentBag<DocStaging>();

        //                    if (documents != null && documents.Count > 0)
        //                    {
        //                        var folderName = folder?.Name?.NormalizeName();
        //                        var newFolderPath = await _resolver.ResolveAsync(_options.Value.RootDestinationFolderId, folderName, ct);
        //                        _logger.LogInformation($"New folred path: {newFolderPath}");


        //                        docsToInser = new List<DocStaging>(documents.Count);

        //                        foreach (var d in documents)
        //                        {
        //                            var item = d.Entry.ToDocStagingInsert();
        //                            item.ToPath = newFolderPath;
        //                            docsToInser.Add(item);
        //                        }
        //                        //izbaciti Parallel ukoliko bude malo dokumenata po folder
        //                        //await Parallel.ForEachAsync(documents, new ParallelOptions
        //                        //{
        //                        //    MaxDegreeOfParallelism = dop,
        //                        //    CancellationToken = ct
        //                        //},
        //                        //async (document, token) =>
        //                        //{
        //                        //    var toInser = document.Entry.ToDocStagingInsert();
        //                        //    toInser.ToPath = newFolderPath;
        //                        //    docBag.Add(toInser);
        //                        //    await Task.CompletedTask;
        //                        //});                            
        //                    }

        //                    var listToInsert = docBag.ToList();

        //                    await using var scope = _sp.CreateAsyncScope();
        //                    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        //                    var fr = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();
        //                    var dr = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

        //                    await uow.BeginAsync(ct: ct);

        //                    try
        //                    {
        //                        if (docsToInser != null && docsToInser.Count > 0)
        //                        {
        //                            _ = await dr.InsertManyAsync(docsToInser, ct);
        //                            _logger.LogInformation($"Docs inserted int db");

        //                            //_ = await _ingestor.InserManyAsync(listToInsert, ct);
        //                        }

        //                        await fr.SetStatusAsync(folder.Id, MigrationStatus.Processed.ToDbString(), null, ct);

        //                        await uow.CommitAsync(ct: ct);
        //                        _logger.LogInformation($"Folder status to PROCESSED. DB Commited");

        //                    }
        //                    catch (Exception exTx)
        //                    {
        //                        _logger.LogError($"Exception: {exTx.Message}");

        //                        await uow.RollbackAsync(ct: ct);
        //                        await using var failScope = _sp.CreateAsyncScope();
        //                        var failUow = failScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        //                        var failFr = failScope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

        //                        await failUow.BeginAsync(ct: token);
        //                        await failFr.FailAsync(folder.Id, exTx.Message, token);
        //                        await failUow.CommitAsync(ct: token);
        //                        return;
        //                    }

        //                    Interlocked.Increment(ref procesed); //thread safe n++

        //                }
        //                catch (Exception ex)
        //                {
        //                    _logger.LogError($"Exception: {ex.Message}");

        //                    await using var failScope = _sp.CreateAsyncScope();
        //                    var failUow = failScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        //                    var failFr = failScope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

        //                    await failUow.BeginAsync(ct: token);
        //                    await failFr.FailAsync(folder.Id, ex.Message, token);
        //                    await failUow.CommitAsync(ct: token);
        //                    //await _folderRepo.FailAsync(folder.Id, ex.Message, ct);
        //                }
        //            }

        //        });
        //    }
        //    var delay = _options.Value.DocumentDiscovery.DelayBetweenBatchesInMs ?? _options.Value.DelayBetweenBatchesInMs;
        //    //_logger.LogInformation("No more documents to process, exiting loop."); TODO
        //    if (delay > 0)
        //        await Task.Delay(delay, ct);
        //    return new DocumentBatchResult(procesed);
        //}
        //public async Task RunLoopAsync(CancellationToken ct)
        //{
        //    int BatchCounter = 1, couter = 0;
        //    var delay = _options.Value.IdleDelayInMs;

        //    _logger.LogInformation("Worker Started");

        //    while (!ct.IsCancellationRequested)
        //    {
        //        using (_logger.BeginScope(new Dictionary<string, object> { ["BatchCnt"] = BatchCounter }))
        //        {
        //            try
        //            {
        //                _logger.LogInformation($"Batch Started");

        //                var resRun = await RunBatchAsync(ct);
        //                if (resRun.PlannedCount == 0)
        //                {
        //                    _logger.LogInformation($"No more documents to process, exiting loop.");
        //                    couter++;
        //                    if (couter == _options.Value.BreakEmptyResults)
        //                    {
        //                        _logger.LogInformation($" Break after {couter} empty results");
        //                        break;
        //                    }
        //                    //_logger.LogInformation("No more documents to process, exiting loop."); TODO
        //                    if (delay > 0)
        //                        await Task.Delay(delay, ct);
        //                }
        //                var between = _options.Value.DelayBetweenBatchesInMs;
        //                if (between > 0)
        //                    await Task.Delay(between, ct);
        //                _logger.LogInformation($"Batch Done");
        //            }
        //            catch (Exception ex)
        //            {
        //                _logger.LogError($"RunLoopAsync Exception: {ex.Message}.");
        //                if (delay > 0)
        //                    await Task.Delay(delay, ct);
        //            }
        //        }
        //        BatchCounter++;
        //        couter = 0;
        //    }
        //    _logger.LogInformation("RunLoopAsync END");

        //} 
        #endregion
    }
}
