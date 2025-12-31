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
using System.Text;

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
        private readonly ILogger _uiLogger;

        private readonly string _rootDestinationFolderId;

        private readonly int _maxParallelism; // Configurable concurrent folder creations (default: 50)
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
            _uiLogger = logger.CreateLogger("UiLogger");
            _rootDestinationFolderId = migrationOptions?.Value?.RootDestinationFolderId ?? throw new ArgumentNullException(nameof(migrationOptions));

            // Use MaxDegreeOfParallelism from config or default to 50
            _maxParallelism = migrationOptions.Value.MaxDegreeOfParallelism > 0
                ? migrationOptions.Value.MaxDegreeOfParallelism
                : 50;
        }

        public async Task PrepareAllFoldersAsync(CancellationToken ct = default)
        {
            try
            {
                _fileLogger.LogInformation("🏗️  Starting parallel folder preparation with {MaxParallelism} concurrent tasks", _maxParallelism);

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
                    _fileLogger.LogInformation("✅ No new folders to create - all folders already exist or no documents ready for processing");
                    _dbLogger.LogInformation("No new folders to create - skipping folder preparation");
                    _uiLogger.LogInformation("Folder Preparation: No new folders needed");

                    // Update phase checkpoint to indicate completion (0 folders to process)
                    await UpdateTotalItemsAsync(0, ct).ConfigureAwait(false);

                    // Check if there are documents ready for Move phase
                    var documentsReady = await CheckDocumentsReadyForMoveAsync(ct).ConfigureAwait(false);

                    if (documentsReady > 0)
                    {
                        _fileLogger.LogInformation("✅ FolderPreparation phase completed - {Count} documents ready for Move phase", documentsReady);
                        _dbLogger.LogInformation("FolderPreparation skipped - {Count} documents ready for move", documentsReady);
                        _uiLogger.LogInformation("Folder Preparation: Skipped (all folders exist) - {Count} documents ready for Move", documentsReady);
                    }
                    else
                    {
                        _fileLogger.LogWarning("⚠️ No documents ready for Move phase - DocStaging might be empty or all documents already processed");
                        _dbLogger.LogWarning("No documents ready for move after FolderPreparation");
                        _uiLogger.LogWarning("Folder Preparation: No documents ready for migration");
                    }

                    // Mark checkpoint as having processed 0 items (which is correct - nothing to do)
                    await SaveCheckpointAsync(0, ct).ConfigureAwait(false);

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
                var semaphore = new SemaphoreSlim(_maxParallelism, _maxParallelism);
                //var foldersToProcess = uniqueFolders.Skip(startIndex).ToList();
                var foldersToProcess = uniqueFolders.ToList();

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

                        // ✅ Batch update FolderStaging every 100 folders (reduced from 500 for better crash recovery)
                        if (current % 100 == 0)
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
                        _fileLogger.LogError(
                            "[{Method}] Failed to create folder: {RootId}/{Path} - {ErrorType}: {Message}",
                            nameof(PrepareAllFoldersAsync), folder.DestinationRootId, folder.FolderPath, ex.GetType().Name, ex.Message);
                        _dbLogger.LogError(ex, "[{Method}] Failed to create folder: {RootId}/{Path}",
                            nameof(PrepareAllFoldersAsync), folder.DestinationRootId, folder.FolderPath);
                        _uiLogger.LogWarning("Folder creation error: {Path}", folder.FolderPath);
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
                _fileLogger.LogError("[{Method}] ❌ Fatal error in folder preparation: {ErrorType} - {Message}",
                    nameof(PrepareAllFoldersAsync), ex.GetType().Name, ex.Message);
                _dbLogger.LogError(ex, "[{Method}] Fatal error in folder preparation",
                    nameof(PrepareAllFoldersAsync));
                _uiLogger.LogError("Critical error in folder preparation: {Error}", ex.Message);
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
                _fileLogger.LogInformation("GetUniqueFoldersAsync: Starting query for unique destination folders from DocStaging");

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

                    _fileLogger.LogInformation("GetUniqueFoldersAsync: Successfully retrieved {Count} unique folders from DocStaging", uniqueFolders.Count);

                    // Log detailed breakdown of folders by root
                    var foldersByRoot = uniqueFolders.GroupBy(f => f.DestinationRootId).ToList();
                    foreach (var group in foldersByRoot)
                    {
                        _fileLogger.LogInformation("GetUniqueFoldersAsync: Root '{RootId}' has {Count} unique folders", group.Key, group.Count());
                    }

                    // Log first few folders as sample
                    var sampleFolders = uniqueFolders.Take(5).ToList();
                    foreach (var folder in sampleFolders)
                    {
                        _fileLogger.LogInformation("GetUniqueFoldersAsync: Sample folder - RootId: {RootId}, Path: {Path}, Properties: {PropCount}",
                            folder.DestinationRootId, folder.FolderPath, folder.Properties?.Count ?? 0);
                    }

                    return uniqueFolders;
                }
                catch (Exception ex)
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    _fileLogger.LogError(ex, "GetUniqueFoldersAsync: Database transaction failed during query. ErrorType: {ErrorType}, Message: {Message}",
                        ex.GetType().Name, ex.Message);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("[{Method}] Error getting unique folders from DocStaging: {ErrorType} - {Message}, StackTrace: {StackTrace}",
                    nameof(GetUniqueFoldersAsync), ex.GetType().Name, ex.Message, ex.StackTrace);
                _dbLogger.LogError(ex, "[{Method}] Error getting unique folders from DocStaging",
                    nameof(GetUniqueFoldersAsync));
                _uiLogger.LogError("Database error while getting folders");
                throw;
            }
        }

        private async Task<(string? FolderId, bool Success, string? Error)> CreateFolderAsync(UniqueFolderInfo folder, CancellationToken ct)
        {
            try
            {
                _fileLogger.LogInformation(
                        "CreateFolderAsync: Starting folder creation {@Folder}",
                        new
                        {
                            folder.DestinationRootId,
                            folder.FolderPath,
                            CacheKey = folder.CacheKey ?? "NULL",
                            TipProizvoda = folder.TipProizvoda ?? "NULL",
                            CoreId = folder.CoreId ?? "NULL",
                            CreationDate = folder.CreationDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "NULL",
                            TargetDossierType = folder.TargetDossierType?.ToString() ?? "NULL",
                            folder.IsCreated,
                            PropertiesCount = folder.Properties?.Count ?? 0
                        }
                    );


                // Log properties if they exist
                if (folder.Properties != null && folder.Properties.Count > 0)
                {
                   
                    _fileLogger.LogInformation("CreateFolderAsync: Folder properties for '{Path}':", folder.FolderPath);
                    var sb = new StringBuilder();

                    sb.AppendLine("Folder properties:");
                    foreach (var kvp in folder.Properties)
                    {
                        sb.Append("  ")
                      .Append(kvp.Key)
                      .Append(" = ")
                      .AppendLine(kvp.Value?.ToString() ?? "NULL");
                    }
                    _fileLogger.LogInformation("{Properties}", sb.ToString());
                    sb = null;
                }
                else
                {
                    _fileLogger.LogInformation("CreateFolderAsync: No properties for folder '{Path}'", folder.FolderPath);
                }

                await using var scope = _scopeFactory.CreateAsyncScope();
                var documentResolver = scope.ServiceProvider.GetRequiredService<IDocumentResolver>();

                // STEP 1: Create parent dossier folder (e.g., DOSSIERS-ACC) under RootDestinationFolderId if it doesn't exist
                // folder.DestinationRootId contains the name like "DOSSIERS-ACC", "DOSSIERS-LE", etc.
                // We need to create this folder under _rootDestinationFolderId first
                _fileLogger.LogInformation("CreateFolderAsync: STEP 1 - Resolving parent dossier folder '{ParentFolder}' under root '{RootId}'",
                    folder.DestinationRootId, _rootDestinationFolderId);

                var parentDossierFolderId = await documentResolver.ResolveAsync(
                    _rootDestinationFolderId,
                    folder.DestinationRootId, // e.g., "DOSSIERS-ACC"
                    null, // No special properties for parent folder
                    ct).ConfigureAwait(false);

                _fileLogger.LogInformation("CreateFolderAsync: Parent dossier folder '{ParentFolder}' resolved to ID: {ParentDossierFolderId}",
                    folder.DestinationRootId, parentDossierFolderId);

                // STEP 2: Create the actual dossier hierarchy under the parent folder
                // Split folder path into parts: "ACC-12345/2024/01" → ["ACC-12345", "2024", "01"]
                var pathParts = folder.FolderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

               

                if (pathParts.Length == 0)
                {
                    _fileLogger.LogWarning("CreateFolderAsync: Empty folder path for RootId: {RootId}", folder.DestinationRootId);
                    return (null, false, "Empty folder path");
                }
                _fileLogger.LogInformation("CreateFolderAsync: STEP 2 - Creating folder hierarchy with {Levels} levels: {PathParts}",
                   pathParts.Length, string.Join(" -> ", pathParts));
                // Create each level in hierarchy starting from the parent dossier folder
                var currentParentId = parentDossierFolderId;
                bool mainFolderIsCreated = false; // Track if main dossier folder was created

                for (int i = 0; i < pathParts.Length; i++)
                {
                    var folderName = pathParts[i];

                    // Only add properties to the FIRST level (main dossier folder)
                    // Sub-folders (year/month) don't need special properties
                    var isMainDossierFolder = (i == 0);

                    _fileLogger.LogInformation("CreateFolderAsync: Processing level {Level}/{Total} - FolderName: '{FolderName}', IsMainFolder: {IsMain}, CurrentParentId: {ParentId}",
                        i + 1, pathParts.Length, folderName, isMainDossierFolder, currentParentId);

                    if (isMainDossierFolder)
                    {
                        // Use ResolveWithStatusAsync to get both folderId and isCreated flag
                        var (folderId, isCreated) = await documentResolver.ResolveWithStatusAsync(
                            currentParentId,
                            folderName,
                            folder.Properties, // May be null
                            folder,
                            ct).ConfigureAwait(false);

                        currentParentId = folderId;
                        mainFolderIsCreated = isCreated;

                        _fileLogger.LogInformation(
                            "CreateFolderAsync: Main dossier folder '{FolderName}' resolved - FolderId: {FolderId}, IsCreated: {IsCreated}, Properties: {PropCount}",
                            folderName, folderId, isCreated, folder.Properties?.Count ?? 0);
                    }
                    else
                    {
                        // Sub-folders created without special properties
                        var previousParentId = currentParentId;
                        currentParentId = await documentResolver.ResolveAsync(
                            currentParentId,
                            folderName,
                            null, // No properties for sub-folders
                            ct).ConfigureAwait(false);

                        _fileLogger.LogInformation(
                            "CreateFolderAsync: Sub-folder '{FolderName}' resolved - FolderId: {FolderId}, ParentId: {ParentId}",
                            folderName, currentParentId, previousParentId);
                    }
                }

                _fileLogger.LogInformation(
                    "CreateFolderAsync: Successfully created folder hierarchy - RootDestinationFolderId: {RootId}, ParentFolder: {ParentFolder}, Path: {Path}, FinalFolderId: {FinalId}, MainFolderCreated: {IsCreated}",
                    _rootDestinationFolderId, folder.DestinationRootId, folder.FolderPath, currentParentId, mainFolderIsCreated);

                // STEP 3: Update DocStaging.DestinationFolderId and DossierDestFolderIsCreated for all documents in this folder
                _fileLogger.LogInformation("CreateFolderAsync: STEP 3 - Updating DocStaging for folder '{Path}' with FolderId: {FolderId}",
                    folder.FolderPath, currentParentId);

                await UpdateDocumentDestinationFolderIdAsync(
                    folder.FolderPath,
                    currentParentId,
                    mainFolderIsCreated,
                    ct).ConfigureAwait(false);

                _fileLogger.LogInformation("CreateFolderAsync: Completed successfully for folder '{Path}' -> FolderId: {FolderId}",
                    folder.FolderPath, currentParentId);

                return (currentParentId, true, null);
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("[{Method}] Error creating folder: RootId: {RootId}, Path: {Path}, ErrorType: {ErrorType}, Message: {Message}, StackTrace: {StackTrace}",
                    nameof(CreateFolderAsync), folder.DestinationRootId, folder.FolderPath, ex.GetType().Name, ex.Message, ex.StackTrace);
                _dbLogger.LogError(ex, "[{Method}] Error creating folder: {Path}",
                    nameof(CreateFolderAsync), folder.FolderPath);
                _uiLogger.LogWarning("Failed to create folder {Path}", folder.FolderPath);
                return (null, false, ex.Message);
            }
        }

        private async Task UpdateDocumentDestinationFolderIdAsync(
            string dossierDestFolderId,
            string alfrescoFolderId,
            bool isCreated,
            CancellationToken ct)
        {
            const int MAX_RETRIES = 3;
            int attempt = 0;

            while (attempt < MAX_RETRIES)
            {
                try
                {
                    if (attempt > 0)
                    {
                        _fileLogger.LogInformation(
                            "UpdateDocumentDestinationFolderIdAsync: Retry attempt {Attempt}/{Max} - DossierDestFolderId: '{DossierId}'",
                            attempt, MAX_RETRIES, dossierDestFolderId);
                    }

                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

                    await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                    try
                    {
                        var rowsUpdated = await docRepo.UpdateDestinationFolderIdAsync(
                            dossierDestFolderId,
                            alfrescoFolderId,
                            isCreated,
                            ct).ConfigureAwait(false);

                        await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                        if (rowsUpdated > 0)
                        {
                            _fileLogger.LogInformation(
                                "✅ UpdateDocumentDestinationFolderIdAsync: Successfully updated {Count} documents - " +
                                "DestinationFolderId: {FolderId}, IsCreated: {IsCreated}, DossierDestFolderId: '{DossierId}'",
                                rowsUpdated, alfrescoFolderId, isCreated, dossierDestFolderId);
                            return; // ✅ SUCCESS - exit retry loop
                        }
                        else
                        {
                            _fileLogger.LogWarning(
                                "⚠️ UpdateDocumentDestinationFolderIdAsync: No documents updated (0 rows affected) - " +
                                "DossierDestFolderId: '{DossierId}', AlfrescoFolderId: {FolderId}",
                                dossierDestFolderId, alfrescoFolderId);
                            return; // OK - možda nema dokumenata za taj folder
                        }
                    }
                    catch (Exception ex)
                    {
                        await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                        throw; // Rethrow za retry logiku
                    }
                }
                catch (Exception ex)
                {
                    attempt++;

                    if (attempt >= MAX_RETRIES)
                    {
                        // ❌ FINAL FAILURE - after all retries exhausted
                        _fileLogger.LogError(ex,
                            "❌ CRITICAL: UpdateDocumentDestinationFolderIdAsync FAILED after {Attempts} attempts - " +
                            "DossierDestFolderId: '{DossierId}', AlfrescoFolderId: {FolderId}, ErrorType: {ErrorType}, Message: {Message}",
                            MAX_RETRIES, dossierDestFolderId, alfrescoFolderId, ex.GetType().Name, ex.Message);

                        _dbLogger.LogError(ex,
                            "Failed to update DestinationFolderId after {Attempts} retries for DossierDestFolderId='{DossierId}'",
                            MAX_RETRIES, dossierDestFolderId);

                        _uiLogger.LogError(
                            "Kritična greška: Folder {DossierId} kreiran ali dokumenti nisu update-ovani!",
                            dossierDestFolderId);

                        // 🔴 THROW - ovo je kritično, folder je kreiran ali dokumenti nemaju DestinationFolderId
                        // Move faza će failovati za te dokumente
                        throw new InvalidOperationException(
                            $"Failed to update DestinationFolderId for '{dossierDestFolderId}' after {MAX_RETRIES} retries. " +
                            $"Folder created (ID: {alfrescoFolderId}) but documents not linked!",
                            ex);
                    }

                    // Exponential backoff: 2^attempt seconds (1s, 2s, 4s)
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));

                    _fileLogger.LogWarning(
                        "⚠️ UpdateDocumentDestinationFolderIdAsync: Retry {Attempt}/{Max} after {Delay}s - " +
                        "DossierDestFolderId: '{DossierId}', Error: {Error}",
                        attempt, MAX_RETRIES, delay.TotalSeconds, dossierDestFolderId, ex.Message);

                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
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
                _fileLogger.LogError("[{Method}] Error getting checkpoint: {ErrorType} - {Message}",
                    nameof(GetCheckpointAsync), ex.GetType().Name, ex.Message);
                _dbLogger.LogError(ex, "[{Method}] Error getting checkpoint",
                    nameof(GetCheckpointAsync));
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
                _fileLogger.LogWarning("[{Method}] Failed to save checkpoint: {ErrorType} - {Message}",
                    nameof(SaveCheckpointAsync), ex.GetType().Name, ex.Message);
                _dbLogger.LogWarning(ex, "[{Method}] Failed to save checkpoint",
                    nameof(SaveCheckpointAsync));
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
                _fileLogger.LogWarning("[{Method}] Failed to update total items in checkpoint: {ErrorType} - {Message}",
                    nameof(UpdateTotalItemsAsync), ex.GetType().Name, ex.Message);
                _dbLogger.LogWarning(ex, "[{Method}] Failed to update total items in checkpoint",
                    nameof(UpdateTotalItemsAsync));
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
                _fileLogger.LogInformation("InsertFoldersToStagingAsync: Starting insertion of {Count} folders into FolderStaging", uniqueFolders.Count);

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

                    _fileLogger.LogInformation("InsertFoldersToStagingAsync: Prepared {Count} FolderStaging records for insertion", foldersToInsert.Count);

                    // Log first few samples
                    var sampleFolders = foldersToInsert.Take(5).ToList();
                    foreach (var folder in sampleFolders)
                    {
                        _fileLogger.LogInformation("InsertFoldersToStagingAsync: Sample folder - Name: '{Name}', DestFolderId: '{DestFolderId}', DossierDestFolderId: '{DossierDestFolderId}', Status: {Status}",
                            folder.Name, folder.DestFolderId, folder.DossierDestFolderId, folder.Status);
                    }

                    // Use InsertManyIgnoreDuplicatesAsync to handle potential duplicates
                    _fileLogger.LogInformation("InsertFoldersToStagingAsync: Executing bulk insert (ignoring duplicates)...");

                    var insertedCount = await folderRepo.InsertManyIgnoreDuplicatesAsync(foldersToInsert, ct).ConfigureAwait(false);
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    var skippedCount = foldersToInsert.Count - insertedCount;
                    _fileLogger.LogInformation("InsertFoldersToStagingAsync: Successfully inserted {InsertedCount} folders into FolderStaging (Skipped {SkippedCount} duplicates, Total: {TotalCount})",
                        insertedCount, skippedCount, foldersToInsert.Count);
                }
                catch (Exception ex)
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    _fileLogger.LogError(ex, "InsertFoldersToStagingAsync: Database transaction failed during insertion - ErrorType: {ErrorType}, Message: {Message}",
                        ex.GetType().Name, ex.Message);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex, "InsertFoldersToStagingAsync: Failed to insert folders into FolderStaging - ErrorType: {ErrorType}, Message: {Message}, StackTrace: {StackTrace}",
                    ex.GetType().Name, ex.Message, ex.StackTrace);
                _dbLogger.LogError(ex, "Failed to insert folders into FolderStaging");
                _uiLogger.LogWarning("Could not save folder tracking data");
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
                    _fileLogger.LogInformation("BatchUpdateFolderStagingAsync: No pending folder results to update");
                    return;
                }

                _fileLogger.LogInformation("BatchUpdateFolderStagingAsync: Starting batch update for {Count} folder results", results.Count);

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

                    var successCount = results.Count(r => r.Success);
                    var failedCount = results.Count - successCount;

                    _fileLogger.LogInformation("BatchUpdateFolderStagingAsync: Prepared {Total} updates - {Success} successful, {Failed} failed",
                        updates.Count, successCount, failedCount);

                    // Log sample updates
                    var sampleUpdates = updates.Take(5).ToList();
                    foreach (var update in sampleUpdates)
                    {
                        _fileLogger.LogInformation("BatchUpdateFolderStagingAsync: Sample update - DestFolderId: '{DestFolderId}', Status: {Status}, NodeId: {NodeId}",
                            update.DestFolderId, update.Status, update.NodeId ?? "NULL");
                    }

                    // Use repository extension method for batch update
                    _fileLogger.LogInformation("BatchUpdateFolderStagingAsync: Executing batch update query...");

                    await folderRepo.BatchUpdateFoldersByDestFolderIdAsync(
                        uow.Connection,
                        uow.Transaction,
                        updates,
                        ct).ConfigureAwait(false);

                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    // Log errors in detail
                    var failures = results.Where(r => !r.Success && !string.IsNullOrEmpty(r.Error)).ToList();
                    if (failures.Count > 0)
                    {
                        _fileLogger.LogWarning("BatchUpdateFolderStagingAsync: Found {Count} failed folders", failures.Count);
                        foreach (var failure in failures)
                        {
                            _fileLogger.LogWarning("BatchUpdateFolderStagingAsync: Folder '{Path}' failed - Error: {Error}",
                                failure.FolderPath, failure.Error);
                        }
                    }

                    _fileLogger.LogInformation(
                        "BatchUpdateFolderStagingAsync: Successfully batch updated {Total} folders in FolderStaging - {Success} succeeded, {Failed} failed",
                        results.Count, successCount, failedCount);
                }
                catch (Exception ex)
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    _fileLogger.LogError(ex, "BatchUpdateFolderStagingAsync: Database transaction failed - ErrorType: {ErrorType}, Message: {Message}",
                        ex.GetType().Name, ex.Message);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning("[{Method}] Failed to batch update FolderStaging: ErrorType: {ErrorType}, Message: {Message}, StackTrace: {StackTrace}",
                    nameof(BatchUpdateFolderStagingAsync), ex.GetType().Name, ex.Message, ex.StackTrace);
                _dbLogger.LogError(ex, "[{Method}] Failed to batch update FolderStaging",
                    nameof(BatchUpdateFolderStagingAsync));
                _uiLogger.LogInformation("Could not update folder status tracking");
                // Don't throw - this is not critical
            }
        }

        /// <summary>
        /// Checks how many documents are ready for Move phase (have Status=READY and DestinationFolderId populated)
        /// </summary>
        private async Task<long> CheckDocumentsReadyForMoveAsync(CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var count = await docRepo.CountReadyForProcessingAsync(ct).ConfigureAwait(false);
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                    return count;
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("[{Method}] Failed to check documents ready for move: {ErrorType} - {Message}",
                    nameof(CheckDocumentsReadyForMoveAsync), ex.GetType().Name, ex.Message);
                _dbLogger.LogError(ex, "[{Method}] Failed to check documents ready for move",
                    nameof(CheckDocumentsReadyForMoveAsync));
                return 0;
            }
        }
    }
}
