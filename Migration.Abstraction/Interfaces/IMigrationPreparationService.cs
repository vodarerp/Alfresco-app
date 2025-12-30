namespace Migration.Abstraction.Interfaces
{
    /// <summary>
    /// Service for preparing the database before migration starts.
    /// Handles cleanup of incomplete items from previous runs.
    /// </summary>
    public interface IMigrationPreparationService
    {
        /// <summary>
        /// Prepares database for migration by deleting all incomplete items.
        /// This ensures a clean start and prevents stuck items from previous runs.
        /// Should be called ONCE before starting migration workflow.
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Statistics about deleted items</returns>
        Task<MigrationPreparationResult> PrepareForMigrationAsync(CancellationToken ct = default);
    }
}
