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
        private readonly IClientApi? _clientApi;
        //private readonly ILogger<FolderDiscoveryService> _logger;

        private readonly object _cursorLock = new();

        // Metrics tracking
        private long _totalInserted = 0;

        // Cache for DOSSIER folder IDs: folderType -> destinationFolderId
        private readonly Dictionary<string, string> _dossierFolderCache = new();

        private const string ServiceName = "FolderDiscovery";

        public FolderDiscoveryService(
            IFolderIngestor ingestor,
            IFolderReader reader,
            IDocumentResolver resolver,
            IOptions<MigrationOptions> options,
            IServiceProvider sp,
            IUnitOfWork unitOfWork,
            ILoggerFactory logger,
            IClientApi? clientApi = null)
        {
            _ingestor = ingestor;
            _reader = reader;
            _resolver = resolver;
            _options = options;
            _sp = sp;
            _unitOfWork = unitOfWork;
            _clientApi = clientApi;
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
            if (currentMultiCursor == null || currentMultiCursor.SubfolderMap.Count == 0)
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

            if (targetCoreIds != null && targetCoreIds.Count > 0)
            {
                _fileLogger.LogInformation(
                    "Filtering by {Count} CoreIds: {CoreIds}",
                    targetCoreIds.Count,
                    string.Join(", ", targetCoreIds));
            }

            // Read batch from current subfolder
            var folderRequest = new FolderReaderRequest(
                RootId: currentFolderId,
                NameFilter: nameFilter,
                Skip: 0,
                Take: batch,
                Cursor: currentMultiCursor.CurrentCursor,
                TargetCoreIds: targetCoreIds);

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

            // Enrich folders with ClientAPI data if properties are missing
            await EnrichFoldersWithClientDataAsync(page.Items, ct).ConfigureAwait(false);

            var foldersToInsert = page.Items.ToList().ToFolderStagingListInsert();

            // Apply folder type detection and source mapping (NEW - FAZA 3)
            ApplyFolderTypeDetectionAndMapping(foldersToInsert, currentType);

            // Populate DossierDestFolderId from cache using TargetDossierType
            var successCount = 0;
            var failCount = 0;

            foreach (var folder in foldersToInsert)
            {
                var dossierTypeCode = folder.TargetDossierType.ToString();

                if (_dossierFolderCache.TryGetValue(dossierTypeCode, out var parentFolderId))
                {
                    folder.DossierDestFolderId = parentFolderId;
                    successCount++;

                    _fileLogger.LogTrace("Folder {Name}: DossierDestFolderId={ParentId} (Type {Code})",
                        folder.Name, parentFolderId, dossierTypeCode);
                }
                else
                {
                    failCount++;
                    _fileLogger.LogWarning(
                        "Parent folder {Code} not found in cache for folder {Name}! Falling back to Unknown.",
                        dossierTypeCode, folder.Name);

                    // Fallback to Unknown (999)
                    if (_dossierFolderCache.TryGetValue("999", out var unknownFolderId))
                    {
                        folder.DossierDestFolderId = unknownFolderId;
                        folder.TargetDossierType = (int)DossierType.Unknown;
                    }
                    else
                    {
                        _fileLogger.LogError(
                            "Unknown folder (999) also not in cache! This should not happen. Folder {Name} will have null DossierDestFolderId.",
                            folder.Name);
                    }
                }
            }

            _fileLogger.LogDebug(
                "Populated DossierDestFolderId for {SuccessCount}/{TotalCount} folders ({FailCount} fallbacks to Unknown)",
                successCount, foldersToInsert.Count, failCount);

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
            //try
            //{
            //    var rootDiscoveryId = _options.Value.RootDiscoveryFolderId;
            //    var nameFilter = _options.Value.FolderDiscovery.NameFilter ?? "-";

            //    _fileLogger.LogInformation("Attempting to count total folders...");
            //    totalCount = await _reader.CountTotalFoldersAsync(rootDiscoveryId, nameFilter, ct).ConfigureAwait(false);

            //    if (totalCount >= 0)
            //    {
            //        _fileLogger.LogInformation("Total folders to discover: {TotalCount}", totalCount);
            //    }
            //    else
            //    {
            //        _fileLogger.LogWarning("Count not supported by Alfresco, progress will show processed items only");
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
                _fileLogger.LogDebug("No folders to insert");
                return 0;
            }

            _fileLogger.LogDebug("Inserting {Count} folders into staging table", folders.Count);

            await using var scope = _sp.CreateAsyncScope();
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

                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Pre-creates all parent destination folders (300/400/500/700/999) SEQUENTIALLY
        /// to avoid race conditions during DocumentDiscoveryService parallel processing
        /// </summary>
        private async Task EnsureDossierFoldersExistAsync(IEnumerable<string> folderTypes, CancellationToken ct)
        {
            _fileLogger.LogInformation("Pre-creating parent destination folders (300/400/500/700/999)...");
            _dbLogger.LogInformation("Starting parent folder pre-creation");

            // Determine which DossierTypes are needed based on discovered folderTypes
            var neededDossierTypes = new HashSet<DossierType>();

            foreach (var folderType in folderTypes)
            {
                // Get all possible DossierTypes for this folder type
                // For FL, this returns both ClientFL (500) and ClientPL (400)
                var possibleTypes = DossierTypeDetector.GetPossibleDossierTypes(folderType);
                foreach (var type in possibleTypes)
                {
                    neededDossierTypes.Add(type);
                }
            }

            // Always ensure Unknown folder exists as fallback
            neededDossierTypes.Add(DossierType.Unknown);

            _fileLogger.LogInformation("Will create {Count} parent folders: {Types}",
                neededDossierTypes.Count,
                string.Join(", ", neededDossierTypes.Select(t => $"{(int)t} ({t})")));

            // Create each parent folder sequentially
            foreach (var dossierType in neededDossierTypes.OrderBy(t => (int)t))
            {
                // Skip unresolved types (should not happen at this point)
                if (dossierType == DossierType.ClientFLorPL || dossierType == DossierType.Other)
                {
                    _fileLogger.LogWarning("Skipping unresolved DossierType: {Type}", dossierType);
                    continue;
                }

                var dossierTypeCode = ((int)dossierType).ToString();

                // Check cache first
                if (_dossierFolderCache.ContainsKey(dossierTypeCode))
                {
                    _fileLogger.LogDebug("Parent folder {Code} already exists in cache", dossierTypeCode);
                    continue;
                }

                var folderName = DossierTypeDetector.GetDestinationFolderName(dossierType);
                _fileLogger.LogInformation("Creating parent folder: {FolderName} (Code: {Code})",
                    folderName, dossierTypeCode);

                try
                {
                    // Create/resolve the parent folder under RootDestinationFolderId
                    var parentFolderId = await _resolver.ResolveAsync(
                        _options.Value.RootDestinationFolderId,
                        folderName,
                        ct).ConfigureAwait(false);

                    // Cache the folder ID by DossierType code (300/400/500/700/999)
                    _dossierFolderCache[dossierTypeCode] = parentFolderId;

                    _fileLogger.LogInformation(
                        "Successfully created/resolved parent folder {Code} ({Name}) -> {FolderId}",
                        dossierTypeCode, folderName, parentFolderId);
                    _dbLogger.LogInformation(
                        "Created parent folder {Code} ({Name})",
                        dossierTypeCode, folderName);
                }
                catch (Exception ex)
                {
                    _fileLogger.LogError("Failed to create parent folder {Code} ({Name}): {Error}",
                        dossierTypeCode, folderName, ex.Message);
                    _dbLogger.LogError(ex,
                        "Failed to create parent folder {Code} ({Name})",
                        dossierTypeCode, folderName);
                    _uiLogger.LogError("Failed to create parent folder {Code}", dossierTypeCode);
                    throw;
                }
            }

            _fileLogger.LogInformation(
                "Successfully pre-created {Count} parent destination folders: {Folders}",
                _dossierFolderCache.Count,
                string.Join(", ", _dossierFolderCache.Keys.OrderBy(k => k)));
            _dbLogger.LogInformation(
                "Pre-created {Count} parent folders",
                _dossierFolderCache.Count);
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

                // Try to extract CoreId from folder name
                var coreId = entry.TryExtractCoreIdFromName();
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
                catch (Exception ex)
                {
                    errorCount++;
                    _fileLogger.LogWarning(ex,
                        "Failed to enrich folder {Name} with ClientAPI data for CoreId: {CoreId}",
                        entry.Name, coreId);
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

        /// <summary>
        /// Applies folder type detection and source mapping to folders (FAZA 3 - NEW)
        /// Uses DossierTypeDetector and SourceDetector from Alfresco.Contracts.Mappers
        /// </summary>
        private void ApplyFolderTypeDetectionAndMapping(List<FolderStaging> folders, string currentFolderType)
        {
            _fileLogger.LogDebug("Applying folder type detection and mapping to {Count} folders from DOSSIER-{Type}",
                folders.Count, currentFolderType);

            var detectedCount = 0;
            var unknownCount = 0;
            var flOrPlResolvedCount = 0;

            foreach (var folder in folders)
            {
                try
                {
                    // Step 1: Extract TipDosijea from Alfresco properties or infer from folder structure
                    // For now, we'll use a simple heuristic based on currentFolderType
                    string? tipDosijea = InferTipDosijeaFromFolderType(currentFolderType);
                    folder.TipDosijea = tipDosijea;

                    // Step 2: Detect DossierType using DossierTypeDetector
                    var dossierType = DossierTypeDetector.DetectFromTipDosijea(tipDosijea);

                    // Step 3: If ClientFLorPL, resolve using ClientSegment
                    if (dossierType == DossierType.ClientFLorPL)
                    {
                        // Use Segment from ClientProperties if available
                        var clientSegment = folder.Segment ?? folder.ClientType;
                        folder.ClientSegment = clientSegment;

                        if (!string.IsNullOrWhiteSpace(clientSegment))
                        {
                            dossierType = DossierTypeDetector.ResolveFLorPL(clientSegment);
                            flOrPlResolvedCount++;

                            _fileLogger.LogDebug(
                                "Resolved FL/PL for folder {Name}: ClientSegment={Segment} -> DossierType={DossierType}",
                                folder.Name, clientSegment, dossierType);
                        }
                        else
                        {
                            _fileLogger.LogWarning(
                                "Folder {Name} has TipDosijea='Dosije klijenta FL/PL' but missing ClientSegment - setting to Unknown",
                                folder.Name);
                            dossierType = DossierType.Unknown;
                        }
                    }

                    // Step 4: Set TargetDossierType
                    folder.TargetDossierType = (int)dossierType;

                    // Step 5: Determine Source using SourceDetector
                    folder.Source = SourceDetector.GetSource(dossierType);

                    if (dossierType == DossierType.Unknown)
                    {
                        unknownCount++;
                        _fileLogger.LogWarning(
                            "Folder {Name} could not be classified - TipDosijea={TipDosijea}, marking as Unknown",
                            folder.Name, tipDosijea);
                    }
                    else
                    {
                        detectedCount++;
                    }

                    _fileLogger.LogTrace(
                        "Folder {Name}: TipDosijea={TipDosijea}, TargetDossierType={TargetType}, Source={Source}",
                        folder.Name, folder.TipDosijea, dossierType, folder.Source);
                }
                catch (Exception ex)
                {
                    unknownCount++;
                    folder.TargetDossierType = (int)DossierType.Unknown;
                    folder.Source = "Heimdall"; // Default fallback

                    _fileLogger.LogError(ex,
                        "Error detecting folder type for {Name}, marking as Unknown",
                        folder.Name);
                }
            }

            _fileLogger.LogInformation(
                "Folder type detection completed: {DetectedCount} detected, {UnknownCount} unknown, {ResolvedCount} FL/PL resolved",
                detectedCount, unknownCount, flOrPlResolvedCount);
        }

        /// <summary>
        /// Infers TipDosijea from DOSSIER folder type
        /// This is a heuristic mapping based on folder structure
        /// </summary>
        private string? InferTipDosijeaFromFolderType(string folderType)
        {
            // Mapping based on analysis document:
            // FL -> may be "Dosije klijenta FL / PL" or "Dosije paket računa"
            // PL -> "Dosije klijenta PL" or "Dosije klijenta FL / PL"
            // ACC -> "Dosije paket računa"
            // D -> "Dosije depozita"

            return folderType?.ToUpperInvariant() switch
            {
                "FL" => "Dosije klijenta FL / PL",  // Will be resolved later using ClientSegment
                "PL" => "Dosije klijenta PL",
                "ACC" => "Dosije paket računa",
                "D" => "Dosije depozita",
                _ => null
            };
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
