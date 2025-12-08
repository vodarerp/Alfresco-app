using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces.Services;
using Migration.Abstraction.Interfaces.Wrappers;
using SqlServer.Abstraction.Interfaces;
using System.Data;

namespace Migration.Infrastructure.Implementation.Services
{
    /// <summary>
    /// Orchestrator for the entire migration pipeline.
    /// Executes all 4 phases sequentially with checkpointing and error handling.
    /// Pattern: Sequential Workers + Parallel Tasks
    ///
    /// Supports two migration modes:
    /// - MigrationByFolder (default): FolderDiscovery → DocumentDiscovery → FolderPreparation → Move
    /// - MigrationByDocument: DocumentSearch → FolderPreparation → Move
    /// </summary>
    public class MigrationWorker : IMigrationWorker
    {
        private readonly IFolderDiscoveryService _folderDiscovery;
        private readonly IDocumentDiscoveryService _documentDiscovery;
        private readonly IDocumentSearchService? _documentSearch;
        private readonly IFolderPreparationService _folderPreparation;
        private readonly IMoveService _moveService;
        private readonly IPhaseCheckpointRepository _phaseCheckpointRepo;
        private readonly ILogger _logger;
       
        private readonly string _connectionString;
        private readonly MigrationOptions _migrationOptions;

        public MigrationWorker(
            IFolderDiscoveryService folderDiscovery,
            IDocumentDiscoveryService documentDiscovery,
            IFolderPreparationService folderPreparation,
            IMoveService moveService,
            IPhaseCheckpointRepository phaseCheckpointRepo,
            ILoggerFactory logger,
            IOptions<global::Alfresco.Contracts.SqlServer.SqlServerOptions> sqlOptions,
            IOptions<MigrationOptions> migrationOptions,
            IDocumentSearchService? documentSearch = null)
        {
            _folderDiscovery = folderDiscovery ?? throw new ArgumentNullException(nameof(folderDiscovery));
            _documentDiscovery = documentDiscovery ?? throw new ArgumentNullException(nameof(documentDiscovery));
            _documentSearch = documentSearch; // Optional - only used in MigrationByDocument mode
            _folderPreparation = folderPreparation ?? throw new ArgumentNullException(nameof(folderPreparation));
            _moveService = moveService ?? throw new ArgumentNullException(nameof(moveService));
            _phaseCheckpointRepo = phaseCheckpointRepo ?? throw new ArgumentNullException(nameof(phaseCheckpointRepo));
            _logger = logger.CreateLogger("FileLogger");
            _connectionString = sqlOptions?.Value?.ConnectionString ?? throw new ArgumentNullException(nameof(sqlOptions));
            _migrationOptions = migrationOptions?.Value ?? throw new ArgumentNullException(nameof(migrationOptions));
        }

        public async Task RunAsync(CancellationToken ct = default)
        {
            try
            {
                _logger.LogInformation("Migration pipeline started");

                // Determine migration mode
                if (_migrationOptions.MigrationByDocument)
                {
                    _logger.LogInformation("Migration mode: MigrationByDocument (DocumentSearch -> FolderPreparation -> Move)");

                    // Validate that DocumentSearchService is available
                    if (_documentSearch == null)
                    {
                        throw new InvalidOperationException(
                            "MigrationByDocument is enabled but IDocumentSearchService is not registered. " +
                            "Please register DocumentSearchService in DI container.");
                    }

                    // Check if DocTypes have changed and reset checkpoint if needed
                    await ValidateAndResetDocTypesCheckpointAsync(ct);

                    // ====================================================================
                    // FAZA 1: DocumentSearch (searches by ecm:docType, populates both tables)
                    // ====================================================================
                    await ExecutePhaseAsync(
                        MigrationPhase.FolderDiscovery,
                        "FAZA 1: DocumentSearch (by ecm:docType)",
                        async (token) => await _documentSearch.RunLoopAsync(token),
                        ct);

                    // Skip DocumentDiscovery phase - it's already done by DocumentSearch
                    _logger.LogInformation("Skipping DocumentDiscovery phase (handled by DocumentSearch)");
                }
                else
                {
                    _logger.LogInformation("Migration mode: MigrationByFolder (FolderDiscovery -> DocumentDiscovery -> FolderPreparation -> Move)");

                    // ====================================================================
                    // FAZA 1: FolderDiscovery
                    // ====================================================================
                    await ExecutePhaseAsync(
                        MigrationPhase.FolderDiscovery,
                        "FAZA 1: FolderDiscovery",
                        async (token) => await _folderDiscovery.RunLoopAsync(token),
                        ct);

                    // ====================================================================
                    // FAZA 2: DocumentDiscovery
                    // ====================================================================
                    await ExecutePhaseAsync(
                        MigrationPhase.DocumentDiscovery,
                        "FAZA 2: DocumentDiscovery",
                        async (token) => await _documentDiscovery.RunLoopAsync(token),
                        ct);
                }

                // ====================================================================
                // FAZA 3: FolderPreparation (common for both modes)
                // ====================================================================
                await ExecutePhaseAsync(
                    MigrationPhase.FolderPreparation,
                    "FAZA 3: FolderPreparation (parallel folder creation)",
                    async (token) => await _folderPreparation.PrepareAllFoldersAsync(token),
                    ct);

                // ====================================================================
                // FAZA 4: Move (common for both modes)
                // ====================================================================
                await ExecutePhaseAsync(
                    MigrationPhase.Move,
                    "FAZA 4: Move (parallel document moves)",
                    async (token) => await _moveService.RunLoopAsync(token),
                    ct);

                _logger.LogInformation("Migration pipeline completed successfully!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration pipeline failed");
                throw;
            }
        }

        public async Task<MigrationPipelineStatus> GetStatusAsync(CancellationToken ct = default)
        {
            try
            {
                // Use dedicated connection for status reads (no conflict with phase services)
                var checkpoints = await ExecuteCheckpointOperationAsync(async (conn, tran) =>
                {
                    var sql = "SELECT * FROM PhaseCheckpoints ORDER BY Phase ASC";
                    var cmd = new CommandDefinition(sql, transaction: tran, cancellationToken: ct);
                    var results = await conn.QueryAsync<PhaseCheckpoint>(cmd);
                    return results.ToList();
                }, ct);

                // Find current phase (first non-completed phase)
                // In MigrationByDocument mode, skip DocumentDiscovery phase (it's handled by DocumentSearch in Phase 1)
                var currentPhaseCheckpoint = checkpoints
                    .OrderBy(c => c.Phase)
                    .Where(c => c.Status != PhaseStatus.Completed)
                    .Where(c => !_migrationOptions.MigrationByDocument || c.Phase != MigrationPhase.DocumentDiscovery)
                    .FirstOrDefault();

                // Filter out DocumentDiscovery in MigrationByDocument mode (for TotalProcessed calculation)
                var relevantCheckpoints = _migrationOptions.MigrationByDocument
                    ? checkpoints.Where(c => c.Phase != MigrationPhase.DocumentDiscovery).ToList()
                    : checkpoints;

                if (currentPhaseCheckpoint == null)
                {
                    // All phases completed
                    var lastPhase = checkpoints.OrderByDescending(c => c.Phase).First();

                    return new MigrationPipelineStatus
                    {
                        CurrentPhase = lastPhase.Phase,
                        CurrentPhaseStatus = PhaseStatus.Completed,
                        CurrentPhaseProgress = 100,
                        StartedAt = relevantCheckpoints.Min(c => c.StartedAt),
                        ElapsedTime = lastPhase.CompletedAt - relevantCheckpoints.Min(c => c.StartedAt),
                        TotalProcessed = relevantCheckpoints.Sum(c => c.TotalProcessed),
                        StatusMessage = "Migration completed successfully"
                    };
                }

                // Calculate progress for current phase
                var progress = CalculatePhaseProgress(currentPhaseCheckpoint);

                _logger.LogDebug("GetStatusAsync: Phase={Phase}, Status={Status}, TotalItems={TotalItems}, TotalProcessed={TotalProcessed}, Progress={Progress}%",
                    currentPhaseCheckpoint.Phase, currentPhaseCheckpoint.Status,
                    currentPhaseCheckpoint.TotalItems, currentPhaseCheckpoint.TotalProcessed, progress);

                return new MigrationPipelineStatus
                {
                    CurrentPhase = currentPhaseCheckpoint.Phase,
                    CurrentPhaseStatus = currentPhaseCheckpoint.Status,
                    CurrentPhaseProgress = progress,
                    StartedAt = currentPhaseCheckpoint.StartedAt,
                    ElapsedTime = currentPhaseCheckpoint.StartedAt.HasValue
                        ? DateTime.UtcNow - currentPhaseCheckpoint.StartedAt.Value
                        : null,
                    TotalProcessed = relevantCheckpoints.Sum(c => c.TotalProcessed),
                    ErrorMessage = currentPhaseCheckpoint.ErrorMessage,
                    StatusMessage = GetPhaseStatusMessage(currentPhaseCheckpoint.Phase, currentPhaseCheckpoint.Status)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting migration status");
                throw;
            }
        }

        public async Task ResetAsync(CancellationToken ct = default)
        {
            try
            {
                _logger.LogWarning("⚠️  Resetting all migration phases to NotStarted...");

                await ExecuteCheckpointOperationAsync(async (conn, tran) =>
                {
                    var sql = @"UPDATE PhaseCheckpoints
                                SET Status = @status, StartedAt = NULL, CompletedAt = NULL,
                                    ErrorMessage = NULL, LastProcessedIndex = NULL, LastProcessedId = NULL,
                                    TotalProcessed = 0, UpdatedAt = @updatedAt";
                    var cmd = new CommandDefinition(sql, new
                    {
                        status = (int)PhaseStatus.NotStarted,
                        updatedAt = DateTime.UtcNow
                    }, transaction: tran, cancellationToken: ct);
                    await conn.ExecuteAsync(cmd);
                }, ct);

                _logger.LogInformation("✅ All phases reset to NotStarted");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting migration");
                throw;
            }
        }

        public async Task ResetPhaseAsync(MigrationPhase phase, CancellationToken ct = default)
        {
            try
            {
                _logger.LogWarning("⚠️  Resetting phase {Phase} to NotStarted...", phase);

                await ExecuteCheckpointOperationAsync(async (conn, tran) =>
                {
                    var sql = @"UPDATE PhaseCheckpoints
                                SET Status = @status, StartedAt = NULL, CompletedAt = NULL,
                                    ErrorMessage = NULL, LastProcessedIndex = NULL, LastProcessedId = NULL,
                                    TotalProcessed = 0, UpdatedAt = @updatedAt
                                WHERE Phase = @phase";
                    var cmd = new CommandDefinition(sql, new
                    {
                        phase = (int)phase,
                        status = (int)PhaseStatus.NotStarted,
                        updatedAt = DateTime.UtcNow
                    }, transaction: tran, cancellationToken: ct);
                    await conn.ExecuteAsync(cmd);
                }, ct);

                _logger.LogInformation("✅ Phase {Phase} reset to NotStarted", phase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting phase {Phase}", phase);
                throw;
            }
        }

        // ====================================================================
        // PRIVATE HELPER METHODS
        // ====================================================================

        private async Task ExecutePhaseAsync(
            MigrationPhase phase,
            string phaseDisplayName,
            Func<CancellationToken, Task> phaseAction,
            CancellationToken ct)
        {
            PhaseCheckpoint? checkpoint = null;

            try
            {
                // Check if phase is already completed
                // Use a dedicated SQL connection (NOT shared UnitOfWork) for checkpoint operations
                checkpoint = await ExecuteCheckpointOperationAsync(async (conn, tran) =>
                {
                    var sql = "SELECT * FROM PhaseCheckpoints WHERE Phase = @phase";
                    var cmd = new CommandDefinition(sql, new { phase = (int)phase }, transaction: tran, cancellationToken: ct);
                    return await conn.QueryFirstOrDefaultAsync<PhaseCheckpoint>(cmd);
                }, ct);

                if (checkpoint?.Status == PhaseStatus.Completed)
                {
                    _logger.LogInformation("⏭️  {PhaseDisplayName} already completed, skipping", phaseDisplayName);
                    return;
                }

                // Mark phase as started
                _logger.LogInformation("{PhaseDisplayName} starting...", phaseDisplayName);

                await ExecuteCheckpointOperationAsync(async (conn, tran) =>
                {
                    var sql = @"UPDATE PhaseCheckpoints
                                SET Status = @status, StartedAt = @startedAt, CompletedAt = NULL,
                                    ErrorMessage = NULL, UpdatedAt = @updatedAt
                                WHERE Phase = @phase";
                    var cmd = new CommandDefinition(sql, new
                    {
                        phase = (int)phase,
                        status = (int)PhaseStatus.InProgress,
                        startedAt = DateTime.UtcNow,
                        updatedAt = DateTime.UtcNow
                    }, transaction: tran, cancellationToken: ct);
                    var rowsAffected = await conn.ExecuteAsync(cmd);

                    _logger.LogInformation("Phase {Phase} ({PhaseDisplayName}) marked as InProgress - {RowsAffected} rows updated",
                        (int)phase, phaseDisplayName, rowsAffected);

                    if (rowsAffected == 0)
                    {
                        _logger.LogWarning("WARNING: Failed to update PhaseCheckpoints - no rows affected for Phase {Phase}!", (int)phase);
                    }
                }, ct);

                // Execute the phase (calls service's RunLoopAsync, PrepareAllFoldersAsync, etc.)
                // Phase services will manage their own transactions
                await phaseAction(ct);

                // Mark phase as completed
                await ExecuteCheckpointOperationAsync(async (conn, tran) =>
                {
                    var sql = @"UPDATE PhaseCheckpoints
                                SET Status = @status, CompletedAt = @completedAt, UpdatedAt = @updatedAt
                                WHERE Phase = @phase";
                    var cmd = new CommandDefinition(sql, new
                    {
                        phase = (int)phase,
                        status = (int)PhaseStatus.Completed,
                        completedAt = DateTime.UtcNow,
                        updatedAt = DateTime.UtcNow
                    }, transaction: tran, cancellationToken: ct);
                    await conn.ExecuteAsync(cmd);
                }, ct);

                _logger.LogInformation("✅ {PhaseDisplayName} completed", phaseDisplayName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ {PhaseDisplayName} failed", phaseDisplayName);

                // Mark phase as failed
                try
                {
                    await ExecuteCheckpointOperationAsync(async (conn, tran) =>
                    {
                        var sql = @"UPDATE PhaseCheckpoints
                                    SET Status = @status, ErrorMessage = @errorMessage, UpdatedAt = @updatedAt
                                    WHERE Phase = @phase";
                        var errorMsg = ex.Message?.Length > 4000 ? ex.Message.Substring(0, 4000) : ex.Message;
                        var cmd = new CommandDefinition(sql, new
                        {
                            phase = (int)phase,
                            status = (int)PhaseStatus.Failed,
                            errorMessage = errorMsg,
                            updatedAt = DateTime.UtcNow
                        }, transaction: tran, cancellationToken: ct);
                        await conn.ExecuteAsync(cmd);
                    }, ct);
                }
                catch (Exception checkpointEx)
                {
                    _logger.LogError(checkpointEx, "Error marking phase as failed");
                }

                // Fail-fast: re-throw to stop execution
                throw;
            }
        }

        /// <summary>
        /// Executes a checkpoint operation using a DEDICATED SQL connection.
        /// This completely isolates checkpoint operations from phase service transactions,
        /// preventing MultipleActiveResultSets errors.
        ///
        /// CRITICAL: Phase services use the shared IUnitOfWork (Scoped) for their transactions.
        /// MigrationWorker checkpoint operations create their OWN independent SQL connections
        /// to avoid any conflicts.
        /// </summary>
        private async Task<T> ExecuteCheckpointOperationAsync<T>(Func<SqlConnection, SqlTransaction, Task<T>> operation, CancellationToken ct)
        {
            // Create a completely independent SQL connection for checkpoint operations
            // This connection is NOT shared with phase services
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var transaction = connection.BeginTransaction();

            try
            {
                // Execute the operation with our dedicated connection AND transaction
                var result = await operation(connection, transaction);

                // Commit immediately
                transaction.Commit();

                return result;
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // Ignore rollback errors
                }
                throw;
            }
            // Connection and transaction will be automatically disposed here
        }

        /// <summary>
        /// Overload for operations that don't return a value
        /// </summary>
        private async Task ExecuteCheckpointOperationAsync(Func<SqlConnection, SqlTransaction, Task> operation, CancellationToken ct)
        {
            await ExecuteCheckpointOperationAsync(async (conn, tran) =>
            {
                await operation(conn, tran);
                return 0; // Dummy return value
            }, ct);
        }

        private int CalculatePhaseProgress(PhaseCheckpoint checkpoint)
        {
            if (checkpoint.TotalItems.HasValue && checkpoint.TotalItems.Value > 0)
            {
                return (int)((checkpoint.TotalProcessed / (double)checkpoint.TotalItems.Value) * 100);
            }

            // If TotalItems is unknown, show progress based on TotalProcessed activity
            // This provides feedback that work is being done even when we don't know total count
            if (checkpoint.Status == PhaseStatus.InProgress && checkpoint.TotalProcessed > 0)
            {
                // Show incremental progress based on processed items
                // Cap at 95% to indicate it's still in progress
                var estimatedProgress = Math.Min(95, 10 + (checkpoint.TotalProcessed / 100));
                return (int)estimatedProgress;
            }

            // If TotalItems is unknown and no items processed yet, return fixed values
            return checkpoint.Status switch
            {
                PhaseStatus.NotStarted => 0,
                PhaseStatus.InProgress => 10, // Show some progress to indicate it started
                PhaseStatus.Completed => 100,
                PhaseStatus.Failed => 0,
                _ => 0
            };
        }

        /// <summary>
        /// Validates DocTypes for MigrationByDocument mode and resets checkpoint if they changed
        /// </summary>
        private async Task ValidateAndResetDocTypesCheckpointAsync(CancellationToken ct)
        {
            try
            {
                // Get current DocTypes from DocumentSearchService (includes UI overrides)
                var currentDocTypes = _documentSearch?.GetCurrentDocTypes();
                if (currentDocTypes == null || !currentDocTypes.Any())
                {
                    _logger.LogWarning("No DocTypes configured for MigrationByDocument mode");
                    return;
                }

                var currentDocTypesStr = string.Join(",", currentDocTypes.OrderBy(dt => dt));

                // Get checkpoint for FolderDiscovery phase (reused for DocumentSearch)
                var checkpoint = await ExecuteCheckpointOperationAsync(async (conn, tran) =>
                {
                    var sql = "SELECT * FROM PhaseCheckpoints WHERE Phase = @phase";
                    var cmd = new CommandDefinition(sql, new { phase = (int)MigrationPhase.FolderDiscovery }, transaction: tran, cancellationToken: ct);
                    return await conn.QueryFirstOrDefaultAsync<PhaseCheckpoint>(cmd);
                }, ct);

                if (checkpoint == null)
                {
                    _logger.LogInformation("No checkpoint found for DocumentSearch phase, will create on first run");
                    return;
                }

                // Compare DocTypes
                if (string.IsNullOrEmpty(checkpoint.DocTypes))
                {
                    // First run with DocTypes - save them
                    _logger.LogInformation("Saving DocTypes to checkpoint: {DocTypes}", currentDocTypesStr);
                    await ExecuteCheckpointOperationAsync(async (conn, tran) =>
                    {
                        var sql = @"UPDATE PhaseCheckpoints
                                    SET DocTypes = @docTypes, UpdatedAt = @updatedAt
                                    WHERE Phase = @phase";
                        var cmd = new CommandDefinition(sql, new
                        {
                            phase = (int)MigrationPhase.FolderDiscovery,
                            docTypes = currentDocTypesStr,
                            updatedAt = DateTime.UtcNow
                        }, transaction: tran, cancellationToken: ct);
                        await conn.ExecuteAsync(cmd);
                    }, ct);
                }
                else
                {
                    // Normalize both strings for comparison (sort and compare)
                    var storedDocTypesNormalized = string.Join(",", checkpoint.DocTypes.Split(',').Select(dt => dt.Trim()).OrderBy(dt => dt));

                    if (storedDocTypesNormalized != currentDocTypesStr)
                    {
                        _logger.LogWarning(
                            "DocTypes changed! Old: [{OldDocTypes}], New: [{NewDocTypes}]. Resetting checkpoint...",
                            checkpoint.DocTypes, currentDocTypesStr);

                        // Reset the checkpoint and update DocTypes
                        await ExecuteCheckpointOperationAsync(async (conn, tran) =>
                        {
                            var sql = @"UPDATE PhaseCheckpoints
                                        SET Status = @status,
                                            StartedAt = NULL,
                                            CompletedAt = NULL,
                                            ErrorMessage = NULL,
                                            LastProcessedIndex = NULL,
                                            LastProcessedId = NULL,
                                            TotalProcessed = 0,
                                            DocTypes = @docTypes,
                                            UpdatedAt = @updatedAt
                                        WHERE Phase = @phase";
                            var cmd = new CommandDefinition(sql, new
                            {
                                phase = (int)MigrationPhase.FolderDiscovery,
                                status = (int)PhaseStatus.NotStarted,
                                docTypes = currentDocTypesStr,
                                updatedAt = DateTime.UtcNow
                            }, transaction: tran, cancellationToken: ct);
                            await conn.ExecuteAsync(cmd);
                        }, ct);

                        // Also reset subsequent phases (FolderPreparation, Move)
                        _logger.LogInformation("Resetting subsequent phases (FolderPreparation, Move)...");
                        await ExecuteCheckpointOperationAsync(async (conn, tran) =>
                        {
                            var sql = @"UPDATE PhaseCheckpoints
                                        SET Status = @status,
                                            StartedAt = NULL,
                                            CompletedAt = NULL,
                                            ErrorMessage = NULL,
                                            LastProcessedIndex = NULL,
                                            LastProcessedId = NULL,
                                            TotalProcessed = 0,
                                            UpdatedAt = @updatedAt
                                        WHERE Phase IN (@folderPrep, @move)";
                            var cmd = new CommandDefinition(sql, new
                            {
                                folderPrep = (int)MigrationPhase.FolderPreparation,
                                move = (int)MigrationPhase.Move,
                                status = (int)PhaseStatus.NotStarted,
                                updatedAt = DateTime.UtcNow
                            }, transaction: tran, cancellationToken: ct);
                            await conn.ExecuteAsync(cmd);
                        }, ct);

                        _logger.LogInformation("✅ Checkpoint reset due to DocTypes change");
                    }
                    else
                    {
                        _logger.LogInformation("DocTypes unchanged, continuing from checkpoint: {DocTypes}", currentDocTypesStr);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating DocTypes checkpoint");
                throw;
            }
        }

        private string GetPhaseStatusMessage(MigrationPhase phase, PhaseStatus status)
        {
            var phaseName = phase switch
            {
                // In MigrationByDocument mode, FolderDiscovery phase is actually DocumentSearch
                MigrationPhase.FolderDiscovery => _migrationOptions.MigrationByDocument ? "Document Search" : "Folder Discovery",
                MigrationPhase.DocumentDiscovery => "Document Discovery",
                MigrationPhase.FolderPreparation => "Folder Preparation",
                MigrationPhase.Move => "Document Move",
                _ => "Unknown Phase"
            };

            var statusText = status switch
            {
                PhaseStatus.NotStarted => "not started",
                PhaseStatus.InProgress => "in progress",
                PhaseStatus.Completed => "completed",
                PhaseStatus.Failed => "failed",
                _ => "unknown status"
            };

            return $"{phaseName} is {statusText}";
        }
    }
}
