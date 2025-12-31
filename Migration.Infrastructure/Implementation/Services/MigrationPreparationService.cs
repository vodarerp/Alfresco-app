using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
using Migration.Extensions.SqlServer;
using SqlServer.Abstraction.Interfaces;

namespace Migration.Infrastructure.Implementation.Services
{
    /// <summary>
    /// Service for preparing the database before migration starts.
    /// Deletes all incomplete items from DocStaging and FolderStaging tables.
    /// This ensures a clean start and prevents stuck items from previous runs.
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
        /// Prepares database for migration by deleting all incomplete items.
        /// Should be called ONCE before starting migration workflow.
        /// </summary>
        public async Task<MigrationPreparationResult> PrepareForMigrationAsync(CancellationToken ct = default)
        {
            _fileLogger.LogInformation("🗑️ Starting database preparation - deleting incomplete items");
            _dbLogger.LogInformation("Starting database preparation");
            _uiLogger.LogInformation("Preparing database for migration...");

            var result = new MigrationPreparationResult { Success = false };

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();
                var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

                _fileLogger.LogDebug("Attempting to begin database transaction for preparation...");

                try
                {
                    await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException ocEx)
                {
                    _fileLogger.LogError(ocEx,
                        "❌ Database transaction was canceled during preparation. " +
                        "This typically indicates: (1) Application is shutting down, (2) Network connectivity issues, " +
                        "(3) SQL Server is not accessible, or (4) Connection timeout. Message: {Message}",
                        ocEx.Message);

                    _uiLogger.LogError(
                        "Povezivanje sa bazom otkazano. Proverite da li je SQL Server dostupan i da li je mrežna veza aktivna.");

                    throw;
                }
                catch (InvalidOperationException ioEx)
                {
                    _fileLogger.LogError(ioEx,
                        "❌ Failed to connect to database during preparation. Message: {Message}",
                        ioEx.Message);

                    _uiLogger.LogError(
                        "Greška pri povezivanju na bazu podataka. Proverite konfiguraciju SQL Servera.");

                    throw;
                }

                try
                {
                    // 1️⃣ Count incomplete items BEFORE deletion (for logging)
                    _fileLogger.LogInformation("Counting incomplete items...");

                    var incompleteDocsCount = await docRepo.CountIncompleteDocumentsAsync(
                        uow.Connection,
                        uow.Transaction,
                        ct).ConfigureAwait(false);

                    var incompleteFoldersCount = await folderRepo.CountIncompleteFoldersAsync(
                        uow.Connection,
                        uow.Transaction,
                        ct).ConfigureAwait(false);

                    _fileLogger.LogInformation(
                        "Found {DocCount} incomplete documents and {FolderCount} incomplete folders",
                        incompleteDocsCount, incompleteFoldersCount);

                    // 2️⃣ Delete incomplete documents
                    _fileLogger.LogInformation("Deleting incomplete documents from DocStaging...");

                    var deletedDocs = await docRepo.DeleteIncompleteDocumentsAsync(
                        uow.Connection,
                        uow.Transaction,
                        ct).ConfigureAwait(false);

                    _fileLogger.LogInformation("✅ Deleted {Count} incomplete documents", deletedDocs);

                    // 3️⃣ Delete incomplete folders
                    _fileLogger.LogInformation("Deleting incomplete folders from FolderStaging...");

                    var deletedFolders = await folderRepo.DeleteIncompleteFoldersAsync(
                        uow.Connection,
                        uow.Transaction,
                        ct).ConfigureAwait(false);

                    _fileLogger.LogInformation("✅ Deleted {Count} incomplete folders", deletedFolders);

                    // 4️⃣ Commit transaction
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    // 5️⃣ Prepare result
                    result.DeletedDocuments = deletedDocs;
                    result.DeletedFolders = deletedFolders;
                    result.Success = true;

                    _fileLogger.LogInformation(
                        "✅ Database preparation completed successfully - " +
                        "Deleted {TotalDocs} documents and {TotalFolders} folders (Total: {Total})",
                        deletedDocs, deletedFolders, deletedDocs + deletedFolders);

                    _dbLogger.LogInformation(
                        "Database preparation completed - deleted {Total} items",
                        deletedDocs + deletedFolders);

                    _uiLogger.LogInformation(
                        "Database prepared: {Total} incomplete items removed",
                        deletedDocs + deletedFolders);

                    if (deletedDocs + deletedFolders == 0)
                    {
                        _fileLogger.LogInformation(
                            "ℹ️ No incomplete items found - database is already clean");
                        _uiLogger.LogInformation("Database is already clean - ready to start migration");
                    }
                    else
                    {
                        _fileLogger.LogInformation(
                            "ℹ️ Migration will start fresh - DocumentSearchService will repopulate staging tables");
                        _uiLogger.LogInformation("Ready to start migration from clean state");
                    }
                }
                catch (Exception ex)
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);

                    _fileLogger.LogError(ex,
                        "❌ Database preparation failed - transaction rolled back. " +
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
                    "❌ Fatal error during database preparation: {ErrorType} - {Message}",
                    ex.GetType().Name, ex.Message);

                result.Success = false;
                result.ErrorMessage = ex.Message;

                throw;
            }

            return result;
        }
    }
}
