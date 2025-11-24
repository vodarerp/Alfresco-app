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
        private readonly ILogger<FolderPreparationService> _logger;
        private readonly string _rootDestinationFolderId;

        private const int MAX_PARALLELISM = 50; // 30-50 concurrent folder creations
        private const int CHECKPOINT_INTERVAL = 1000; // Save checkpoint every 1000 folders

        private long _foldersCreated = 0;
        private int _totalFolders = 0; // Cache total folders count to avoid repeated queries
        private readonly ConcurrentBag<string> _errors = new();

        public FolderPreparationService(
            IServiceScopeFactory scopeFactory,
            ILogger<FolderPreparationService> logger,
            IOptions<MigrationOptions> migrationOptions)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rootDestinationFolderId = migrationOptions?.Value?.RootDestinationFolderId ?? throw new ArgumentNullException(nameof(migrationOptions));
        }

        public async Task PrepareAllFoldersAsync(CancellationToken ct = default)
        {
            try
            {
                _logger.LogInformation("🏗️  Starting parallel folder preparation with {MaxParallelism} concurrent tasks", MAX_PARALLELISM);

                // ====================================================================
                // STEP 1: Get all unique destination folders from DocStaging
                // ====================================================================
                var uniqueFolders = await GetUniqueFoldersAsync(ct).ConfigureAwait(false);
                var totalFolders = uniqueFolders.Count;
                _totalFolders = totalFolders; // Cache for GetProgressAsync

                _logger.LogInformation(
                    "Found {TotalFolders} unique destination folders to create",
                    totalFolders);

                if (totalFolders == 0)
                {
                    _logger.LogWarning("No folders to create - DocStaging might be empty");
                    return;
                }

                // Update phase checkpoint with total items
                await UpdateTotalItemsAsync(totalFolders, ct).ConfigureAwait(false);

                // ====================================================================
                // STEP 2: Resume from checkpoint if exists
                // ====================================================================
                var checkpoint = await GetCheckpointAsync(ct).ConfigureAwait(false);
                var startIndex = checkpoint?.LastProcessedIndex ?? 0;

                if (startIndex > 0)
                {
                    _logger.LogInformation(
                        "Resuming from checkpoint: {StartIndex}/{TotalFolders} folders already created",
                        startIndex, totalFolders);
                    _foldersCreated = startIndex;
                }

                // ====================================================================
                // STEP 3: Parallel folder creation with SemaphoreSlim throttling
                // ====================================================================
                var semaphore = new SemaphoreSlim(MAX_PARALLELISM, MAX_PARALLELISM);
                var foldersToProcess = uniqueFolders.Skip(startIndex).ToList();

                _logger.LogInformation(
                    "Starting parallel folder creation: {Remaining} folders remaining",
                    foldersToProcess.Count);

                var tasks = foldersToProcess.Select(async (folder, index) =>
                {
                    await semaphore.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        await CreateFolderAsync(folder, ct).ConfigureAwait(false);

                        var current = Interlocked.Increment(ref _foldersCreated);

                        // Checkpoint every 1000 folders
                        if (current % CHECKPOINT_INTERVAL == 0)
                        {
                            await SaveCheckpointAsync(current, ct).ConfigureAwait(false);
                            _logger.LogInformation(
                                "Progress: {Created}/{Total} folders created ({Percentage:F1}%)",
                                current, totalFolders, (current / (double)totalFolders) * 100);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to create folder: {RootId}/{Path}",
                            folder.DestinationRootId, folder.FolderPath);
                        _errors.Add($"{folder.DestinationRootId}/{folder.FolderPath}: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks).ConfigureAwait(false);

                // ====================================================================
                // STEP 4: Final checkpoint save
                // ====================================================================
                await SaveCheckpointAsync(_foldersCreated, ct).ConfigureAwait(false);

                if (_errors.Count > 0)
                {
                    _logger.LogWarning(
                        "⚠️  Folder preparation completed with {Errors} errors out of {Total} folders",
                        _errors.Count, totalFolders);

                    foreach (var error in _errors.Take(10))
                    {
                        _logger.LogError("Error: {Error}", error);
                    }

                    if (_errors.Count > 10)
                    {
                        _logger.LogError("... and {More} more errors", _errors.Count - 10);
                    }
                }
                else
                {
                    _logger.LogInformation(
                        "✅ Folder preparation completed successfully: {Total} folders created",
                        totalFolders);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Fatal error in folder preparation");
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
                _logger.LogError(ex, "Error getting unique folders from DocStaging");
                throw;
            }
        }

        private async Task CreateFolderAsync(UniqueFolderInfo folder, CancellationToken ct)
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
                _logger.LogWarning("Empty folder path for {RootId}", folder.DestinationRootId);
                return;
            }

            // Create each level in hierarchy starting from the parent dossier folder
            var currentParentId = parentDossierFolderId;

            foreach (var folderName in pathParts)
            {
                // DocumentResolver.ResolveAsync is idempotent: checks if folder exists, creates if not, caches result
                currentParentId = await documentResolver.ResolveAsync(
                    currentParentId,
                    folderName,
                    folder.Properties, // Pass properties (may be null)
                    ct).ConfigureAwait(false);
            }

            _logger.LogDebug(
                "Created folder hierarchy: {RootDestinationFolderId}/{ParentFolder}/{Path} → {FinalId}",
                _rootDestinationFolderId, folder.DestinationRootId, folder.FolderPath, currentParentId);

            // STEP 3: Update DocStaging.DestinationFolderId for all documents in this folder
            await UpdateDocumentDestinationFolderIdAsync(
                folder.FolderPath,
                currentParentId,
                ct).ConfigureAwait(false);
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
                        _logger.LogDebug(
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
                _logger.LogError(ex,
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
                _logger.LogError(ex, "Error getting checkpoint");
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

                    _logger.LogDebug("Checkpoint saved: {FoldersCreated} folders", foldersCreated);
                }
                catch
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save checkpoint");
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
                _logger.LogWarning(ex, "Failed to update total items in checkpoint");
            }
        }
    }
}
