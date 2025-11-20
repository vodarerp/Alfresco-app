using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Extensions;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Mapper;
using Mapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Abstraction.Models;
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
        private readonly IMigrationCheckpointRepository _checkpointRepo;
        private readonly IDocumentReader _reader;
        private readonly IOptions<MigrationOptions> _options;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IServiceProvider _sp;
        private readonly ILogger _dbLogger;
        private readonly ILogger _fileLogger;
        private readonly ILogger _uiLogger;
        private readonly IOpisToTipMapper _opisToTipMapper;

        private long _totalProcessed = 0;
        private long _totalFailed = 0;
        private int _batchCounter = 0;

        private const string ServiceName = "DocumentDiscovery";

        public DocumentDiscoveryService(
            IDocumentIngestor ingestor,
            IDocumentReader reader,
            IDocStagingRepository docRepo,
            IFolderStagingRepository folderRepo,
            IMigrationCheckpointRepository checkpointRepo,
            IOptions<MigrationOptions> options,
            IServiceProvider sp,
            IUnitOfWork unitOfWork,
            ILoggerFactory logger,
            IOpisToTipMapper opisToTipMapper)
        {
            _ingestor = ingestor;
            _reader = reader;
            _docRepo = docRepo;
            _folderRepo = folderRepo;
            _checkpointRepo = checkpointRepo ?? throw new ArgumentNullException(nameof(checkpointRepo));
            _options = options;
            _sp = sp;
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _dbLogger = logger.CreateLogger("DbLogger");
            _fileLogger = logger.CreateLogger("FileLogger");
            _uiLogger = logger.CreateLogger("UiLogger");
            _opisToTipMapper = opisToTipMapper ?? throw new ArgumentNullException(nameof(opisToTipMapper));
        }

        public async Task<DocumentBatchResult> RunBatchAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            using var batchScope = _fileLogger.BeginScope(new Dictionary<string, object>
            {
                ["Service"] = nameof(DocumentDiscoveryService),
                ["Operation"] = "RunBatch"
            });

            var batch = _options.Value.DocumentDiscovery.BatchSize ?? _options.Value.BatchSize;
            var dop = _options.Value.DocumentDiscovery.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;

            _fileLogger.LogInformation("DocumentDiscovery batch started - BatchSize: {BatchSize}, DOP: {DOP}", batch, dop);
            _dbLogger.LogInformation("DocumentDiscovery batch started");

            var folders = await AcquireFoldersForProcessingAsync(batch, ct).ConfigureAwait(false);

            if (folders.Count == 0)
            {
                _fileLogger.LogDebug("No folders ready for processing - batch is empty");
                return new DocumentBatchResult(0);
            }

            var processedCount = 0;
            var errors = new ConcurrentBag<(long folderId, Exception error)>();

            _fileLogger.LogInformation("Starting parallel processing of {Count} folders with DOP={DOP}", folders.Count, dop);

            await Parallel.ForEachAsync(folders, new ParallelOptions
            {
                MaxDegreeOfParallelism = dop,
                CancellationToken = ct
            },
            async (folder, token) =>
            {
                try
                {
                    _fileLogger.LogDebug("Processing folder {FolderId} ({Name})", folder.Id, folder.Name);
                    await ProcessSingleFolderAsync(folder, ct).ConfigureAwait(false);
                    Interlocked.Increment(ref processedCount);
                    Interlocked.Increment(ref _totalProcessed);
                    _fileLogger.LogDebug("Successfully processed folder {FolderId}", folder.Id);
                }
                catch (Exception ex)
                {
                    _fileLogger.LogError("Failed to process folder {FolderId} ({Name}): {Error}",
                        folder.Id, folder.Name, ex.Message);
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
        public async Task<bool> RunLoopAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback)
        {
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;
            var batchSize = _options.Value.DocumentDiscovery.BatchSize ?? _options.Value.BatchSize;
            var completedSuccessfully = false;

            _fileLogger.LogInformation("DocumentDiscovery service started - IdleDelay: {IdleDelay}ms, MaxEmptyResults: {MaxEmptyResults}",
                delay, maxEmptyResults);
            _dbLogger.LogInformation("DocumentDiscovery service started");
            _uiLogger.LogInformation("Document Discovery started");

            // Reset stuck folders from previous crashed run
            _fileLogger.LogInformation("Resetting stuck folders...");
            await ResetStuckItemsAsync(ct).ConfigureAwait(false);

            // Load checkpoint to resume from last position
            await LoadCheckpointAsync(ct).ConfigureAwait(false);

            // Start from next batch after checkpoint
            var batchCounter = _batchCounter + 1;

            // Try to get total count of folders to process
            long totalCount = 0;
            //try
            //{
            //    _fileLogger.LogInformation("Attempting to count total folders to process...");

            //    await using var scope = _sp.CreateAsyncScope();
            //    var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

            //    totalCount = await folderRepo.CountReadyForProcessingAsync(ct).ConfigureAwait(false);

            //    if (totalCount >= 0)
            //    {
            //        _fileLogger.LogInformation("Total folders to process: {TotalCount}", totalCount);
            //    }
            //    else
            //    {
            //        _fileLogger.LogWarning("Count not available, progress will show processed items only");
            //    }
            //}
            //catch (Exception ex)
            //{
            //    _fileLogger.LogWarning(ex, "Failed to count total folders, continuing without total count");
            //    totalCount = 0;
            //}

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
                            completedSuccessfully = true;
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
                    _fileLogger.LogInformation("DocumentDiscovery service cancelled by user");
                    _dbLogger.LogInformation("DocumentDiscovery service cancelled");
                    _uiLogger.LogInformation("Document Discovery cancelled");
                    progress.Message = $"Cancelled after processing {_totalProcessed} folders ({_totalFailed} failed)";
                    progressCallback?.Invoke(progress);
                    throw;
                }
                catch (Exception ex)
                {
                    _fileLogger.LogError("Critical error in batch {BatchCounter}: {Error}", batchCounter, ex.Message);
                    _dbLogger.LogError(ex, "Error in batch {BatchCounter}", batchCounter);
                    _uiLogger.LogError("Error in batch {BatchCounter}", batchCounter);

                    progress.Message = $"Error in batch {batchCounter}: {ex.Message}";
                    progressCallback?.Invoke(progress);

                    // Exponential backoff on error
                    await Task.Delay(delay * 2, ct).ConfigureAwait(false);
                    batchCounter++;
                }
            }

            _fileLogger.LogInformation(
                "DocumentDiscovery service completed after {Count} batches. " +
                "Total: {Processed} processed, {Failed} failed",
                batchCounter - 1, _totalProcessed, _totalFailed);
            _dbLogger.LogInformation(
                "DocumentDiscovery service completed - Total: {Processed} processed, {Failed} failed",
                _totalProcessed, _totalFailed);
            _uiLogger.LogInformation("Document Discovery completed: {Processed} folders processed", _totalProcessed);

            return completedSuccessfully;
        }


        public async Task<bool> RunLoopAsync(CancellationToken ct)
        {
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;
            var completedSuccessfully = false;

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
                            completedSuccessfully = true;
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

            return completedSuccessfully;
        }

        #region Private metods

        private async Task ResetStuckItemsAsync(CancellationToken ct)
        {
            try
            {
                var timeout = TimeSpan.FromMinutes(_options.Value.StuckItemsTimeoutMinutes);
                _fileLogger.LogDebug("Checking for stuck folders with timeout: {Minutes} minutes", _options.Value.StuckItemsTimeoutMinutes);

                await _unitOfWork.BeginAsync(ct: ct).ConfigureAwait(false);

                var resetCount = await _folderRepo.ResetStuckFolderAsync(
                    _unitOfWork.Connection,
                    _unitOfWork.Transaction,
                    timeout,
                    ct).ConfigureAwait(false);

                await _unitOfWork.CommitAsync(ct: ct).ConfigureAwait(false);

                if (resetCount > 0)
                {
                    _fileLogger.LogWarning(
                        "Reset {Count} stuck folders that were IN PROGRESS for more than {Minutes} minutes",
                        resetCount, _options.Value.StuckItemsTimeoutMinutes);
                    _dbLogger.LogWarning(
                        "Reset {Count} stuck folders (timeout: {Minutes} minutes)",
                        resetCount, _options.Value.StuckItemsTimeoutMinutes);
                    _uiLogger.LogWarning("Reset {Count} stuck folders", resetCount);
                }
                else
                {
                    _fileLogger.LogInformation("No stuck folders found");
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning("Failed to reset stuck folders: {Error}", ex.Message);
                _dbLogger.LogError(ex, "Failed to reset stuck folders");
                await _unitOfWork.RollbackAsync(ct).ConfigureAwait(false);
            }
        }

        private async Task LoadCheckpointAsync(CancellationToken ct)
        {
            try
            {
                await _unitOfWork.BeginAsync(ct: ct).ConfigureAwait(false);

                var checkpoint = await _checkpointRepo.GetByServiceNameAsync(ServiceName, ct).ConfigureAwait(false);
                await _unitOfWork.CommitAsync(ct: ct).ConfigureAwait(false);

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
            catch (Exception ex)
            {
                _fileLogger.LogWarning(ex, "Failed to load checkpoint, starting fresh");
                _dbLogger.LogError(ex, "Failed to load checkpoint, starting fresh");
                await _unitOfWork.RollbackAsync(ct: ct).ConfigureAwait(false);
                _totalProcessed = 0;
                _totalFailed = 0;
                _batchCounter = 0;
            }
        }

        private async Task SaveCheckpointAsync(CancellationToken ct)
        {
            try
            {
                await _unitOfWork.BeginAsync(ct: ct).ConfigureAwait(false);

                var checkpoint = new MigrationCheckpoint
                {
                    ServiceName = ServiceName,
                    TotalProcessed = _totalProcessed,
                    TotalFailed = _totalFailed,
                    BatchCounter = _batchCounter
                };

                await _checkpointRepo.UpsertAsync(checkpoint, ct).ConfigureAwait(false);
                await _unitOfWork.CommitAsync(ct: ct).ConfigureAwait(false);

                _fileLogger.LogDebug("Checkpoint saved: {TotalProcessed} processed, {TotalFailed} failed, batch {BatchCounter}",
                    _totalProcessed, _totalFailed, _batchCounter);
            }
            catch (Exception ex)
            {
                _dbLogger.LogWarning(ex, "Failed to save checkpoint");
                await _unitOfWork.RollbackAsync(ct: ct).ConfigureAwait(false);
            }
        }

        private async Task<IReadOnlyList<FolderStaging>> AcquireFoldersForProcessingAsync(int batch, CancellationToken ct)
        {
            _fileLogger.LogDebug("Acquiring {BatchSize} folders for processing", batch);

            try
            {
                await _unitOfWork.BeginAsync(ct: ct).ConfigureAwait(false);

                var folders = await _folderRepo.TakeReadyForProcessingAsync(batch, ct).ConfigureAwait(false);
                _fileLogger.LogDebug("Retrieved {Count} folders from database", folders.Count);

                // Batch update instead of N individual updates
                var updates = folders.Select(f => (
                    f.Id,
                    MigrationStatus.InProgress.ToDbString(),
                    (string?)null
                ));

                await _folderRepo.BatchSetFolderStatusAsync_v1(
                    _unitOfWork.Connection,
                    _unitOfWork.Transaction,
                    updates,
                    ct).ConfigureAwait(false);

                await _unitOfWork.CommitAsync(ct: ct).ConfigureAwait(false);
                _fileLogger.LogDebug("Marked {Count} folders as IN PROGRESS", folders.Count);

                return folders;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("Failed to acquire folders: {Error}", ex.Message);
                _dbLogger.LogError(ex, "Failed to acquire folders for processing");

                await _unitOfWork.RollbackAsync(ct).ConfigureAwait(false);
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

            // Use pagination to prevent OutOfMemory for folders with many documents
            const int PAGE_SIZE = 100; // Process 100 documents at a time
            int skipCount = 0;
            int totalProcessed = 0;
            bool hasMore = true;

            _fileLogger.LogInformation("Reading documents from Alfresco folder {NodeId} with pagination (pageSize: {PageSize})",
                folder.NodeId, PAGE_SIZE);

            while (hasMore && !ct.IsCancellationRequested)
            {
                _fileLogger.LogDebug("Reading page: skipCount={SkipCount}, maxItems={MaxItems} for folder {FolderId}",
                    skipCount, PAGE_SIZE, folder.Id);

                var result = await _reader.ReadBatchWithPaginationAsync(folder.NodeId!, skipCount, PAGE_SIZE, ct).ConfigureAwait(false);

                if (result == null || result.Documents.Count == 0)
                {
                    _fileLogger.LogDebug("No more documents in current page for folder {FolderId}", folder.Id);
                    break;
                }

                _fileLogger.LogInformation("Found {Count} documents in page (skipCount={SkipCount}) for folder {FolderId}",
                    result.Documents.Count, skipCount, folder.Id);

                // Process documents from current page
                var docsToInsert = new List<DocStaging>(result.Documents.Count);

                foreach (var d in result.Documents)
                {
                    var item = d.Entry.ToDocStagingInsert();
                    // ToPath will be determined by MoveService based on document properties
                    item.ToPath = string.Empty; // Will be populated by MoveService
                    item.Status = MigrationStatus.Ready.ToDbString();

                    // Apply document mapping using mappers from Faza 1 (database-driven)
                    // Uses scoped UnitOfWork to avoid connection conflicts
                    await using (var scope = _sp.CreateAsyncScope())
                    {
                        var scopedUow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                        var scopedMapper = scope.ServiceProvider.GetRequiredService<IOpisToTipMapper>();

                        await scopedUow.BeginAsync(ct: ct).ConfigureAwait(false);
                        try
                        {
                            var mappedType = await scopedMapper.GetTipDokumentaAsync(d.Entry.Properties?.GetValueOrDefault("ecm:opisDokumenta")?.ToString() ?? "", ct).ConfigureAwait(false);
                            // Apply the mapping result to item
                            if (!string.IsNullOrWhiteSpace(mappedType))
                            {
                                item.DocumentType = mappedType;
                            }
                            await scopedUow.CommitAsync(ct: ct).ConfigureAwait(false);
                        }
                        catch
                        {
                            try { await scopedUow.RollbackAsync(ct: ct).ConfigureAwait(false); } catch { }
                            // Continue with defaults on mapping failure
                        }
                    }

                    docsToInsert.Add(item);
                }

                _fileLogger.LogInformation(
                    "Prepared {Count} documents for insertion from page (folder {FolderId})",
                    docsToInsert.Count, folder.Id);

                // Insert documents from current page
                await InsertDocsAsync(docsToInsert, folder.Id, ct).ConfigureAwait(false);

                totalProcessed += docsToInsert.Count;

                _fileLogger.LogInformation(
                    "Processed page: {Processed} documents from folder {FolderId}. Total so far: {Total}",
                    docsToInsert.Count, folder.Id, totalProcessed);

                // Check if there are more documents
                hasMore = result.Count > 0;
                skipCount += result.Count;
            }

            if (totalProcessed == 0)
            {
                _fileLogger.LogInformation(
                    "No documents found in folder {FolderId} ({Name}, NodeId: {NodeId}) - marking as PROCESSED",
                    folder.Id, folder.Name, folder.NodeId);
            }
            else
            {
                _fileLogger.LogInformation(
                    "Successfully processed all pages for folder {FolderId} ({Name}): {Total} documents total",
                    folder.Id, folder.Name, totalProcessed);
            }

            // Mark folder as processed after all pages are done
            await MarkFolderAsProcessedAsync(folder.Id, ct).ConfigureAwait(false);


        }

        private async Task InsertDocsAndMarkFolderAsync(List<DocStaging> docsToInsert, long folderId, CancellationToken ct)
        {
            try
            {
                await _unitOfWork.BeginAsync(ct: ct).ConfigureAwait(false);

                int inserted = 0;
                if (docsToInsert.Count > 0)
                {
                    _fileLogger.LogDebug("Inserting {Count} documents for folder {FolderId}",
                        docsToInsert.Count, folderId);

                    inserted = await _docRepo.InsertManyAsync(docsToInsert, ct).ConfigureAwait(false);

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

                await _folderRepo.SetStatusAsync(folderId, MigrationStatus.Processed.ToDbString(), null, ct).ConfigureAwait(false);
                _fileLogger.LogDebug("Marked folder {FolderId} as PROCESSED", folderId);

                await _unitOfWork.CommitAsync(ct).ConfigureAwait(false);
                _fileLogger.LogDebug("Transaction committed for folder {FolderId}", folderId);
            }
            catch (Exception ex)
            {
                _dbLogger.LogError(ex,
                    "Failed to insert documents and mark folder {FolderId} as PROCESSED. " +
                    "Attempted to insert {Count} documents. Rolling back transaction.",
                    folderId, docsToInsert.Count);

                await _unitOfWork.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Inserts documents without marking folder as processed.
        /// Used in pagination loop where folder is marked after all pages are processed.
        /// </summary>
        private async Task InsertDocsAsync(List<DocStaging> docsToInsert, long folderId, CancellationToken ct)
        {
            if (docsToInsert == null || docsToInsert.Count == 0)
            {
                _fileLogger.LogDebug("No documents to insert for folder {FolderId}", folderId);
                return;
            }

            await using var scope = _sp.CreateAsyncScope();
            var scopedUow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var scopedDocRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

            try
            {
                await scopedUow.BeginAsync(ct: ct).ConfigureAwait(false);

                _fileLogger.LogDebug("Inserting {Count} documents for folder {FolderId}",
                    docsToInsert.Count, folderId);

                int inserted = await scopedDocRepo.InsertManyAsync(docsToInsert, ct).ConfigureAwait(false);

                _fileLogger.LogInformation(
                    "Successfully inserted {Inserted}/{Total} documents for folder {FolderId}",
                    inserted, docsToInsert.Count, folderId);

                await scopedUow.CommitAsync(ct).ConfigureAwait(false);
                _fileLogger.LogDebug("Transaction committed for folder {FolderId}", folderId);
            }
            catch (Exception ex)
            {
                _dbLogger.LogError(ex,
                    "Failed to insert documents for folder {FolderId}. " +
                    "Attempted to insert {Count} documents. Rolling back transaction.",
                    folderId, docsToInsert.Count);

                try { await scopedUow.RollbackAsync(ct).ConfigureAwait(false); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Inserts documents without marking folder as processed.
        /// Used in pagination loop where folder is marked after all pages are processed.
        /// </summary>
        private async Task InsertDocsAsync(List<DocStaging> docsToInsert, long folderId, CancellationToken ct)
        {
            if (docsToInsert == null || docsToInsert.Count == 0)
            {
                _fileLogger.LogDebug("No documents to insert for folder {FolderId}", folderId);
                return;
            }

            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

            await uow.BeginAsync().ConfigureAwait(false);

            try
            {
                _fileLogger.LogDebug("Inserting {Count} documents for folder {FolderId}",
                    docsToInsert.Count, folderId);

                int inserted = await docRepo.InsertManyAsync(docsToInsert, ct).ConfigureAwait(false);

                _fileLogger.LogInformation(
                    "Successfully inserted {Inserted}/{Total} documents for folder {FolderId}",
                    inserted, docsToInsert.Count, folderId);

                await uow.CommitAsync().ConfigureAwait(false);
                _fileLogger.LogDebug("Transaction committed for folder {FolderId}", folderId);
            }
            catch (Exception ex)
            {
                _dbLogger.LogError(ex,
                    "Failed to insert documents for folder {FolderId}. " +
                    "Attempted to insert {Count} documents. Rolling back transaction.",
                    folderId, docsToInsert.Count);

                await uow.RollbackAsync().ConfigureAwait(false);
                throw;
            }
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
            try
            {
                await _unitOfWork.BeginAsync(ct: ct).ConfigureAwait(false);

                await _folderRepo.SetStatusAsync(
                    id,
                    MigrationStatus.Processed.ToDbString(),
                    null,
                    ct).ConfigureAwait(false);

                await _unitOfWork.CommitAsync(ct: ct).ConfigureAwait(false);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        private async Task MarkFoldersAsFailedAsync(
           ConcurrentBag<(long FolderId, Exception Error)> errors,
           CancellationToken ct)
        {
            try
            {
                await _unitOfWork.BeginAsync(ct: ct).ConfigureAwait(false);

                // Koristi batch extension method
                var updates = errors.Select(e => (
                    e.FolderId,
                    MigrationStatus.Error.ToDbString(),
                    e.Error.Message.Length > 4000
                        ? e.Error.Message[..4000]
                        : e.Error.Message
                ));

                await _folderRepo.BatchSetFolderStatusAsync_v1(
                    _unitOfWork.Connection,
                    _unitOfWork.Transaction,
                    updates,
                    ct).ConfigureAwait(false);

                await _unitOfWork.CommitAsync(ct: ct).ConfigureAwait(false);

                _fileLogger.LogWarning("Marked {Count} folders as failed", errors.Count);
            }
            catch (Exception ex)
            {
                _dbLogger.LogError(ex, "Failed to mark folders as failed");
                await _unitOfWork.RollbackAsync(ct: ct).ConfigureAwait(false);
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

        /// <summary>
        /// Applies document mapping using mappers from FAZA 1 (Per Analiza_migracije_v2.md)
        ///
        /// NEW APPROACH - COMPLETE METADATA PREPARATION:
        /// - Extracts ALL ecm:* properties from old Alfresco document
        /// - Uses ecm:opisDokumenta (NOT document name) for status detection
        /// - Uses OpisToTipMapperV2 to map ecm:opisDokumenta → ecm:tipDokumenta (database-driven)
        /// - Uses DestinationRootFolderDeterminator for per-document destination
        /// - Uses DocumentStatusDetector.ShouldBeActiveByOpis() for status
        /// - Prepares ALL Alfresco properties so MoveService ONLY copies documents
        ///
        /// Populates ALL DocStaging fields needed for migration
        /// </summary>
        private async Task ApplyDocumentMappingAsync(DocStaging doc, FolderStaging folder, Entry alfrescoEntry, CancellationToken ct)
        {
            try
            {
                // ========================================
                // Step 1: Extract ALL ecm:* properties from old Alfresco
                // Property names per application that creates documents:
                // - cm:title, cm:description
                // - ecm:docDesc, ecm:coreId, ecm:status, ecm:docType
                // - ecm:docDossierType, ecm:docClientType, ecm:source
                // - ecm:docCreationDate
                // ========================================
                string? docDesc = null;              // ecm:docDesc (Document description)
                string? existingDocType = null;      // ecm:docType (Document type code)
                string? existingStatus = null;       // ecm:status (Document status)
                string? coreIdFromDoc = null;        // ecm:coreId (Core ID)
                string? docDossierType = null;       // ecm:docDossierType (Tip dosijea)
                string? docClientType = null;        // ecm:docClientType (Client type: PI/LE)
                string? sourceFromDoc = null;        // ecm:source (Source system)
                DateTime? docCreationDate = null;    // ecm:docCreationDate (Original creation date)
                string? cmTitle = null;              // cm:title (Document title)
                string? cmDescription = null;        // cm:description (Document description)

                // Additional properties (may not be present in all documents)
                string? contractNumber = null;       // ecm:brojUgovora
                string? productType = null;          // ecm:tipProizvoda
                string? accountNumbers = null;       // ecm:docAccountNumbers

                if (alfrescoEntry.Properties != null)
                {
                    // ========================================
                    // Core document properties (ACTUAL property names from application)
                    // ========================================

                    // ecm:docDesc - Document description (KEY PROPERTY for mapping)
                    if (alfrescoEntry.Properties.TryGetValue("ecm:docDesc", out var docDescObj))
                        docDesc = docDescObj?.ToString();

                    // ecm:docType - Document type code (e.g., "00099", "00824")
                    if (alfrescoEntry.Properties.TryGetValue("ecm:docType", out var docTypeObj))
                        existingDocType = docTypeObj?.ToString();

                    // ecm:status - Document status ("validiran", "poništen")
                    if (alfrescoEntry.Properties.TryGetValue("ecm:status", out var statusObj))
                        existingStatus = statusObj?.ToString();

                    // ecm:coreId - Core ID
                    if (alfrescoEntry.Properties.TryGetValue("ecm:coreId", out var coreIdObj))
                        coreIdFromDoc = coreIdObj?.ToString();

                    // ecm:docDossierType - Tip dosijea ("Dosije klijenta FL", "Dosije klijenta PL")
                    if (alfrescoEntry.Properties.TryGetValue("ecm:docDossierType", out var dossierTypeObj))
                        docDossierType = dossierTypeObj?.ToString();

                    // ecm:docClientType - Client type ("PI", "LE")
                    if (alfrescoEntry.Properties.TryGetValue("ecm:docClientType", out var clientTypeObj))
                        docClientType = clientTypeObj?.ToString();

                    // ecm:source - Source system ("Heimdall", "DUT", etc.)
                    if (alfrescoEntry.Properties.TryGetValue("ecm:source", out var sourceObj))
                        sourceFromDoc = sourceObj?.ToString();

                    // ecm:docCreationDate - Original creation date
                    if (alfrescoEntry.Properties.TryGetValue("ecm:docCreationDate", out var creationDateObj))
                    {
                        if (creationDateObj is DateTime dt)
                            docCreationDate = dt;
                        else if (DateTime.TryParse(creationDateObj?.ToString(), out var parsedDate))
                            docCreationDate = parsedDate;
                    }

                    // cm:title - Document title
                    if (alfrescoEntry.Properties.TryGetValue("cm:title", out var titleObj))
                        cmTitle = titleObj?.ToString();

                    // cm:description - Document description
                    if (alfrescoEntry.Properties.TryGetValue("cm:description", out var descObj))
                        cmDescription = descObj?.ToString();

                    // ========================================
                    // Additional properties (may not be present)
                    // ========================================

                    // ecm:brojUgovora - Contract number
                    if (alfrescoEntry.Properties.TryGetValue("ecm:brojUgovora", out var contractObj))
                        contractNumber = contractObj?.ToString();

                    // ecm:tipProizvoda - Product type
                    if (alfrescoEntry.Properties.TryGetValue("ecm:tipProizvoda", out var productObj))
                        productType = productObj?.ToString();

                    // ecm:docAccountNumbers - Account numbers
                    if (alfrescoEntry.Properties.TryGetValue("ecm:docAccountNumbers", out var accountsObj))
                        accountNumbers = accountsObj?.ToString();
                }

                // ========================================
                // Populate extracted properties
                // ========================================
                doc.DocDescription = docDesc;                        // ecm:docDesc
                doc.OriginalDocumentCode = existingDocType;          // ecm:docType
                doc.OldAlfrescoStatus = existingStatus;              // ecm:status
                doc.ContractNumber = contractNumber;                 // ecm:brojUgovora
                doc.ProductType = productType;                       // ecm:tipProizvoda
                doc.AccountNumbers = accountNumbers;                 // ecm:docAccountNumbers
                doc.OriginalCreatedAt = docCreationDate ?? alfrescoEntry.CreatedAt.DateTime;

                // Category fields - Not populated (no category properties in old documents)
                doc.CategoryCode = null;
                doc.CategoryName = null;

                // Use folder's TipDosijea if document doesn't have it, otherwise use document's value
                doc.TipDosijea = docDossierType ?? folder.TipDosijea;

                // Use coreId from document if available, otherwise use folder's CoreId
                doc.CoreId = coreIdFromDoc ?? folder.CoreId;

                // Use clientSegment from document if available (ecm:docClientType)
                doc.ClientSegment = docClientType ?? folder.ClientSegment ?? folder.Segment;

                // ========================================
                // Step 2: Map ecm:docDesc → ecm:docType using OpisToTipMapperV2 (database-driven)
                // ========================================
                string? mappedDocType = null;
                if (!string.IsNullOrWhiteSpace(docDesc))
                {
                    mappedDocType = await _opisToTipMapper.GetTipDokumentaAsync(docDesc, ct).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(mappedDocType))
                    {
                        _fileLogger.LogTrace("Mapped ecm:docDesc '{Opis}' → ecm:docType '{Tip}'",
                            docDesc, mappedDocType);
                    }
                    else
                    {
                        _fileLogger.LogDebug("No mapping found for ecm:docDesc '{Opis}', using existing docType",
                            docDesc);
                    }
                }

                // Use mapped value if available, otherwise keep existing
                doc.DocumentType = mappedDocType ?? existingDocType;

                // ========================================
                // Step 3: Determine document status using NEW method (ecm:docDesc)
                // ========================================
                var statusInfo = DocumentStatusDetector.GetStatusInfoByOpis(docDesc, existingStatus);

                doc.IsActive = statusInfo.IsActive;
                doc.NewAlfrescoStatus = statusInfo.Status;

                _fileLogger.LogTrace(
                    "Status determination: ecm:docDesc '{Opis}', Old Status: '{OldStatus}' → " +
                    "IsActive: {IsActive}, New Status: '{NewStatus}', HasMigrationSuffix: {HasSuffix}",
                    docDesc, existingStatus, statusInfo.IsActive, statusInfo.Status,
                    statusInfo.HasMigrationSuffixInOpis);

                // ========================================
                // Step 5: PER-DOCUMENT destination determination
                // ========================================
                var destinationType = DestinationRootFolderDeterminator.DetermineAndResolve(
                    doc.DocumentType,      // ecm:tipDokumenta (mapped or existing)
                    doc.TipDosijea,        // ecm:tipDosijea (from folder)
                    doc.ClientSegment);    // ecm:clientSegment

                // FALLBACK: If destination type is Unknown, try to determine from folder name prefix
                if (destinationType == DossierType.Unknown && !string.IsNullOrWhiteSpace(folder.Name))
                {
                    var prefix = DossierIdFormatter.ExtractPrefix(folder.Name);

                    var fallbackType = prefix.ToUpperInvariant() switch
                    {
                        "PI" => DossierType.ClientFL,      // Personal Individual → FL
                        "FL" => DossierType.ClientFL,      // Fizičko Lice → FL
                        "LE" => DossierType.ClientPL,      // Legal Entity → PL
                        "PL" => DossierType.ClientPL,      // Pravno Lice → PL
                        "ACC" => DossierType.AccountPackage, // Account Package
                        "DE" => DossierType.Deposit,       // Deposit
                        "D" => DossierType.Deposit,        // Deposit (short)
                        _ => DossierType.Unknown           // Keep Unknown if prefix not recognized
                    };

                    if (fallbackType != DossierType.Unknown)
                    {
                        destinationType = fallbackType;
                        _fileLogger.LogInformation(
                            "FALLBACK: Determined TargetDossierType from folder name prefix: '{Prefix}' → {DestType}",
                            prefix, destinationType);
                    }
                }

                doc.TargetDossierType = (int)destinationType;

                _fileLogger.LogTrace(
                    "Destination determination: TipDokumenta: '{TipDok}', TipDosijea: '{TipDos}', " +
                    "ClientSegment: '{Segment}' → TargetDossierType: {DestType}",
                    doc.DocumentType, doc.TipDosijea, doc.ClientSegment, destinationType);

                // ========================================
                // Step 6: Determine Source
                // Use source from document if available (ecm:source), otherwise use SourceDetector
                // ========================================
                doc.Source = sourceFromDoc ?? SourceDetector.GetSource(destinationType);

                // ========================================
                // Step 7: Format destination dossier ID
                // IMPORTANT: For ACC dosijee, convert PI/LE → ACC prefix
                // Example: PI-102206 → ACC102206 (if targetType = AccountPackage)
                // ========================================
                if (!string.IsNullOrWhiteSpace(folder.Name))
                {
                    // Use new method that changes prefix based on target dossier type
                    doc.DossierDestFolderId = DossierIdFormatter.ConvertForTargetType(
                        folder.Name,
                        doc.TargetDossierType ?? (int)DossierType.Unknown);

                    _fileLogger.LogTrace(
                        "Converted dossier ID: '{OldId}' (Type: {OldType}) → '{NewId}' (TargetType: {TargetType})",
                        folder.Name, folder.TipDosijea, doc.DossierDestFolderId, destinationType);
                }

                // ========================================
                // Step 8: Determine document version
                // ========================================
                // Per Analiza_migracije_v2.md:
                // - Unsigned documents: version 1.1
                // - Signed documents: version 1.2
                // For now, default to 1.1 (unsigned)
                // TODO: Implement signed document detection logic
                doc.Version = 1.1m; // Default: unsigned
                doc.IsSigned = false;

                // Check if document name contains "signed" or "potpisano"
                if (!string.IsNullOrWhiteSpace(alfrescoEntry.Name))
                {
                    var nameLower = alfrescoEntry.Name.ToLowerInvariant();
                    if (nameLower.Contains("signed") ||
                        nameLower.Contains("potpisano") ||
                        nameLower.Contains("potpisan"))
                    {
                        doc.Version = 1.2m; // Signed
                        doc.IsSigned = true;
                    }
                }

                _fileLogger.LogTrace(
                    "Document mapping complete: Opis: '{Opis}', TipDokumenta: '{Tip}', " +
                    "IsActive: {IsActive}, Status: '{Status}', Source: '{Source}', " +
                    "DestType: {DestType}, DossierDestId: '{DossierId}', Version: {Version}",
                    doc.DocDescription, doc.DocumentType, doc.IsActive, doc.NewAlfrescoStatus,
                    doc.Source, destinationType, doc.DossierDestFolderId, doc.Version);
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex,
                    "Error applying document mapping for document {Name} in folder {FolderName}",
                    alfrescoEntry.Name, folder.Name);

                // Set safe defaults on error
                doc.DocDescription = null;
                doc.DocumentType = null;
                doc.IsActive = false; // Safe default - inactive
                doc.NewAlfrescoStatus = "poništen";
                doc.Source = "Heimdall";
                doc.TipDosijea = folder.TipDosijea;
                doc.TargetDossierType = (int)DossierType.Unknown;
                doc.ClientSegment = folder.ClientSegment ?? folder.Segment;
                doc.CoreId = folder.CoreId;
                doc.DossierDestFolderId = folder.Name?.Replace("-", "");
            }
        }

        #endregion
    }
}
