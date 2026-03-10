using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
using Migration.Extensions.SqlServer;
using SqlServer.Abstraction.Interfaces;

namespace Migration.Infrastructure.Implementation.Services
{
    /// <summary>
    /// Service for preparing the database before migration starts.
    /// Resets incomplete items in DocStaging and FolderStaging tables to their safe restart state.
    /// This enables resume from the point of interruption instead of starting from scratch.
    /// </summary>
    public class MigrationPreparationService : IMigrationPreparationService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _fileLogger;
        private readonly ILogger _dbLogger;
        private readonly ILogger _uiLogger;

        public MigrationPreparationService(
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _fileLogger = loggerFactory.CreateLogger("FileLogger");
            _dbLogger = loggerFactory.CreateLogger("DbLogger");
            _uiLogger = loggerFactory.CreateLogger("UiLogger");
        }

        /// <summary>
        /// Prepares database for migration by resetting incomplete items to their safe restart state.
        /// - PREPARATION docs → READY (folder prep was interrupted)
        /// - IN_PROGRESS docs → PREPARED (move was interrupted)
        /// - IN_PROGRESS folders → READY (folder creation was interrupted)
        /// Should be called ONCE before starting migration workflow.
        /// </summary>
        public async Task<MigrationPreparationResult> PrepareForMigrationAsync(CancellationToken ct = default)
        {
            _fileLogger.LogInformation("Starting database preparation - resetting incomplete items for resume");
            _dbLogger.LogInformation("Starting database preparation");
            _uiLogger.LogInformation("Preparing database for migration...");

            var result = new MigrationPreparationResult { Success = false };

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();
                var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);

                try
                {
                    // 1. Count resettable items BEFORE reset (for logging)
                    _fileLogger.LogInformation("Counting resettable items...");

                    var resettableDocsCount = await docRepo.CountResettableDocumentsAsync(
                        uow.Connection,
                        uow.Transaction,
                        ct).ConfigureAwait(false);

                    var resettableFoldersCount = await folderRepo.CountResettableFoldersAsync(
                        uow.Connection,
                        uow.Transaction,
                        ct).ConfigureAwait(false);

                    _fileLogger.LogInformation(
                        "Found {DocCount} resettable documents and {FolderCount} resettable folders",
                        resettableDocsCount, resettableFoldersCount);

                    // 2. Reset incomplete documents (PREPARATION→READY, IN_PROGRESS→PREPARED)
                    _fileLogger.LogInformation("Resetting incomplete documents in DocStaging...");

                    var resetDocs = await docRepo.ResetIncompleteDocumentsAsync(
                        uow.Connection,
                        uow.Transaction,
                        ct).ConfigureAwait(false);

                    _fileLogger.LogInformation("Reset {Count} incomplete documents", resetDocs);

                    // 3. Reset incomplete folders (IN_PROGRESS→READY)
                    _fileLogger.LogInformation("Resetting incomplete folders in FolderStaging...");

                    var resetFolders = await folderRepo.ResetIncompleteFoldersAsync(
                        uow.Connection,
                        uow.Transaction,
                        ct).ConfigureAwait(false);

                    _fileLogger.LogInformation("Reset {Count} incomplete folders", resetFolders);

                    // 4. Commit transaction
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    // 5. Prepare result
                    result.ResetDocuments = resetDocs;
                    result.ResetFolders = resetFolders;
                    result.Success = true;

                    _fileLogger.LogInformation(
                        "Database preparation completed successfully - " +
                        "Reset {TotalDocs} documents and {TotalFolders} folders (Total: {Total})",
                        resetDocs, resetFolders, resetDocs + resetFolders);

                    _dbLogger.LogInformation(
                        "Database preparation completed - reset {Total} items",
                        resetDocs + resetFolders);

                    _uiLogger.LogInformation(
                        "Database prepared: {Total} incomplete items reset for resume",
                        resetDocs + resetFolders);

                    if (resetDocs + resetFolders == 0)
                    {
                        _fileLogger.LogInformation(
                            "No incomplete items found - database is already in clean state");
                        _uiLogger.LogInformation("Database is already clean - ready to start migration");
                    }
                    else
                    {
                        _fileLogger.LogInformation(
                            "Incomplete items reset to safe state - migration will resume from checkpoint");
                        _uiLogger.LogInformation("Ready to resume migration from last checkpoint");
                    }
                }
                catch (Exception ex)
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);

                    _fileLogger.LogError(ex,
                        "Database preparation failed - transaction rolled back. " +
                        "ErrorType: {ErrorType}, Message: {Message}",
                        ex.GetType().Name, ex.Message);

                    _dbLogger.LogError(ex, "Database preparation failed");
                    _uiLogger.LogError("Failed to prepare database: {Error}", ex.Message);

                    result.Success = false;
                    result.ErrorMessage = ex.Message;

                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex,
                    "Fatal error during database preparation: {ErrorType} - {Message}",
                    ex.GetType().Name, ex.Message);

                result.Success = false;
                result.ErrorMessage = ex.Message;

                throw;
            }

            return result;
        }
    }
}
