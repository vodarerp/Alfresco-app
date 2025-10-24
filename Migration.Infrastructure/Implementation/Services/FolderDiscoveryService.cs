using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Mapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces.Services;
using Migration.Abstraction.Models;
//using Oracle.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    public class FolderDiscoveryService : IFolderDiscoveryService
    {
        private readonly IFolderIngestor _ingestor;
        private readonly IFolderReader _reader;
        private readonly IDocumentResolver _resolver;
        private readonly IOptions<MigrationOptions> _options;
        private MultiFolderDiscoveryCursor? _multiFolderCursor = null;
        private readonly IServiceProvider _sp;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger _dbLogger;
        private readonly ILogger _fileLogger;
        private readonly ILogger _uiLogger;
        //private readonly ILogger<FolderDiscoveryService> _logger;

        private readonly object _cursorLock = new();

        // Metrics tracking
        private long _totalInserted = 0;

        // Cache for DOSSIER folder IDs: folderType -> destinationFolderId
        private readonly Dictionary<string, string> _dossierFolderCache = new();

        private const string ServiceName = "FolderDiscovery";

        public FolderDiscoveryService(IFolderIngestor ingestor, IFolderReader reader, IDocumentResolver resolver, IOptions<MigrationOptions> options, IServiceProvider sp, IUnitOfWork unitOfWork, ILoggerFactory logger)
        {
            _ingestor = ingestor;
            _reader = reader;
            _resolver = resolver;
            _options = options;
            _sp = sp;
            _unitOfWork = unitOfWork;
            //_logger = logger;
            _dbLogger = logger.CreateLogger("DbLogger");
            _fileLogger = logger.CreateLogger("FileLogger");
            _uiLogger = logger.CreateLogger("UiLogger");
        }

        public async Task<FolderBatchResult> RunBatchAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            using var batchScope = _fileLogger.BeginScope(new Dictionary<string, object>
            {
                ["Service"] = nameof(FolderDiscoveryService),
                ["Operation"] = "RunBatch"
            });
            var batch = _options.Value.FolderDiscovery.BatchSize ?? _options.Value.BatchSize;
            var nameFilter = _options.Value.FolderDiscovery.NameFilter ?? "-";
            var rootDiscoveryId = _options.Value.RootDiscoveryFolderId;
            var folderTypes = _options.Value.FolderDiscovery.FolderTypes;

            // Initialize multi-folder cursor if needed
            MultiFolderDiscoveryCursor? currentMultiCursor;
            lock (_cursorLock)
            {
                currentMultiCursor = _multiFolderCursor;
            }

            // If no cursor exists, discover DOSSIER subfolders
            if (currentMultiCursor == null || currentMultiCursor.SubfolderMap.Count == 0)
            {
                _fileLogger.LogInformation("Discovering DOSSIER subfolders...");
                var subfolders = await _reader.FindDossierSubfoldersAsync(rootDiscoveryId, folderTypes, ct).ConfigureAwait(false);

                if (subfolders.Count == 0)
                {
                    _fileLogger.LogWarning("No DOSSIER subfolders found matching criteria");
                    return new FolderBatchResult(0);
                }

                _fileLogger.LogInformation("Found {Count} DOSSIER subfolders: {Types}",
                    subfolders.Count, string.Join(", ", subfolders.Keys));

                // PRE-CREATE all DOSSIER destination folders SEQUENTIALLY to avoid race conditions
                await EnsureDossierFoldersExistAsync(subfolders.Keys, ct).ConfigureAwait(false);

                currentMultiCursor = new MultiFolderDiscoveryCursor
                {
                    SubfolderMap = subfolders,
                    CurrentFolderIndex = 0,
                    CurrentFolderType = subfolders.Keys.First(),
                    CurrentCursor = null
                };

                lock (_cursorLock)
                {
                    _multiFolderCursor = currentMultiCursor;
                }
            }

            // Get current subfolder to process
            var orderedTypes = currentMultiCursor.SubfolderMap.Keys.OrderBy(k => k).ToList();
            if (currentMultiCursor.CurrentFolderIndex >= orderedTypes.Count)
            {
                _fileLogger.LogInformation("All DOSSIER subfolders processed");
                return new FolderBatchResult(0);
            }

            var currentType = orderedTypes[currentMultiCursor.CurrentFolderIndex];
            var currentFolderId = currentMultiCursor.SubfolderMap[currentType];

            _fileLogger.LogDebug("Processing DOSSIER-{Type} folder", currentType);

            // Read batch from current subfolder
            var folderRequest = new FolderReaderRequest(
                RootId: currentFolderId,
                NameFilter: nameFilter,
                Skip: 0,
                Take: batch,
                Cursor: currentMultiCursor.CurrentCursor);

            var page = await _reader.ReadBatchAsync(folderRequest, ct).ConfigureAwait(false);

            // If no more items in current subfolder, move to next
            if (!page.HasMore || page.Items.Count == 0)
            {
                _fileLogger.LogInformation("Finished processing DOSSIER-{Type}, moving to next subfolder", currentType);

                lock (_cursorLock)
                {
                    _multiFolderCursor!.CurrentFolderIndex++;
                    if (_multiFolderCursor.CurrentFolderIndex < orderedTypes.Count)
                    {
                        _multiFolderCursor.CurrentFolderType = orderedTypes[_multiFolderCursor.CurrentFolderIndex];
                        _multiFolderCursor.CurrentCursor = null;
                    }
                }

                // Save checkpoint
                if (!ct.IsCancellationRequested)
                {
                    await SaveCheckpointAsync(ct).ConfigureAwait(false);
                }

                // Recursively call to start processing next folder
                return await RunBatchAsync(ct).ConfigureAwait(false);
            }

            _fileLogger.LogInformation("Read {Count} folders from DOSSIER-{Type}", page.Items.Count, currentType);

            var foldersToInsert = page.Items.ToList().ToFolderStagingListInsert();

            // Populate DossierDestFolderId from cache for all folders
            if (_dossierFolderCache.TryGetValue(currentType, out var dossierFolderId))
            {
                foreach (var folder in foldersToInsert)
                {
                    folder.DossierDestFolderId = dossierFolderId;
                }
                _fileLogger.LogDebug("Populated DossierDestFolderId={DossierFolderId} for {Count} folders",
                    dossierFolderId, foldersToInsert.Count);
            }
            else
            {
                _fileLogger.LogWarning("DOSSIER-{Type} not found in cache! This should not happen.", currentType);
            }

            var inserted = await InsertFoldersAsync(foldersToInsert, ct).ConfigureAwait(false);

            // Update cursor
            lock (_cursorLock)
            {
                _multiFolderCursor!.CurrentCursor = page.Next;
            }

            Interlocked.Add(ref _totalInserted, foldersToInsert.Count);

            // Save checkpoint after successful batch
            if (!ct.IsCancellationRequested)
            {
                await SaveCheckpointAsync(ct).ConfigureAwait(false);
            }

            sw.Stop();
            _fileLogger.LogInformation(
                "FolderDiscovery batch completed: {Count} folders from DOSSIER-{Type} inserted in {Elapsed}ms (Total: {Total} inserted)",
                inserted, currentType, sw.ElapsedMilliseconds, _totalInserted);
            _dbLogger.LogInformation(
                "FolderDiscovery batch completed: {Count} folders from DOSSIER-{Type} inserted in {Elapsed}ms (Total: {Total} inserted)",
                inserted, currentType, sw.ElapsedMilliseconds, _totalInserted);

            return new FolderBatchResult(inserted);
        }

        public async Task RunLoopAsync(CancellationToken ct)
        {
            var batchCounter = 1;
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;

            _fileLogger.LogInformation("FolderDiscovery worker started");

            // Note: FolderDiscovery doesn't have IN PROGRESS state issues
            // because it doesn't mark folders as IN PROGRESS during processing
            // It inserts directly to staging tables

            // Load checkpoint to resume from last position
            await LoadCheckpointAsync(ct).ConfigureAwait(false);

            // Reset metrics if starting fresh
            if (_multiFolderCursor == null)
            {
                _totalInserted = 0;
            }

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

                    if (result.InsertedCount == 0)
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
                            break;
                        }

                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        emptyResultCounter = 0; // Reset counter on success

                        var betweenDelay = _options.Value.FolderDiscovery.DelayBetweenBatchesInMs
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
                    _dbLogger.LogInformation("FolderDiscovery worker cancelled");
                    throw;
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
                "FolderDiscovery worker completed after {Count} batches. Total: {Total} folders inserted",
                batchCounter - 1, _totalInserted);
        }

        public async Task RunLoopAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback)
        {
            var batchCounter = 1;
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;
            var batchSize = _options.Value.FolderDiscovery.BatchSize ?? _options.Value.BatchSize;

            _fileLogger.LogInformation("FolderDiscovery worker started");

            // Note: FolderDiscovery doesn't have IN PROGRESS state issues
            // because it doesn't mark folders as IN PROGRESS during processing
            // It inserts directly to staging tables

            // Load checkpoint to resume from last position
            await LoadCheckpointAsync(ct).ConfigureAwait(false);

            // Reset metrics if starting fresh
            if (_multiFolderCursor == null)
            {
                _totalInserted = 0;
            }

            // Try to get total count
            long totalCount = 0;
            try
            {
                var rootDiscoveryId = _options.Value.RootDiscoveryFolderId;
                var nameFilter = _options.Value.FolderDiscovery.NameFilter ?? "-";

                _fileLogger.LogInformation("Attempting to count total folders...");
                totalCount = await _reader.CountTotalFoldersAsync(rootDiscoveryId, nameFilter, ct).ConfigureAwait(false);

                if (totalCount >= 0)
                {
                    _fileLogger.LogInformation("Total folders to discover: {TotalCount}", totalCount);
                }
                else
                {
                    _fileLogger.LogWarning("Count not supported by Alfresco, progress will show processed items only");
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
                ProcessedItems = _totalInserted,
                BatchSize = batchSize,
                CurrentBatch = 0,
                Message = totalCount > 0
                    ? $"Starting folder discovery... (Total: {totalCount})"
                    : "Starting folder discovery..."
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
                    progress.ProcessedItems = _totalInserted;
                    progress.CurrentBatch = batchCounter;
                    progress.CurrentBatchCount = result.InsertedCount;
                    progress.SuccessCount = result.InsertedCount;
                    progress.FailedCount = 0;
                    progress.Timestamp = DateTimeOffset.UtcNow;
                    progress.Message = result.InsertedCount > 0
                        ? $"Discovered {result.InsertedCount} folders in batch {batchCounter}"
                        : "No more folders to discover";

                    progressCallback?.Invoke(progress);

                    if (result.InsertedCount == 0)
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

                            progress.Message = $"Completed: {_totalInserted} folders discovered";
                            progressCallback?.Invoke(progress);
                            break;
                        }

                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        emptyResultCounter = 0; // Reset counter on success

                        var betweenDelay = _options.Value.FolderDiscovery.DelayBetweenBatchesInMs
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
                    _dbLogger.LogInformation("FolderDiscovery worker cancelled");
                    progress.Message = $"Cancelled after discovering {_totalInserted} folders";
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
                "FolderDiscovery worker completed after {Count} batches. Total: {Total} folders inserted",
                batchCounter - 1, _totalInserted);
        }

        #region private methods

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
                        _totalInserted = checkpoint.TotalProcessed;

                        if (!string.IsNullOrEmpty(checkpoint.CheckpointData))
                        {
                            var multiCursor = JsonSerializer.Deserialize<MultiFolderDiscoveryCursor>(checkpoint.CheckpointData);
                            lock (_cursorLock)
                            {
                                _multiFolderCursor = multiCursor;
                            }

                            _fileLogger.LogInformation(
                                "Checkpoint loaded: {TotalProcessed} processed, on folder {FolderType} (index {Index})",
                                _totalInserted, multiCursor?.CurrentFolderType, multiCursor?.CurrentFolderIndex);
                        }
                        else
                        {
                            _fileLogger.LogInformation("Checkpoint loaded: {TotalProcessed} processed (no cursor)", _totalInserted);
                        }
                    }
                    else
                    {
                        _fileLogger.LogInformation("No checkpoint found, starting fresh");
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
                _dbLogger.LogWarning(ex, "Failed to load checkpoint, starting fresh");
            }
        }

        private async Task SaveCheckpointAsync(CancellationToken ct)
        {
            try
            {
                MultiFolderDiscoveryCursor? currentMultiCursor;
                lock (_cursorLock)
                {
                    currentMultiCursor = _multiFolderCursor;
                }

                await using var scope = _sp.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var checkpointRepo = scope.ServiceProvider.GetRequiredService<IMigrationCheckpointRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var checkpoint = new MigrationCheckpoint
                    {
                        ServiceName = ServiceName,
                        CheckpointData = currentMultiCursor != null ? JsonSerializer.Serialize(currentMultiCursor) : null,
                        LastProcessedId = currentMultiCursor?.CurrentCursor?.LastObjectId,
                        LastProcessedAt = currentMultiCursor?.CurrentCursor?.LastObjectCreated.UtcDateTime,
                        TotalProcessed = _totalInserted,
                        TotalFailed = 0
                    };

                    await checkpointRepo.UpsertAsync(checkpoint, ct).ConfigureAwait(false);
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    _fileLogger.LogDebug("Checkpoint saved: {TotalProcessed} processed", _totalInserted);
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

        private async Task<int> InsertFoldersAsync(
            List<FolderStaging> folders,
            CancellationToken ct)
        {
            if (folders.Count == 0)
            {
                return 0;
            }

            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

            await uow.BeginAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
            try
            {
                var inserted = await folderRepo.InsertManyAsync(folders, ct).ConfigureAwait(false);
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                _fileLogger.LogDebug("Successfully inserted {Count} folders", inserted);
                return inserted;
            }
            catch (Exception ex)
            {
                //_dbLogger.LogError(ex, "Failed to insert {Count} folders", folders.Count);
                //_uiLogger.LogError(ex, "Failed to insert {Count} folders", folders.Count);

                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Pre-creates all DOSSIER destination folders SEQUENTIALLY to avoid race conditions
        /// in DocumentDiscoveryService parallel processing
        /// </summary>
        private async Task EnsureDossierFoldersExistAsync(IEnumerable<string> folderTypes, CancellationToken ct)
        {
            _fileLogger.LogInformation("Pre-creating DOSSIER destination folders to avoid race conditions...");

            foreach (var folderType in folderTypes)
            {
                // Check cache first
                if (_dossierFolderCache.ContainsKey(folderType))
                {
                    _fileLogger.LogDebug("DOSSIER-{Type} already exists in cache", folderType);
                    continue;
                }

                var dossierFolderName = $"DOSSIER-{folderType}";
                _fileLogger.LogInformation("Creating destination folder: {FolderName}", dossierFolderName);

                try
                {
                    // Create/resolve the DOSSIER folder under RootDestinationFolderId
                    var dossierFolderId = await _resolver.ResolveAsync(
                        _options.Value.RootDestinationFolderId,
                        dossierFolderName,
                        ct).ConfigureAwait(false);

                    // Cache the folder ID
                    _dossierFolderCache[folderType] = dossierFolderId;

                    _fileLogger.LogInformation(
                        "Successfully created/resolved DOSSIER-{Type} -> {FolderId}",
                        folderType, dossierFolderId);
                }
                catch (Exception ex)
                {
                    _dbLogger.LogError(ex,
                        "Failed to create DOSSIER-{Type} destination folder", folderType);
                    _uiLogger.LogError(ex,
                        "Failed to create DOSSIER-{Type} destination folder", folderType);
                    throw;
                }
            }

            _fileLogger.LogInformation(
                "Successfully pre-created {Count} DOSSIER destination folders",
                _dossierFolderCache.Count);
        }


        #endregion

        #region Older version - wroking (commented)
        //public async Task<FolderBatchResult> RunBatchAsync(CancellationToken ct)
        //{
        //    _logger.LogInformation("RunBatchAsync Started");

        //    var cnt = 0;
        //    var batch = _options.Value.DocumentDiscovery.BatchSize ?? _options.Value.BatchSize;
        //    var dop = _options.Value.DocumentDiscovery.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;


        //    var folderRequest = new FolderReaderRequest(
        //        _options.Value.RootDiscoveryFolderId, _options.Value.FolderDiscovery.NameFilter ?? "-", 0, batch, _cursor
        //        );

        //    _logger.LogInformation("_reader.ReadBatchAsync called");
        //    var page = await _reader.ReadBatchAsync(folderRequest, ct);

        //    if (!page.HasMore) return new FolderBatchResult(cnt);
        //    //using var scope = _sp.CreateScope();

        //    //var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        //    await _unitOfWork.BeginAsync(IsolationLevel.ReadCommitted, ct);
        //    try
        //    {
        //        var toInsert = page.Items.ToList().ToFolderStagingListInsert();

        //        if (toInsert.Count > 0)
        //        {
        //            _logger.LogInformation("_ingestor.InserManyAsync called");

        //            cnt = await _ingestor.InserManyAsync(toInsert, ct);
        //        }
        //        _cursor = page.Next;
        //        await _unitOfWork.CommitAsync();
        //        _logger.LogInformation("RunBatchAsync Commited");
        //        return new FolderBatchResult(cnt);
        //    }
        //    catch (Exception ex)
        //    {
        //        await _unitOfWork.RollbackAsync();
        //        _logger.LogInformation("RunBatchAsync Rollback");
        //        _logger.LogError("RunBatchAsync crashed!! {errMsg}!", ex.Message);
        //        return new FolderBatchResult(0);

        //    }




        //}

        //public async Task RunLoopAsync(CancellationToken ct)
        //{
        //    int BatchCounter = 1, couter = 0;
        //    var delay = _options.Value.IdleDelayInMs;
        //    _logger.LogInformation("Worker Started");
        //    while (!ct.IsCancellationRequested)
        //    {
        //        using (_logger.BeginScope(new Dictionary<string, object> { ["BatchCounter"] = BatchCounter }))
        //        {
        //            try
        //            {
        //                _logger.LogInformation($"Batch Started");

        //                var resRun = await RunBatchAsync(ct);

        //                if (resRun.InsertedCount == 0)
        //                {
        //                    _logger.LogInformation($"No more documents to process, exiting loop.");
        //                    couter++;
        //                    if (couter == _options.Value.BreakEmptyResults)
        //                    {
        //                        _logger.LogInformation($" Break after {couter} empty results");
        //                        break;
        //                    }
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
