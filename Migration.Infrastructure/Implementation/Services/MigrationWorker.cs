using Alfresco.Contracts.Enums;
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
    /// </summary>
    public class MigrationWorker : IMigrationWorker
    {
        private readonly IFolderDiscoveryService _folderDiscovery;
        private readonly IDocumentDiscoveryService _documentDiscovery;
        private readonly IFolderPreparationService _folderPreparation;
        private readonly IMoveService _moveService;
        private readonly IPhaseCheckpointRepository _phaseCheckpointRepo;
        private readonly ILogger<MigrationWorker> _logger;
        private readonly IUnitOfWork _uow;
        private readonly string _connectionString;

        public MigrationWorker(
            IFolderDiscoveryService folderDiscovery,
            IDocumentDiscoveryService documentDiscovery,
            IFolderPreparationService folderPreparation,
            IMoveService moveService,
            IPhaseCheckpointRepository phaseCheckpointRepo,
            ILogger<MigrationWorker> logger,
            IUnitOfWork uow,
            IOptions<global::Alfresco.Contracts.SqlServer.SqlServerOptions> sqlOptions)
        {
            _folderDiscovery = folderDiscovery ?? throw new ArgumentNullException(nameof(folderDiscovery));
            _documentDiscovery = documentDiscovery ?? throw new ArgumentNullException(nameof(documentDiscovery));
            _folderPreparation = folderPreparation ?? throw new ArgumentNullException(nameof(folderPreparation));
            _moveService = moveService ?? throw new ArgumentNullException(nameof(moveService));
            _phaseCheckpointRepo = phaseCheckpointRepo ?? throw new ArgumentNullException(nameof(phaseCheckpointRepo));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _connectionString = sqlOptions?.Value?.ConnectionString ?? throw new ArgumentNullException(nameof(sqlOptions));
        }

        public async Task RunAsync(CancellationToken ct = default)
        {
            try
            {
                _logger.LogInformation("🚀 Migration pipeline started");

                // ====================================================================
                // FAZA 1: FolderDiscovery
                // ====================================================================
                await ExecutePhaseAsync(
                    MigrationPhase.FolderDiscovery,
                    "📂 FAZA 1: FolderDiscovery",
                    async (token) => await _folderDiscovery.RunLoopAsync(token),
                    ct);

                // ====================================================================
                // FAZA 2: DocumentDiscovery
                // ====================================================================
                await ExecutePhaseAsync(
                    MigrationPhase.DocumentDiscovery,
                    "📄 FAZA 2: DocumentDiscovery",
                    async (token) => await _documentDiscovery.RunLoopAsync(token),
                    ct);

                // ====================================================================
                // FAZA 3: FolderPreparation (NOVA FAZA!)
                // ====================================================================
                await ExecutePhaseAsync(
                    MigrationPhase.FolderPreparation,
                    "🏗️  FAZA 3: FolderPreparation (parallel folder creation)",
                    async (token) => await _folderPreparation.PrepareAllFoldersAsync(token),
                    ct);

                // ====================================================================
                // FAZA 4: Move
                // ====================================================================
                await ExecutePhaseAsync(
                    MigrationPhase.Move,
                    "🚚 FAZA 4: Move (parallel document moves)",
                    async (token) => await _moveService.RunLoopAsync(token),
                    ct);

                _logger.LogInformation("🎉 Migration pipeline completed successfully!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Migration pipeline failed");
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
                var currentPhaseCheckpoint = checkpoints
                    .OrderBy(c => c.Phase)
                    .FirstOrDefault(c => c.Status != PhaseStatus.Completed);

                if (currentPhaseCheckpoint == null)
                {
                    // All phases completed
                    var lastPhase = checkpoints.OrderByDescending(c => c.Phase).First();
                    return new MigrationPipelineStatus
                    {
                        CurrentPhase = lastPhase.Phase,
                        CurrentPhaseStatus = PhaseStatus.Completed,
                        CurrentPhaseProgress = 100,
                        StartedAt = checkpoints.Min(c => c.StartedAt),
                        ElapsedTime = lastPhase.CompletedAt - checkpoints.Min(c => c.StartedAt),
                        TotalProcessed = checkpoints.Sum(c => c.TotalProcessed),
                        StatusMessage = "Migration completed successfully"
                    };
                }

                // Calculate progress for current phase
                var progress = CalculatePhaseProgress(currentPhaseCheckpoint);

                return new MigrationPipelineStatus
                {
                    CurrentPhase = currentPhaseCheckpoint.Phase,
                    CurrentPhaseStatus = currentPhaseCheckpoint.Status,
                    CurrentPhaseProgress = progress,
                    StartedAt = currentPhaseCheckpoint.StartedAt,
                    ElapsedTime = currentPhaseCheckpoint.StartedAt.HasValue
                        ? DateTime.UtcNow - currentPhaseCheckpoint.StartedAt.Value
                        : null,
                    TotalProcessed = checkpoints.Sum(c => c.TotalProcessed),
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
                    await conn.ExecuteAsync(cmd);
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

            // If TotalItems is unknown, return 0 if NotStarted, 50 if InProgress, 100 if Completed
            return checkpoint.Status switch
            {
                PhaseStatus.NotStarted => 0,
                PhaseStatus.InProgress => 50,
                PhaseStatus.Completed => 100,
                PhaseStatus.Failed => 0,
                _ => 0
            };
        }

        private string GetPhaseStatusMessage(MigrationPhase phase, PhaseStatus status)
        {
            var phaseName = phase switch
            {
                MigrationPhase.FolderDiscovery => "Folder Discovery",
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
