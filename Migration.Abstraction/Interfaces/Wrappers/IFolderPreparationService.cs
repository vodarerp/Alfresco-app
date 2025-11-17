namespace Migration.Abstraction.Interfaces.Wrappers
{
    /// <summary>
    /// Service for preparing all destination folders BEFORE document move.
    /// FAZA 3 in the migration pipeline (NEW PHASE).
    /// Creates all unique destination folders in parallel to eliminate
    /// on-the-fly folder creation bottleneck in MoveService.
    /// </summary>
    public interface IFolderPreparationService
    {
        /// <summary>
        /// Creates all unique destination folders from DocStaging table.
        /// Uses parallel processing (30-50 concurrent tasks) for maximum throughput.
        /// Idempotent: safe to re-run if folders already exist.
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Task</returns>
        Task PrepareAllFoldersAsync(CancellationToken ct = default);

        /// <summary>
        /// Returns the total number of unique destination folders to be created.
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Total folder count</returns>
        Task<int> GetTotalFolderCountAsync(CancellationToken ct = default);

        /// <summary>
        /// Returns current progress (folders created / total folders).
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Tuple: (Created, Total)</returns>
        Task<(int Created, int Total)> GetProgressAsync(CancellationToken ct = default);
    }
}
