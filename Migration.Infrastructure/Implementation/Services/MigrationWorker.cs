using Alfresco.Abstraction.Models;
using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces.Services;
using Migration.Abstraction.Interfaces.Wrappers;
using SqlServer.Abstraction.Interfaces;
using System;
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
        private readonly ILogger _uiLogger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly GlobalErrorTracker _errorTracker;

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
            IServiceScopeFactory scopeFactory,
            IOptions<MigrationOptions> migrationOptions,
            GlobalErrorTracker errorTracker,
            IDocumentSearchService? documentSearch = null)
        {
            _folderDiscovery = folderDiscovery ?? throw new ArgumentNullException(nameof(folderDiscovery));
            _documentDiscovery = documentDiscovery ?? throw new ArgumentNullException(nameof(documentDiscovery));
            _documentSearch = documentSearch; // Optional - only used in MigrationByDocument mode
            _folderPreparation = folderPreparation ?? throw new ArgumentNullException(nameof(folderPreparation));
            _moveService = moveService ?? throw new ArgumentNullException(nameof(moveService));
            _phaseCheckpointRepo = phaseCheckpointRepo ?? throw new ArgumentNullException(nameof(phaseCheckpointRepo));
            _logger = logger.CreateLogger("FileLogger");
            _uiLogger = logger.CreateLogger("UiLogger");
            _connectionString = sqlOptions?.Value?.ConnectionString ?? throw new ArgumentNullException(nameof(sqlOptions));
            _migrationOptions = migrationOptions?.Value ?? throw new ArgumentNullException(nameof(migrationOptions));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _errorTracker = errorTracker ?? throw new ArgumentNullException(nameof(errorTracker));
        }

        public async Task RunAsync(CancellationToken ct = default)
        {
            try
            {
                // Reset error tracker at the start of migration
                _errorTracker.Reset();
                _logger.LogInformation("Migration pipeline started - Error tracker reset");
                _uiLogger.LogInformation("Migracija pokrenuta");

                // Determine migration mode
                if (_migrationOptions.MigrationByDocument)
                {
                    _logger.LogInformation("Migration mode: MigrationByDocument (DocumentSearch -> FolderPreparation -> Move)");

                    // Validate that DocumentSearchService is available
                    if (_documentSearch == null)
                    {
                        _uiLogger.LogError("DocumentSearchService not configured");
                        throw new InvalidOperationException(
                            "MigrationByDocument is enabled but IDocumentSearchService is not registered. " +
                            "Please register DocumentSearchService in DI container.");
                    }

                    // Check if DocDescriptions have changed and reset checkpoint if needed
                    await ValidateAndResetDocDescriptionsCheckpointAsync(ct);

                    // ====================================================================
                    // FAZA 1: DocumentSearch (searches by ecm:docDesc, populates both tables)
                    // ====================================================================
                    await ExecutePhaseAsync(
                        MigrationPhase.FolderDiscovery,
                        "FAZA 1: DocumentSearch (by ecm:docDesc)",
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
                _uiLogger.LogInformation("✅ Migracija uspešno završena!");

                // Log final error metrics -- NIKOLA
                var metrics = _errorTracker.GetMetrics();
                _logger.LogInformation(
                    "📊 Migration Error Summary: Timeouts: {TimeoutCount}, Retry Failures: {RetryFailureCount}, Total: {TotalErrors}",
                    metrics.TimeoutCount, metrics.RetryExhaustedCount, metrics.TotalErrorCount);
                _uiLogger.LogInformation(
                    "📊 Greške tokom migracije: Timeout-i: {TimeoutCount}, Retry failures: {RetryFailureCount}",
                    metrics.TimeoutCount, metrics.RetryExhaustedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration pipeline failed");
                _uiLogger.LogError("❌ Migracija prekinuta - kritična greška: {Error}", ex.Message);

                // Log error metrics before stopping
                var metrics = _errorTracker.GetMetrics();
                _logger.LogError(
                    "📊 Error Summary at failure: Timeouts: {TimeoutCount}/{MaxTimeouts}, Retry Failures: {RetryFailureCount}/{MaxRetryFailures}, Total: {TotalErrors}/{MaxTotalErrors}",
                    metrics.TimeoutCount, metrics.MaxTimeouts,
                    metrics.RetryExhaustedCount, metrics.MaxRetryFailures,
                    metrics.TotalErrorCount, metrics.MaxTotalErrors);

                throw;
            }
        }

        public async Task<MigrationPipelineStatus> GetStatusAsync(CancellationToken ct = default)
        {
            try
            {
                // Use dedicated connection for status reads (no conflict with phase services)
                var checkpoints = await ExecuteCheckpointOperationAsync(async (uow) =>
                {
                    var sql = "SELECT * FROM PhaseCheckpoints ORDER BY Phase ASC";
                    var cmd = new CommandDefinition(sql, transaction: uow.Transaction, cancellationToken: ct);
                    var results = await uow.Connection.QueryAsync<PhaseCheckpoint>(cmd); //((IDbConnection)uow.Connection)
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
                _uiLogger.LogError("Cannot get migration status");
                throw;
            }
        }

        public async Task ResetAsync(CancellationToken ct = default)
        {
            try
            {
                _logger.LogWarning("⚠️  Resetting all migration phases to NotStarted...");

                await ExecuteCheckpointOperationAsync(async (uow) =>
                {
                    var sql = @"UPDATE PhaseCheckpoints
                                SET Status = @status, StartedAt = NULL, CompletedAt = NULL,
                                    ErrorMessage = NULL, LastProcessedIndex = NULL, LastProcessedId = NULL,
                                    TotalProcessed = 0, UpdatedAt = @updatedAt";
                    var cmd = new CommandDefinition(sql, new
                    {
                        status = (int)PhaseStatus.NotStarted,
                        updatedAt = DateTime.UtcNow
                    }, transaction: uow.Transaction, cancellationToken: ct);
                    await uow.Connection.ExecuteAsync(cmd);
                }, ct);

                _logger.LogInformation("✅ All phases reset to NotStarted");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting migration");
                _uiLogger.LogError("Cannot reset migration");
                throw;
            }
        }

        public async Task ResetPhaseAsync(MigrationPhase phase, CancellationToken ct = default)
        {
            try
            {
                _logger.LogWarning("⚠️  Resetting phase {Phase} to NotStarted...", phase);

                await ExecuteCheckpointOperationAsync(async (uow) =>
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
                    }, transaction: uow.Transaction, cancellationToken: ct);
                    await uow.Connection.ExecuteAsync(cmd);
                }, ct);

                _logger.LogInformation("✅ Phase {Phase} reset to NotStarted", phase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting phase {Phase}", phase);
                _uiLogger.LogError("Cannot reset phase {Phase}", phase);
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
                checkpoint = await ExecuteCheckpointOperationAsync(async (uow) =>
                {
                    var sql = "SELECT * FROM PhaseCheckpoints WHERE Phase = @phase";
                    var cmd = new CommandDefinition(sql, new { phase = (int)phase }, transaction: uow.Transaction, cancellationToken: ct);
                    return await uow.Connection.QueryFirstOrDefaultAsync<PhaseCheckpoint>(cmd);
                }, ct);

                if (checkpoint?.Status == PhaseStatus.Completed)
                {
                    _logger.LogInformation("⏭️  {PhaseDisplayName} already completed, skipping", phaseDisplayName);
                    return;
                }

                // Mark phase as started
                _logger.LogInformation("{PhaseDisplayName} starting...", phaseDisplayName);

                await ExecuteCheckpointOperationAsync(async (uow) =>
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
                    }, transaction: uow.Transaction, cancellationToken: ct);
                    var rowsAffected = await uow.Connection.ExecuteAsync(cmd);

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
                await ExecuteCheckpointOperationAsync(async (uow) =>
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
                    }, transaction: uow.Transaction, cancellationToken: ct);
                    await uow.Connection.ExecuteAsync(cmd);
                }, ct);

                _logger.LogInformation("✅ {PhaseDisplayName} completed", phaseDisplayName);
            }
            catch (AlfrescoTimeoutException timeoutEx)
            {
                // Record timeout in global error tracker
                _errorTracker.RecordTimeout(timeoutEx, $"Phase: {phaseDisplayName}");

                _logger.LogError(timeoutEx, "❌ {PhaseDisplayName} failed - TIMEOUT after {Timeout}s",
                    phaseDisplayName, timeoutEx.TimeoutDuration.TotalSeconds);
                _uiLogger.LogError("❌ {PhaseDisplayName} neuspešan - TIMEOUT ({Timeout}s)",
                    phaseDisplayName, timeoutEx.TimeoutDuration.TotalSeconds);

                // Mark phase as failed
                await MarkPhaseAsFailed(phase, timeoutEx.Message, ct);

                // Check if migration should stop
                if (_errorTracker.ShouldStopMigration)
                {
                    _logger.LogCritical("🛑 STOPPING MIGRATION: Error threshold exceeded!");
                    _uiLogger.LogCritical("🛑 MIGRACIJA ZAUSTAVLJENA: Prekoračen limit grešaka!");
                    throw;
                }

                // Re-throw to stop execution
                throw;
            }
            catch (AlfrescoRetryExhaustedException retryEx)
            {
                // Record retry exhausted in global error tracker
                _errorTracker.RecordRetryExhausted(retryEx, $"Phase: {phaseDisplayName}");

                _logger.LogError(retryEx, "❌ {PhaseDisplayName} failed - RETRY EXHAUSTED after {RetryCount} attempts",
                    phaseDisplayName, retryEx.RetryCount);
                _uiLogger.LogError("❌ {PhaseDisplayName} neuspešan - Svi retry pokušaji iskorišćeni ({RetryCount})",
                    phaseDisplayName, retryEx.RetryCount);

                // Mark phase as failed
                await MarkPhaseAsFailed(phase, retryEx.Message, ct);

                // Check if migration should stop
                if (_errorTracker.ShouldStopMigration)
                {
                    _logger.LogCritical("🛑 STOPPING MIGRATION: Error threshold exceeded!");
                    _uiLogger.LogCritical("🛑 MIGRACIJA ZAUSTAVLJENA: Prekoračen limit grešaka!");
                    throw;
                }

                // Re-throw to stop execution
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ {PhaseDisplayName} failed", phaseDisplayName);
                _uiLogger.LogError("❌ {PhaseDisplayName} neuspešan: {Error}", phaseDisplayName, ex.Message);

                // Mark phase as failed
                await MarkPhaseAsFailed(phase, ex.Message, ct);

                // Fail-fast: re-throw to stop execution
                throw;
            }
        }


        /// <summary>
        /// Helper method to mark phase as failed
        /// </summary>
        private async Task MarkPhaseAsFailed(MigrationPhase phase, string errorMessage, CancellationToken ct)
        {
            try
            { //NIKOLA
                await ExecuteCheckpointOperationAsync(async (uow) =>
                {
                    var sql = @"UPDATE PhaseCheckpoints
                                SET Status = @status, ErrorMessage = @errorMessage, UpdatedAt = @updatedAt
                                WHERE Phase = @phase";
                    var errorMsg = errorMessage?.Length > 4000 ? errorMessage.Substring(0, 4000) : errorMessage;
                    var cmd = new CommandDefinition(sql, new
                    {
                        phase = (int)phase,
                        status = (int)PhaseStatus.Failed,
                        errorMessage = errorMsg,
                        updatedAt = DateTime.UtcNow
                    }, transaction: uow.Transaction, cancellationToken: ct);
                    await uow.Connection.ExecuteAsync(cmd);
                }, ct);
            }
            catch (Exception checkpointEx)
            {
                _logger.LogError(checkpointEx, "Error marking phase as failed");
                _uiLogger.LogError("Cannot save phase error status");
            }
        }

        private async Task<T> ExecuteCheckpointOperationAsync<T>(Func<IUnitOfWork, Task<T>> operation, CancellationToken ct)
        {
            // Create a new scope with a dedicated UnitOfWork instance
            // This UnitOfWork is completely independent from phase services NIKOLA
            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);

            try
            {
                // Execute the operation with our dedicated UnitOfWork
                var result = await operation(uow);

                // Commit immediately
                await uow.CommitAsync(ct).ConfigureAwait(false);

                return result;
            }
            catch (Exception ex)
            {
                try
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore rollback errors
                }

                // Log to UI if this is a critical checkpoint operation failure
                if (ex is TaskCanceledException)
                {
                    _uiLogger.LogWarning("Operation cancelled");
                }
                else
                {
                    _uiLogger.LogError("Database checkpoint error: {Error}", ex.Message);
                }

                throw;
            }
            // UnitOfWork will be automatically disposed by scope disposal
        }

      
        
        private async Task ExecuteCheckpointOperationAsync(Func<IUnitOfWork, Task> operation, CancellationToken ct)
        {
            await ExecuteCheckpointOperationAsync(async (uow) =>
            {
                await operation(uow);
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
        
        private async Task ValidateAndResetDocDescriptionsCheckpointAsync(CancellationToken ct)
        {
            try
            {
                // Get current DocDescriptions from DocumentSearchService (includes UI overrides)
                var currentDocDescriptions = _documentSearch?.GetCurrentDocDescriptions();
                if (currentDocDescriptions == null || !currentDocDescriptions.Any())
                {
                    _logger.LogWarning("No DocDescriptions configured for MigrationByDocument mode");
                    return;
                }

                var currentDocDescriptionsStr = string.Join(",", currentDocDescriptions.OrderBy(dt => dt));

                // Get checkpoint for FolderDiscovery phase (reused for DocumentSearch)
                var checkpoint = await ExecuteCheckpointOperationAsync(async (uow) =>
                {
                    var sql = "SELECT * FROM PhaseCheckpoints WHERE Phase = @phase";
                    var cmd = new CommandDefinition(sql, new { phase = (int)MigrationPhase.FolderDiscovery }, transaction: uow.Transaction, cancellationToken: ct);
                    return await uow.Connection.QueryFirstOrDefaultAsync<PhaseCheckpoint>(cmd);
                }, ct);

                if (checkpoint == null)
                {
                    _logger.LogInformation("No checkpoint found for DocumentSearch phase, will create on first run");
                    return;
                }

                // Compare DocDescriptions (stored in DocTypes column for compatibility)
                if (string.IsNullOrEmpty(checkpoint.DocTypes))
                {
                    // First run with DocDescriptions - save them
                    _logger.LogInformation("Saving DocDescriptions to checkpoint: {DocDescriptions}", currentDocDescriptionsStr);
                    await ExecuteCheckpointOperationAsync(async (uow) =>
                    {
                        var sql = @"UPDATE PhaseCheckpoints
                                    SET DocTypes = @docTypes, UpdatedAt = @updatedAt
                                    WHERE Phase = @phase";
                        var cmd = new CommandDefinition(sql, new
                        {
                            phase = (int)MigrationPhase.FolderDiscovery,
                            docTypes = currentDocDescriptionsStr,
                            updatedAt = DateTime.UtcNow
                        }, transaction: uow.Transaction, cancellationToken: ct);
                        await uow.Connection.ExecuteAsync(cmd);
                    }, ct);
                }
                else
                {
                    // Normalize both strings for comparison (sort and compare)
                    var storedDocDescriptionsNormalized = string.Join(",", checkpoint.DocTypes.Split(',').Select(dt => dt.Trim()).OrderBy(dt => dt));

                    if (storedDocDescriptionsNormalized != currentDocDescriptionsStr)
                    {
                        _logger.LogWarning(
                            "DocDescriptions changed! Old: [{OldDocDescriptions}], New: [{NewDocDescriptions}]. Resetting checkpoint...",
                            checkpoint.DocTypes, currentDocDescriptionsStr);

                        // Reset the checkpoint and update DocDescriptions
                        await ExecuteCheckpointOperationAsync(async (uow) =>
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
                                docTypes = currentDocDescriptionsStr,
                                updatedAt = DateTime.UtcNow
                            }, transaction: uow.Transaction, cancellationToken: ct);
                            await uow.Connection.ExecuteAsync(cmd);
                        }, ct);

                        // Also reset subsequent phases (FolderPreparation, Move)
                        _logger.LogInformation("Resetting subsequent phases (FolderPreparation, Move)...");
                        await ExecuteCheckpointOperationAsync(async (uow) =>
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
                            }, transaction: uow.Transaction, cancellationToken: ct);
                            await uow.Connection.ExecuteAsync(cmd);
                        }, ct);

                        _logger.LogInformation("✅ Checkpoint reset due to DocDescriptions change");
                    }
                    else
                    {
                        _logger.LogInformation("DocDescriptions unchanged, continuing from checkpoint: {DocDescriptions}", currentDocDescriptionsStr);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating DocDescriptions checkpoint");
                _uiLogger.LogError("Cannot validate document descriptions");
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
