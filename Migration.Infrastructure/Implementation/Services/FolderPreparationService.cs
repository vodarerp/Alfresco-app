using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Abstraction.Models;
using Migration.Extensions.SqlServer;
using SqlServer.Abstraction.Interfaces;
using System.Collections.Concurrent;

namespace Migration.Infrastructure.Implementation.Services
{
    /// <summary>
    /// Service for preparing all destination folders BEFORE document move.
    /// FAZA 3 in the migration pipeline (NEW PHASE).
    /// Uses parallel processing (30-50 concurrent tasks) to create folders efficiently.
    /// Pattern: Parallel Tasks with SemaphoreSlim throttling
    /// </summary>
    public class FolderPreparationService : IFolderPreparationService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _fileLogger;
        private readonly ILogger _dbLogger;
        
        private readonly string _rootDestinationFolderId;

        private const int MAX_PARALLELISM = 50; // 30-50 concurrent folder creations
        private const int CHECKPOINT_INTERVAL = 1000; // Save checkpoint every 1000 folders

        private long _foldersCreated = 0;
        private int _totalFolders = 0; // Cache total folders count to avoid repeated queries
        private readonly ConcurrentBag<string> _errors = new();

        // Track created folder IDs for FolderStaging updates
        private readonly ConcurrentBag<(string FolderPath, string AlfrescoFolderId, bool Success, string? Error)> _folderResults = new();

        public FolderPreparationService(
            IServiceScopeFactory scopeFactory,
            ILoggerFactory logger,
            IOptions<MigrationOptions> migrationOptions)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _fileLogger = logger.CreateLogger("FileLogger");
            _dbLogger = logger.CreateLogger("DbLogger");
            _rootDestinationFolderId = migrationOptions?.Value?.RootDestinationFolderId ?? throw new ArgumentNullException(nameof(migrationOptions));
        }

        public async Task PrepareAllFoldersAsync(CancellationToken ct = default)
        {
            try
            {
                _fileLogger.LogInformation("🏗️  Starting parallel folder preparation with {MaxParallelism} concurrent tasks", MAX_PARALLELISM);

                // ====================================================================
                // STEP 0: Reset stuck items from previous crashed run
                // ====================================================================
                _fileLogger.LogInformation("Resetting stuck items...");
                await ResetStuckItemsAsync(ct).ConfigureAwait(false);
                await ResetStuckFoldersAsync(ct).ConfigureAwait(false);

                // ====================================================================
                // STEP 1: Get all unique destination folders from DocStaging
                // ====================================================================
                var uniqueFolders = await GetUniqueFoldersAsync(ct).ConfigureAwait(false);
                var totalFolders = uniqueFolders.Count;
                _totalFolders = totalFolders; // Cache for GetProgressAsync

                _fileLogger.LogInformation(
                    "Found {TotalFolders} unique destination folders to create",
                    totalFolders);

                if (totalFolders == 0)
                {
                    _fileLogger.LogWarning("No folders to create - DocStaging might be empty");
                    return;
                }

                // Update phase checkpoint with total items
                await UpdateTotalItemsAsync(totalFolders, ct).ConfigureAwait(false);

                // ====================================================================
                // STEP 1.5: Insert all folders into FolderStaging with Status='Pending'
                // ====================================================================
                _fileLogger.LogInformation("Inserting {Count} folders into FolderStaging...", totalFolders);
                await InsertFoldersToStagingAsync(uniqueFolders, ct).ConfigureAwait(false);

                // ====================================================================
                // STEP 2: Resume from checkpoint if exists
                // ====================================================================
                var checkpoint = await GetCheckpointAsync(ct).ConfigureAwait(false);
                var startIndex = checkpoint?.LastProcessedIndex ?? 0;

                if (startIndex > 0)
                {
                    _fileLogger.LogInformation(
                        "Resuming from checkpoint: {StartIndex}/{TotalFolders} folders already created",
                        startIndex, totalFolders);
                    _foldersCreated = startIndex;
                }

                // ====================================================================
                // STEP 3: Parallel folder creation with SemaphoreSlim throttling
                // ====================================================================
                var semaphore = new SemaphoreSlim(MAX_PARALLELISM, MAX_PARALLELISM);
                var foldersToProcess = uniqueFolders.Skip(startIndex).ToList();

                _fileLogger.LogInformation(
                    "Starting parallel folder creation: {Remaining} folders remaining",
                    foldersToProcess.Count);

                var tasks = foldersToProcess.Select(async (folder, index) =>
                {
                    await semaphore.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        var (folderId, success, error) = await CreateFolderAsync(folder, ct).ConfigureAwait(false);

                        // Track result for batch update
                        _folderResults.Add((folder.FolderPath, folderId ?? "", success, error));

                        var current = Interlocked.Increment(ref _foldersCreated);

                        // Batch update FolderStaging every 500 folders
                        if (current % 500 == 0)
                        {
                            await BatchUpdateFolderStagingAsync(ct).ConfigureAwait(false);
                        }

                        // Checkpoint every 1000 folders
                        if (current % CHECKPOINT_INTERVAL == 0)
                        {
                            await SaveCheckpointAsync(current, ct).ConfigureAwait(false);
                            _fileLogger.LogInformation(
                                "Progress: {Created}/{Total} folders created ({Percentage:F1}%)",
                                current, totalFolders, (current / (double)totalFolders) * 100);
                        }
                    }
                    catch (Exception ex)
                    {
                        _fileLogger.LogError(ex,
                            "Failed to create folder: {RootId}/{Path}",
                            folder.DestinationRootId, folder.FolderPath);
                        _errors.Add($"{folder.DestinationRootId}/{folder.FolderPath}: {ex.Message}");

                        // Track failed result
                        _folderResults.Add((folder.FolderPath, "", false, ex.Message));
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks).ConfigureAwait(false);

                // ====================================================================
                // STEP 4: Final batch update for remaining folders
                // ====================================================================
                await BatchUpdateFolderStagingAsync(ct).ConfigureAwait(false);

                // ====================================================================
                // STEP 5: Final checkpoint save
                // ====================================================================
                await SaveCheckpointAsync(_foldersCreated, ct).ConfigureAwait(false);

                if (_errors.Count > 0)
                {
                    _fileLogger.LogWarning(
                        "⚠️  Folder preparation completed with {Errors} errors out of {Total} folders",
                        _errors.Count, totalFolders);

                    foreach (var error in _errors.Take(10))
                    {
                        _fileLogger.LogError("Error: {Error}", error);
                    }

                    if (_errors.Count > 10)
                    {
                        _fileLogger.LogError("... and {More} more errors", _errors.Count - 10);
                    }
                }
                else
                {
                    _fileLogger.LogInformation(
                        "✅ Folder preparation completed successfully: {Total} folders created",
                        totalFolders);
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex, "❌ Fatal error in folder preparation");
                throw;
            }
        }

        public async Task<int> GetTotalFolderCountAsync(CancellationToken ct = default)
        {
            // If total is already cached, return it (avoids repeated queries during PrepareAllFoldersAsync)
            if (_totalFolders > 0)
            {
                return _totalFolders;
            }

            // Otherwise, query database to get total count
            var folders = await GetUniqueFoldersAsync(ct).ConfigureAwait(false);
            _totalFolders = folders.Count;
            return _totalFolders;
        }

        public Task<(int Created, int Total)> GetProgressAsync(CancellationToken ct = default)
        {
            // Use cached values - no database query needed
            var created = (int)Interlocked.Read(ref _foldersCreated);
            var total = _totalFolders;
            return Task.FromResult((created, total));
        }

        // ====================================================================
        // PRIVATE HELPER METHODS
        // ====================================================================

        private async Task<List<UniqueFolderInfo>> GetUniqueFoldersAsync(CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);

                try
                {
                    // Query DocStaging for DISTINCT (TargetDossierType, DossierDestFolderId) combinations
                    // This gives us all unique destination folders
                    var uniqueFolders = await docRepo.GetUniqueDestinationFoldersAsync(ct).ConfigureAwait(false);

                    await uow.CommitAsync(ct).ConfigureAwait(false);

                    return uniqueFolders;
                }
                catch
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex, "Error getting unique folders from DocStaging");
                throw;
            }
        }

        private async Task<(string? FolderId, bool Success, string? Error)> CreateFolderAsync(UniqueFolderInfo folder, CancellationToken ct)
        {
            try
            {
                // Use DocumentResolver to create folder hierarchy
                // DocumentResolver uses lock striping (Problem #3) so it's safe for concurrent calls

                await using var scope = _scopeFactory.CreateAsyncScope();
                var documentResolver = scope.ServiceProvider.GetRequiredService<IDocumentResolver>();

                // STEP 1: Create parent dossier folder (e.g., DOSSIERS-ACC) under RootDestinationFolderId if it doesn't exist
                // folder.DestinationRootId contains the name like "DOSSIERS-ACC", "DOSSIERS-LE", etc.
                // We need to create this folder under _rootDestinationFolderId first
                var parentDossierFolderId = await documentResolver.ResolveAsync(
                    _rootDestinationFolderId,
                    folder.DestinationRootId, // e.g., "DOSSIERS-ACC"
                    null, // No special properties for parent folder
                    ct).ConfigureAwait(false);

                // STEP 2: Create the actual dossier hierarchy under the parent folder
                // Split folder path into parts: "ACC-12345/2024/01" → ["ACC-12345", "2024", "01"]
                var pathParts = folder.FolderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (pathParts.Length == 0)
                {
                    _fileLogger.LogWarning("Empty folder path for {RootId}", folder.DestinationRootId);
                    return (null, false, "Empty folder path");
                }

                // Create each level in hierarchy starting from the parent dossier folder
                var currentParentId = parentDossierFolderId;

                for (int i = 0; i < pathParts.Length; i++)
                {
                    var folderName = pathParts[i];

                    // Only add properties to the FIRST level (main dossier folder)
                    // Sub-folders (year/month) don't need special properties
                    var isMainDossierFolder = (i == 0);

                    if (isMainDossierFolder)
                    {
                        // Use new overload with UniqueFolderInfo for property enrichment
                        currentParentId = await documentResolver.ResolveAsync(
                            currentParentId,
                            folderName,
                            folder.Properties, // May be null
                            folder, // Pass UniqueFolderInfo for property building
                            ct).ConfigureAwait(false);
                    }
                    else
                    {
                        // Sub-folders created without special properties
                        currentParentId = await documentResolver.ResolveAsync(
                            currentParentId,
                            folderName,
                            null, // No properties for sub-folders
                            ct).ConfigureAwait(false);
                    }
                }

                _fileLogger.LogDebug(
                    "Created folder hierarchy: {RootDestinationFolderId}/{ParentFolder}/{Path} → {FinalId}",
                    _rootDestinationFolderId, folder.DestinationRootId, folder.FolderPath, currentParentId);

                // STEP 3: Update DocStaging.DestinationFolderId for all documents in this folder
                await UpdateDocumentDestinationFolderIdAsync(
                    folder.FolderPath,
                    currentParentId,
                    ct).ConfigureAwait(false);

                return (currentParentId, true, null);
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex, "Error creating folder: {Path}", folder.FolderPath);
                return (null, false, ex.Message);
            }
        }

        private async Task UpdateDocumentDestinationFolderIdAsync(
            string dossierDestFolderId,
            string alfrescoFolderId,
            CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var rowsUpdated = await docRepo.UpdateDestinationFolderIdAsync(
                        dossierDestFolderId,
                        alfrescoFolderId,
                        ct).ConfigureAwait(false);

                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    if (rowsUpdated > 0)
                    {
                        _fileLogger.LogDebug(
                            "Updated {Count} documents with DestinationFolderId={FolderId} for DossierDestFolderId='{DossierId}'",
                            rowsUpdated, alfrescoFolderId, dossierDestFolderId);
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
                _fileLogger.LogError(ex,
                    "Failed to update DestinationFolderId for DossierDestFolderId='{DossierId}'. Folder ID: {FolderId}",
                    dossierDestFolderId, alfrescoFolderId);
                // Don't throw - this is not critical for folder creation
                // MoveService will fail gracefully if DestinationFolderId is null
            }
        }

        private async Task<PhaseCheckpoint?> GetCheckpointAsync(CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var phaseCheckpointRepo = scope.ServiceProvider.GetRequiredService<IPhaseCheckpointRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var checkpoint = await phaseCheckpointRepo.GetCheckpointAsync(MigrationPhase.FolderPreparation, ct).ConfigureAwait(false);
                    await uow.CommitAsync(ct).ConfigureAwait(false);
                    return checkpoint;
                }
                catch
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex, "Error getting checkpoint");
                return null;
            }
        }

        private async Task SaveCheckpointAsync(long foldersCreated, CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var phaseCheckpointRepo = scope.ServiceProvider.GetRequiredService<IPhaseCheckpointRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);

                try
                {
                    await phaseCheckpointRepo.UpdateProgressAsync(
                        MigrationPhase.FolderPreparation,
                        lastProcessedIndex: (int)foldersCreated,
                        lastProcessedId: null,
                        totalProcessed: foldersCreated,
                        ct).ConfigureAwait(false);

                    await uow.CommitAsync(ct).ConfigureAwait(false);

                    _fileLogger.LogDebug("Checkpoint saved: {FoldersCreated} folders", foldersCreated);
                }
                catch
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning(ex, "Failed to save checkpoint");
            }
        }

        private async Task UpdateTotalItemsAsync(int totalItems, CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var phaseCheckpointRepo = scope.ServiceProvider.GetRequiredService<IPhaseCheckpointRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);

                try
                {
                    // Update TotalItems field in PhaseCheckpoint for progress calculation
                    var checkpoint = await phaseCheckpointRepo.GetCheckpointAsync(MigrationPhase.FolderPreparation, ct).ConfigureAwait(false);

                    if (checkpoint != null)
                    {
                        checkpoint.TotalItems = totalItems;
                        await phaseCheckpointRepo.UpdateAsync(checkpoint, ct).ConfigureAwait(false);
                    }

                    await uow.CommitAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning(ex, "Failed to update total items in checkpoint");
            }
        }

        private async Task ResetStuckItemsAsync(CancellationToken ct)
        {
            try
            {
                var timeout = TimeSpan.FromMinutes(30); // Default 30 minutes timeout for stuck items
                _fileLogger.LogDebug("Checking for stuck documents with timeout: 30 minutes");

                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var resetCount = await docRepo.ResetStuckDocumentsAsync(
                        uow.Connection,
                        uow.Transaction,
                        timeout,
                        ct).ConfigureAwait(false);

                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    if (resetCount > 0)
                    {
                        _fileLogger.LogWarning(
                            "Reset {Count} stuck documents that were IN PROGRESS for more than 30 minutes",
                            resetCount);
                        _dbLogger.LogWarning(
                            "Reset {Count} stuck documents (timeout: 30 minutes)",
                            resetCount);
                    }
                    else
                    {
                        _fileLogger.LogInformation("No stuck documents found");
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
                _fileLogger.LogWarning(ex, "Failed to reset stuck documents: {Error}", ex.Message);
                _dbLogger.LogError(ex, "Failed to reset stuck documents");
            }
        }

        private async Task ResetStuckFoldersAsync(CancellationToken ct)
        {
            try
            {
                var timeout = TimeSpan.FromMinutes(30); // Default 30 minutes timeout for stuck folders
                _fileLogger.LogDebug("Checking for stuck folders with timeout: 30 minutes");

                await using var scope = _scopeFactory.CreateAsyncScope();
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
                            "Reset {Count} stuck folders that were IN PROGRESS for more than 30 minutes",
                            resetCount);
                        _dbLogger.LogWarning(
                            "Reset {Count} stuck folders (timeout: 30 minutes)",
                            resetCount);
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
                _fileLogger.LogWarning(ex, "Failed to reset stuck folders: {Error}", ex.Message);
                _dbLogger.LogError(ex, "Failed to reset stuck folders");
            }
        }

        private async Task InsertFoldersToStagingAsync(List<UniqueFolderInfo> uniqueFolders, CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var foldersToInsert = uniqueFolders.Select(f => new FolderStaging
                    {
                        Name = f.FolderPath.Split('/').LastOrDefault() ?? f.FolderPath,
                        DestFolderId = f.FolderPath,
                        DossierDestFolderId = f.DestinationRootId,
                        Status = MigrationStatus.Ready.ToDbString(),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }).ToList();

                    // Use InsertManyIgnoreDuplicatesAsync to handle potential duplicates
                    var insertedCount = await folderRepo.InsertManyIgnoreDuplicatesAsync(foldersToInsert, ct).ConfigureAwait(false);
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    _fileLogger.LogInformation("Inserted {Count} folders into FolderStaging with Status='Pending'", insertedCount);
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex, "Failed to insert folders into FolderStaging");
                _dbLogger.LogError(ex, "Failed to insert folders into FolderStaging");
                // Don't throw - this is not critical, we can continue without FolderStaging tracking
            }
        }

        private async Task BatchUpdateFolderStagingAsync(CancellationToken ct)
        {
            try
            {
                // Get all pending results and clear the bag
                var results = _folderResults.ToList();
                if (results.Count == 0)
                {
                    return;
                }

                // Clear processed results
                while (_folderResults.TryTake(out _)) { }

                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    // Prepare updates for batch operation
                    var updates = results.Select(r => (
                        DestFolderId: r.FolderPath,
                        Status: r.Success ? MigrationStatus.Done.ToDbString() : MigrationStatus.Error.ToDbString(),
                        NodeId: r.AlfrescoFolderId
                    )).ToList();

                    // Use repository extension method for batch update
                    await folderRepo.BatchUpdateFoldersByDestFolderIdAsync(
                        uow.Connection,
                        uow.Transaction,
                        updates,
                        ct).ConfigureAwait(false);

                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    // Log errors
                    var failures = results.Where(r => !r.Success && !string.IsNullOrEmpty(r.Error)).ToList();
                    foreach (var failure in failures)
                    {
                        _fileLogger.LogWarning("Folder {Path} failed: {Error}", failure.FolderPath, failure.Error);
                    }

                    var successCount = results.Count(r => r.Success);
                    var failedCount = results.Count - successCount;

                    _fileLogger.LogInformation(
                        "Batch updated {Total} folders in FolderStaging: {Success} succeeded, {Failed} failed",
                        results.Count, successCount, failedCount);
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning(ex, "Failed to batch update FolderStaging");
                _dbLogger.LogError(ex, "Failed to batch update FolderStaging");
                // Don't throw - this is not critical
            }
        }
    }
}
