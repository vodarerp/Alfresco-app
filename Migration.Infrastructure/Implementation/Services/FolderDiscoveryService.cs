using Alfresco.Abstraction.Models;
using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Extensions;
using Alfresco.Contracts.Mapper;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Mapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces.Services;
using Migration.Abstraction.Models;
using Migration.Infrastructure.Extensions;
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
        private readonly IFolderReader _reader;
        private readonly IOptions<MigrationOptions> _options;
        private MultiFolderDiscoveryCursor? _multiFolderCursor = null;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _dbLogger;
        private readonly ILogger _fileLogger;
        private readonly ILogger _uiLogger;
        private readonly IClientApi? _clientApi;

        private readonly object _cursorLock = new();

        // Metrics tracking
        private long _totalInserted = 0;

        private const string ServiceName = "FolderDiscovery";

        public FolderDiscoveryService(
            IFolderIngestor ingestor,
            IFolderReader reader,
            IOptions<MigrationOptions> options,
            IServiceScopeFactory scopeFactory,
            ILoggerFactory logger,
            IClientApi? clientApi = null)
        {
           
            _reader = reader;
            _options = options;
            _scopeFactory = scopeFactory;
           
            _clientApi = clientApi;
            
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
            var targetCoreIds = _options.Value.FolderDiscovery.TargetCoreIds;

            _fileLogger.LogInformation("FolderDiscovery batch started - BatchSize: {BatchSize}, NameFilter: {NameFilter}",
                batch, nameFilter);
            _dbLogger.LogInformation("FolderDiscovery batch started");

            // Initialize multi-folder cursor if needed
            MultiFolderDiscoveryCursor? currentMultiCursor;
            lock (_cursorLock)
            {
                currentMultiCursor = _multiFolderCursor;
            }

            // If no cursor exists, discover DOSSIER subfolders
            if (currentMultiCursor == null)
            {
                _fileLogger.LogInformation("Discovering DOSSIER subfolders in root {RootId}...", rootDiscoveryId);
                _dbLogger.LogInformation("Starting DOSSIER subfolder discovery");
                var subfolders = await _reader.FindDossierSubfoldersAsync(rootDiscoveryId, folderTypes, ct).ConfigureAwait(false);

                if (subfolders.Count == 0)
                {
                    _fileLogger.LogWarning("No DOSSIER subfolders found matching criteria");
                    _dbLogger.LogWarning("No DOSSIER subfolders found");
                    return new FolderBatchResult(0);
                }

                _fileLogger.LogInformation("Found {Count} DOSSIER subfolders: {Types}",
                    subfolders.Count, string.Join(", ", subfolders.Keys));
                _dbLogger.LogInformation("Found {Count} DOSSIER subfolders", subfolders.Count);

                currentMultiCursor = new MultiFolderDiscoveryCursor
                {
                    SubfolderMap = subfolders,
                    CurrentFolderIndex = 0,
                    CurrentFolderType = subfolders.Keys.OrderBy(k => k).First(),
                    CurrentCursor = null,
                    CurrentSkipCount = 0
                };

                lock (_cursorLock)
                {
                    _multiFolderCursor = currentMultiCursor;
                }
            }
            // If cursor was loaded from checkpoint but SubfolderMap is empty, re-discover folders
            else if (currentMultiCursor.SubfolderMap.Count == 0)
            {
                _fileLogger.LogWarning("Checkpoint loaded but SubfolderMap is empty, re-discovering DOSSIER subfolders");
                var subfolders = await _reader.FindDossierSubfoldersAsync(rootDiscoveryId, folderTypes, ct).ConfigureAwait(false);

                if (subfolders.Count == 0)
                {
                    _fileLogger.LogWarning("No DOSSIER subfolders found matching criteria");
                    return new FolderBatchResult(0);
                }

                // Preserve the checkpoint progress (CurrentFolderIndex, CurrentSkipCount) but update SubfolderMap
                lock (_cursorLock)
                {
                    _multiFolderCursor!.SubfolderMap = subfolders;

                    // Validate that CurrentFolderIndex still exists in the new SubfolderMap
                    var restoredOrderedTypes = subfolders.Keys.OrderBy(k => k).ToList();
                    if (_multiFolderCursor.CurrentFolderIndex >= restoredOrderedTypes.Count)
                    {
                        _fileLogger.LogWarning(
                            "CurrentFolderIndex {Index} is out of range for {Count} folder types. Resetting to 0.",
                            _multiFolderCursor.CurrentFolderIndex, restoredOrderedTypes.Count);
                        _multiFolderCursor.CurrentFolderIndex = 0;
                        _multiFolderCursor.CurrentSkipCount = 0;
                    }

                    _multiFolderCursor.CurrentFolderType = restoredOrderedTypes[_multiFolderCursor.CurrentFolderIndex];
                }

                currentMultiCursor = _multiFolderCursor;
                _fileLogger.LogInformation(
                    "Restored checkpoint: processing folder type {FolderType} at index {Index} with skip count {Skip}",
                    currentMultiCursor.CurrentFolderType, currentMultiCursor.CurrentFolderIndex, currentMultiCursor.CurrentSkipCount);
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

            if (targetCoreIds != null && targetCoreIds.Count > 0)
            {
                _fileLogger.LogInformation(
                    "Filtering by {Count} CoreIds: {CoreIds}",
                    targetCoreIds.Count,
                    string.Join(", ", targetCoreIds));
            }

            // Determine which reader method to use
            var useV2Reader = _options.Value.FolderDiscovery.UseV2Reader;
            var useDateFilter = _options.Value.FolderDiscovery.UseDateFilter;
            var dateFrom = _options.Value.FolderDiscovery.DateFrom;
            var dateTo = _options.Value.FolderDiscovery.DateTo;

            FolderReaderResult page;

            if (useV2Reader)
            {
                // Use v2 method with CMIS and skip/take pagination
                _fileLogger.LogDebug("Using ReadBatchAsync_v2 (CMIS) for folder discovery");

                var folderRequest = new FolderReaderRequest(
                    RootId: currentFolderId,
                    NameFilter: currentType,  // Pass the folder type (e.g., "PI", "PL") for CMIS LIKE query
                    Skip: currentMultiCursor.CurrentSkipCount,
                    Take: batch,
                    Cursor: null,  // v2 doesn't use cursor
                    TargetCoreIds: _options.Value.FolderDiscovery.TargetCoreIds);  // v2 doesn't support CoreId filtering (yet)

                page = await _reader.ReadBatchAsync_v2(
                    folderRequest,
                    dateFrom,
                    dateTo,
                    useDateFilter,
                    ct).ConfigureAwait(false);
            }
            else
            {
                // Use original method with AFTS and cursor-based pagination
                _fileLogger.LogDebug("Using ReadBatchAsync (AFTS) for folder discovery");

                var folderRequest = new FolderReaderRequest(
                    RootId: currentFolderId,
                    NameFilter: nameFilter,
                    Skip: 0,
                    Take: batch,
                    Cursor: currentMultiCursor.CurrentCursor,
                    TargetCoreIds: targetCoreIds);

                page = await _reader.ReadBatchAsync(folderRequest, ct).ConfigureAwait(false);
            }

            // If no items returned, move to next subfolder
            if (page.Items.Count == 0)
            {
                _fileLogger.LogInformation("No items returned for DOSSIER-{Type}, moving to next subfolder", currentType);

                lock (_cursorLock)
                {
                    _multiFolderCursor!.CurrentFolderIndex++;
                    if (_multiFolderCursor.CurrentFolderIndex < orderedTypes.Count)
                    {
                        _multiFolderCursor.CurrentFolderType = orderedTypes[_multiFolderCursor.CurrentFolderIndex];
                        _multiFolderCursor.CurrentCursor = null;
                        _multiFolderCursor.CurrentSkipCount = 0;  // Reset skip count for new folder
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

            // Enrich folders with ClientAPI data if properties are missing
            await EnrichFoldersWithClientDataAsync(page.Items, ct).ConfigureAwait(false);

            var foldersToInsert = page.Items.ToList().ToFolderStagingListInsert();

            var inserted = await InsertFoldersAsync(foldersToInsert, ct).ConfigureAwait(false);

            // Check if there are more items AFTER processing current batch
            if (!page.HasMore)
            {
                _fileLogger.LogInformation("Finished processing DOSSIER-{Type}, moving to next subfolder", currentType);

                lock (_cursorLock)
                {
                    _multiFolderCursor!.CurrentFolderIndex++;
                    if (_multiFolderCursor.CurrentFolderIndex < orderedTypes.Count)
                    {
                        _multiFolderCursor.CurrentFolderType = orderedTypes[_multiFolderCursor.CurrentFolderIndex];
                        _multiFolderCursor.CurrentCursor = null;
                        _multiFolderCursor.CurrentSkipCount = 0;  // Reset skip count for new folder
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

            // Update cursor/skip count based on which reader was used
            lock (_cursorLock)
            {
                if (useV2Reader)
                {
                    // For v2, increment skip count by the batch size
                    _multiFolderCursor!.CurrentSkipCount += batch;
                    _fileLogger.LogDebug("Updated skip count to {SkipCount} for next batch", _multiFolderCursor.CurrentSkipCount);
                }
                else
                {
                    // For v1, update the cursor
                    _multiFolderCursor!.CurrentCursor = page.Next;
                }
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

        public async Task<bool> RunLoopAsync(CancellationToken ct)
        {
            var batchCounter = 1;
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;
            var completedSuccessfully = false;

            _fileLogger.LogInformation("FolderDiscovery service started - IdleDelay: {IdleDelay}ms, MaxEmptyResults: {MaxEmptyResults}",
                delay, maxEmptyResults);
            _dbLogger.LogInformation("FolderDiscovery service started");
            _uiLogger.LogInformation("Folder Discovery started");

            // Note: FolderDiscovery doesn't have IN PROGRESS state issues
            // because it doesn't mark folders as IN PROGRESS during processing
            // It inserts directly to staging tables

            // Load checkpoint to resume from last position
            _fileLogger.LogInformation("Loading checkpoint...");
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
                            completedSuccessfully = true;
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
                    _fileLogger.LogInformation("FolderDiscovery service cancelled by user");
                    _dbLogger.LogInformation("FolderDiscovery service cancelled");
                    _uiLogger.LogInformation("Folder Discovery cancelled");
                    throw;
                }
                catch (AlfrescoTimeoutException timeoutEx)
                {
                    _fileLogger.LogError("FolderDiscovery service stopped - Alfresco Timeout: {Message}", timeoutEx.Message);
                    _dbLogger.LogError(timeoutEx, "FolderDiscovery service stopped - Timeout");
                    _uiLogger.LogError("Folder Discovery stopped - Timeout: {Operation}", timeoutEx.Operation);
                    throw; // Re-throw to stop migration
                }
                catch (AlfrescoRetryExhaustedException retryEx)
                {
                    _fileLogger.LogError("FolderDiscovery service stopped - Alfresco Retry Exhausted: {Message}", retryEx.Message);
                    _dbLogger.LogError(retryEx, "FolderDiscovery service stopped - Retry Exhausted");
                    _uiLogger.LogError("Folder Discovery stopped - Retry Exhausted: {Operation}", retryEx.Operation);
                    throw; // Re-throw to stop migration
                }
                catch (AlfrescoException alfrescoEx)
                {
                    _fileLogger.LogError("FolderDiscovery service stopped - Alfresco Error: {Message}", alfrescoEx.Message);
                    _dbLogger.LogError(alfrescoEx, "FolderDiscovery service stopped - Alfresco Error");
                    _uiLogger.LogError("Folder Discovery stopped - Alfresco Error (Status: {StatusCode})", alfrescoEx.StatusCode);
                    throw; // Re-throw to stop migration
                }
                catch (ClientApiTimeoutException clientTimeoutEx)
                {
                    _fileLogger.LogError("FolderDiscovery service stopped - Client API Timeout: {Message}", clientTimeoutEx.Message);
                    _dbLogger.LogError(clientTimeoutEx, "FolderDiscovery service stopped - Client API Timeout");
                    _uiLogger.LogError("Folder Discovery stopped - Client API Timeout: {Operation}", clientTimeoutEx.Operation);
                    throw; // Re-throw to stop migration
                }
                catch (ClientApiRetryExhaustedException clientRetryEx)
                {
                    _fileLogger.LogError("FolderDiscovery service stopped - Client API Retry Exhausted: {Message}", clientRetryEx.Message);
                    _dbLogger.LogError(clientRetryEx, "FolderDiscovery service stopped - Client API Retry Exhausted");
                    _uiLogger.LogError("Folder Discovery stopped - Client API Retry Exhausted: {Operation}", clientRetryEx.Operation);
                    throw; // Re-throw to stop migration
                }
                catch (ClientApiException clientEx)
                {
                    _fileLogger.LogError("FolderDiscovery service stopped - Client API Error: {Message}", clientEx.Message);
                    _dbLogger.LogError(clientEx, "FolderDiscovery service stopped - Client API Error");
                    _uiLogger.LogError("Folder Discovery stopped - Client API Error (Status: {StatusCode})", clientEx.StatusCode);
                    throw; // Re-throw to stop migration
                }
                catch (Exception ex)
                {
                    _fileLogger.LogError("Critical error in batch {BatchCounter}: {Error}", batchCounter, ex.Message);
                    _dbLogger.LogError(ex, "Error in batch {BatchCounter}", batchCounter);
                    _uiLogger.LogError("Error in batch {BatchCounter}", batchCounter);

                    // Exponential backoff on error
                    await Task.Delay(delay * 2, ct).ConfigureAwait(false);
                    batchCounter++;
                }
            }

            _fileLogger.LogInformation(
                "FolderDiscovery service completed after {Count} batches. Total: {Total} folders inserted",
                batchCounter - 1, _totalInserted);
            _dbLogger.LogInformation(
                "FolderDiscovery service completed - Total: {Total} folders inserted",
                _totalInserted);
            _uiLogger.LogInformation("Folder Discovery completed: {Total} folders inserted", _totalInserted);

            return completedSuccessfully;
        }

        public async Task<bool> RunLoopAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback)
        {
            var batchCounter = 1;
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;
            var batchSize = _options.Value.FolderDiscovery.BatchSize ?? _options.Value.BatchSize;
            var completedSuccessfully = false;

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
                            completedSuccessfully = true;
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
                catch (AlfrescoTimeoutException timeoutEx)
                {
                    _fileLogger.LogError("FolderDiscovery worker stopped - Alfresco Timeout: {Message}", timeoutEx.Message);
                    _dbLogger.LogError(timeoutEx, "FolderDiscovery worker stopped - Timeout");
                    _uiLogger.LogError("Folder Discovery worker stopped - Timeout: {Operation}", timeoutEx.Operation);
                    throw; // Re-throw to stop migration
                }
                catch (AlfrescoRetryExhaustedException retryEx)
                {
                    _fileLogger.LogError("FolderDiscovery worker stopped - Alfresco Retry Exhausted: {Message}", retryEx.Message);
                    _dbLogger.LogError(retryEx, "FolderDiscovery worker stopped - Retry Exhausted");
                    _uiLogger.LogError("Folder Discovery worker stopped - Retry Exhausted: {Operation}", retryEx.Operation);
                    throw; // Re-throw to stop migration
                }
                catch (AlfrescoException alfrescoEx)
                {
                    _fileLogger.LogError("FolderDiscovery worker stopped - Alfresco Error: {Message}", alfrescoEx.Message);
                    _dbLogger.LogError(alfrescoEx, "FolderDiscovery worker stopped - Alfresco Error");
                    _uiLogger.LogError("Folder Discovery worker stopped - Alfresco Error (Status: {StatusCode})", alfrescoEx.StatusCode);
                    throw; // Re-throw to stop migration
                }
                catch (ClientApiTimeoutException clientTimeoutEx)
                {
                    _fileLogger.LogError("FolderDiscovery worker stopped - Client API Timeout: {Message}", clientTimeoutEx.Message);
                    _dbLogger.LogError(clientTimeoutEx, "FolderDiscovery worker stopped - Client API Timeout");
                    _uiLogger.LogError("Folder Discovery worker stopped - Client API Timeout: {Operation}", clientTimeoutEx.Operation);
                    throw; // Re-throw to stop migration
                }
                catch (ClientApiRetryExhaustedException clientRetryEx)
                {
                    _fileLogger.LogError("FolderDiscovery worker stopped - Client API Retry Exhausted: {Message}", clientRetryEx.Message);
                    _dbLogger.LogError(clientRetryEx, "FolderDiscovery worker stopped - Client API Retry Exhausted");
                    _uiLogger.LogError("Folder Discovery worker stopped - Client API Retry Exhausted: {Operation}", clientRetryEx.Operation);
                    throw; // Re-throw to stop migration
                }
                catch (ClientApiException clientEx)
                {
                    _fileLogger.LogError("FolderDiscovery worker stopped - Client API Error: {Message}", clientEx.Message);
                    _dbLogger.LogError(clientEx, "FolderDiscovery worker stopped - Client API Error");
                    _uiLogger.LogError("Folder Discovery worker stopped - Client API Error (Status: {StatusCode})", clientEx.StatusCode);
                    throw; // Re-throw to stop migration
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

            return completedSuccessfully;
        }

        #region private methods

        private async Task LoadCheckpointAsync(CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
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
                            try
                            {
                                // NOTE: FolderSeekCursor model was updated to include LastObjectName (composite cursor)
                                // Old checkpoints (with 2-parameter cursor) will fail to deserialize
                                // In that case, we start fresh from the beginning
                                var multiCursor = JsonSerializer.Deserialize<MultiFolderDiscoveryCursor>(checkpoint.CheckpointData);
                                lock (_cursorLock)
                                {
                                    _multiFolderCursor = multiCursor;
                                }

                                _fileLogger.LogInformation(
                                    "Checkpoint loaded: {TotalProcessed} processed, on folder {FolderType} (index {Index})",
                                    _totalInserted, multiCursor?.CurrentFolderType, multiCursor?.CurrentFolderIndex);
                            }
                            catch (JsonException ex)
                            {
                                _fileLogger.LogWarning(ex,
                                    "Failed to deserialize checkpoint data (likely old format). Starting from beginning. " +
                                    "TotalProcessed count ({TotalProcessed}) is preserved.",
                                    _totalInserted);

                                // Start fresh, but keep TotalProcessed count
                                _multiFolderCursor = null;
                            }
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
                _uiLogger.LogInformation("Starting fresh folder discovery");
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

                await using var scope = _scopeFactory.CreateAsyncScope();
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
                _uiLogger.LogWarning("Could not save discovery checkpoint");
            }
        }

        private async Task<int> InsertFoldersAsync(
            List<FolderStaging> folders,
            CancellationToken ct)
        {
            if (folders.Count == 0)
            {
                _fileLogger.LogDebug("No folders to insert");
                return 0;
            }

            _fileLogger.LogDebug("Inserting {Count} folders into staging table", folders.Count);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

            await uow.BeginAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
            try
            {
                var inserted = await folderRepo.InsertManyAsync(folders, ct).ConfigureAwait(false);
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                _fileLogger.LogInformation("Successfully inserted {Count} folders into staging", inserted);
                return inserted;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("Failed to insert {Count} folders: {Error}", folders.Count, ex.Message);
                _dbLogger.LogError(ex, "Failed to insert {Count} folders", folders.Count);
                _uiLogger.LogError("Database error inserting folders");

                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Enriches folders with ClientAPI data if Alfresco properties are missing
        /// </summary>
        private async Task EnrichFoldersWithClientDataAsync(IReadOnlyList<ListEntry> folders, CancellationToken ct)
        {
            if (_clientApi == null)
            {
                _fileLogger.LogDebug("ClientAPI not configured, skipping client data enrichment");
                return;
            }

            var enrichmentCount = 0;
            var errorCount = 0;

            foreach (var listEntry in folders)
            {
                var entry = listEntry.Entry;
                // First, try to parse properties from Alfresco if they exist
                entry.PopulateClientProperties();

                // Check if we need to enrich from ClientAPI
                if (entry.HasClientProperties())
                {
                    _fileLogger.LogDebug("Folder {Name} already has client properties from Alfresco", entry.Name);
                    continue;
                }

                string coreId = entry?.ClientProperties?.CoreId ?? "";

               // var xx = entry.TryExtractCoreIdFromName_v2();
                // Try to extract CoreId from folder name
                //if (string.IsNullOrWhiteSpace(coreId)) coreId = entry.TryExtractCoreIdFromName_v2();
                if (string.IsNullOrWhiteSpace(coreId)) coreId = ClientPropertiesExtensions.TryExtractCoreIdFromName(entry.Name);

                if (string.IsNullOrWhiteSpace(coreId))
                {
                    _fileLogger.LogWarning("Could not extract CoreId from folder name: {Name}", entry.Name);
                    continue;
                }

                try
                {
                    _fileLogger.LogDebug("Fetching client data from ClientAPI for CoreId: {CoreId}", coreId);

                    // Call ClientAPI to get client data
                    var clientData = await _clientApi.GetClientDataAsync(coreId, ct).ConfigureAwait(false);

                    // Enrich entry with client data
                    entry.EnrichWithClientData(clientData);

                    enrichmentCount++;

                    _fileLogger.LogDebug(
                        "Successfully enriched folder {Name} with ClientAPI data for CoreId: {CoreId}",
                        entry.Name, coreId);
                }
                catch (ClientApiTimeoutException timeoutEx)
                {
                    _fileLogger.LogError("FolderDiscovery stopped - Client API Timeout: {Message}", timeoutEx.Message);
                    _uiLogger.LogError("Folder Discovery stopped - Client API Timeout: {Operation}", timeoutEx.Operation);
                    throw; // Re-throw to stop migration
                }
                catch (ClientApiRetryExhaustedException retryEx)
                {
                    _fileLogger.LogError("FolderDiscovery stopped - Client API Retry Exhausted: {Message}", retryEx.Message);
                    _uiLogger.LogError("Folder Discovery stopped - Client API Retry Exhausted: {Operation}", retryEx.Operation);
                    throw; // Re-throw to stop migration
                }
                catch (ClientApiException clientEx)
                {
                    _fileLogger.LogError("FolderDiscovery stopped - Client API Error: {Message}", clientEx.Message);
                    _uiLogger.LogError("Folder Discovery stopped - Client API Error (Status: {StatusCode})", clientEx.StatusCode);
                    throw; // Re-throw to stop migration
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _fileLogger.LogWarning(ex,
                        "Failed to enrich folder {Name} with ClientAPI data for CoreId: {CoreId}",
                        entry.Name, coreId);
                    _uiLogger.LogInformation("Could not enrich folder {Name}", entry.Name);
                    // Continue processing other folders even if one fails
                }
            }

            if (enrichmentCount > 0)
            {
                _fileLogger.LogInformation(
                    "Enriched {EnrichedCount} folders with ClientAPI data (Errors: {ErrorCount})",
                    enrichmentCount, errorCount);
            }
        }

        #endregion

     
    }
}
