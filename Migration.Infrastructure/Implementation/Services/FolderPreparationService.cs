using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Oracle.Models;
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
        private readonly IDocStagingRepository _docRepo;
        private readonly IDocumentResolver _documentResolver;
        private readonly IPhaseCheckpointRepository _phaseCheckpointRepo;
        private readonly IUnitOfWork _uow;
        private readonly ILogger<FolderPreparationService> _logger;

        private const int MAX_PARALLELISM = 50; // 30-50 concurrent folder creations
        private const int CHECKPOINT_INTERVAL = 1000; // Save checkpoint every 1000 folders

        private long _foldersCreated = 0;
        private readonly ConcurrentBag<string> _errors = new();

        public FolderPreparationService(
            IDocStagingRepository docRepo,
            IDocumentResolver documentResolver,
            IPhaseCheckpointRepository phaseCheckpointRepo,
            IUnitOfWork uow,
            ILogger<FolderPreparationService> logger)
        {
            _docRepo = docRepo ?? throw new ArgumentNullException(nameof(docRepo));
            _documentResolver = documentResolver ?? throw new ArgumentNullException(nameof(documentResolver));
            _phaseCheckpointRepo = phaseCheckpointRepo ?? throw new ArgumentNullException(nameof(phaseCheckpointRepo));
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            var folders = await GetUniqueFoldersAsync(ct).ConfigureAwait(false);
            return folders.Count;
        }

        public async Task<(int Created, int Total)> GetProgressAsync(CancellationToken ct = default)
        {
            var total = await GetTotalFolderCountAsync(ct).ConfigureAwait(false);
            var created = (int)Interlocked.Read(ref _foldersCreated);
            return (created, total);
        }

        // ====================================================================
        // PRIVATE HELPER METHODS
        // ====================================================================

        private async Task<List<UniqueFolderInfo>> GetUniqueFoldersAsync(CancellationToken ct)
        {
            try
            {
                await _uow.BeginAsync(ct: ct).ConfigureAwait(false);

                // Query DocStaging for DISTINCT (TargetDossierType, DossierDestFolderId) combinations
                // This gives us all unique destination folders
                var uniqueFolders = await _docRepo.GetUniqueDestinationFoldersAsync(ct).ConfigureAwait(false);

                await _uow.CommitAsync(ct).ConfigureAwait(false);

                return uniqueFolders;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting unique folders from DocStaging");
                await _uow.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }
        }

        private async Task CreateFolderAsync(UniqueFolderInfo folder, CancellationToken ct)
        {
            // Use DocumentResolver to create folder hierarchy
            // DocumentResolver uses lock striping (Problem #3) so it's safe for concurrent calls
            // Pattern: destinationRootId = DOSSIER folder, newFolderName = ACC-xxx or subfolder

            // Split folder path into parts: "ACC-12345/2024/01" → ["ACC-12345", "2024", "01"]
            var pathParts = folder.FolderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (pathParts.Length == 0)
            {
                _logger.LogWarning("Empty folder path for {RootId}", folder.DestinationRootId);
                return;
            }

            // Create each level in hierarchy
            var currentParentId = folder.DestinationRootId;

            foreach (var folderName in pathParts)
            {
                // DocumentResolver.ResolveAsync is idempotent: checks if folder exists, creates if not, caches result
                currentParentId = await _documentResolver.ResolveAsync(
                    currentParentId,
                    folderName,
                    folder.Properties, // Pass properties (may be null)
                    ct).ConfigureAwait(false);
            }

            _logger.LogDebug(
                "Created folder hierarchy: {RootId}/{Path} → {FinalId}",
                folder.DestinationRootId, folder.FolderPath, currentParentId);
        }

        private async Task<PhaseCheckpoint?> GetCheckpointAsync(CancellationToken ct)
        {
            try
            {
                await _uow.BeginAsync(ct: ct).ConfigureAwait(false);
                var checkpoint = await _phaseCheckpointRepo.GetCheckpointAsync(MigrationPhase.FolderPreparation, ct).ConfigureAwait(false);
                await _uow.CommitAsync(ct).ConfigureAwait(false);
                return checkpoint;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting checkpoint");
                await _uow.RollbackAsync(ct).ConfigureAwait(false);
                return null;
            }
        }

        private async Task SaveCheckpointAsync(long foldersCreated, CancellationToken ct)
        {
            try
            {
                await _uow.BeginAsync(ct: ct).ConfigureAwait(false);

                await _phaseCheckpointRepo.UpdateProgressAsync(
                    MigrationPhase.FolderPreparation,
                    lastProcessedIndex: (int)foldersCreated,
                    lastProcessedId: null,
                    totalProcessed: foldersCreated,
                    ct).ConfigureAwait(false);

                await _uow.CommitAsync(ct).ConfigureAwait(false);

                _logger.LogDebug("Checkpoint saved: {FoldersCreated} folders", foldersCreated);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save checkpoint");
                await _uow.RollbackAsync(ct).ConfigureAwait(false);
            }
        }

        private async Task UpdateTotalItemsAsync(int totalItems, CancellationToken ct)
        {
            try
            {
                await _uow.BeginAsync(ct: ct).ConfigureAwait(false);

                // Update TotalItems field in PhaseCheckpoint for progress calculation
                var checkpoint = await _phaseCheckpointRepo.GetCheckpointAsync(MigrationPhase.FolderPreparation, ct).ConfigureAwait(false);

                if (checkpoint != null)
                {
                    checkpoint.TotalItems = totalItems;
                    await _phaseCheckpointRepo.UpdateAsync(checkpoint, ct).ConfigureAwait(false);
                }

                await _uow.CommitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update total items in checkpoint");
                await _uow.RollbackAsync(ct).ConfigureAwait(false);
            }
        }
    }
}
