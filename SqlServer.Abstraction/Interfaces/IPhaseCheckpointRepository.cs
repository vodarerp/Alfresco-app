using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Oracle.Models;

namespace SqlServer.Abstraction.Interfaces
{
    /// <summary>
    /// Repository for managing phase-level migration checkpoints.
    /// Provides operations for tracking and resuming migration phases.
    /// </summary>
    public interface IPhaseCheckpointRepository : IRepository<PhaseCheckpoint, long>
    {
        /// <summary>
        /// Get checkpoint for a specific phase
        /// </summary>
        /// <param name="phase">The migration phase</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>PhaseCheckpoint or null if not found</returns>
        Task<PhaseCheckpoint?> GetCheckpointAsync(MigrationPhase phase, CancellationToken ct = default);

        /// <summary>
        /// Get all checkpoints ordered by phase
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>List of all phase checkpoints</returns>
        Task<List<PhaseCheckpoint>> GetAllCheckpointsAsync(CancellationToken ct = default);

        /// <summary>
        /// Mark phase as started (sets status to InProgress and StartedAt timestamp)
        /// </summary>
        /// <param name="phase">The migration phase</param>
        /// <param name="ct">Cancellation token</param>
        Task MarkPhaseStartedAsync(MigrationPhase phase, CancellationToken ct = default);

        /// <summary>
        /// Mark phase as completed (sets status to Completed and CompletedAt timestamp)
        /// </summary>
        /// <param name="phase">The migration phase</param>
        /// <param name="ct">Cancellation token</param>
        Task MarkPhaseCompletedAsync(MigrationPhase phase, CancellationToken ct = default);

        /// <summary>
        /// Mark phase as failed (sets status to Failed, CompletedAt timestamp, and error message)
        /// </summary>
        /// <param name="phase">The migration phase</param>
        /// <param name="errorMessage">Error message describing the failure</param>
        /// <param name="ct">Cancellation token</param>
        Task MarkPhaseFailedAsync(MigrationPhase phase, string errorMessage, CancellationToken ct = default);

        /// <summary>
        /// Update phase progress (LastProcessedIndex, LastProcessedId, TotalProcessed)
        /// </summary>
        /// <param name="phase">The migration phase</param>
        /// <param name="lastProcessedIndex">Last processed index</param>
        /// <param name="lastProcessedId">Last processed ID</param>
        /// <param name="totalProcessed">Total items processed</param>
        /// <param name="ct">Cancellation token</param>
        Task UpdateProgressAsync(
            MigrationPhase phase,
            int? lastProcessedIndex,
            string? lastProcessedId,
            long totalProcessed,
            CancellationToken ct = default);

        /// <summary>
        /// Reset all phases to NotStarted status (for restarting migration from beginning)
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        Task ResetAllPhasesAsync(CancellationToken ct = default);

        /// <summary>
        /// Reset a specific phase to NotStarted status
        /// </summary>
        /// <param name="phase">The migration phase to reset</param>
        /// <param name="ct">Cancellation token</param>
        Task ResetPhaseAsync(MigrationPhase phase, CancellationToken ct = default);

        /// <summary>
        /// Sets the DocTypes for a specific phase (used in MigrationByDocument mode)
        /// </summary>
        /// <param name="phase">The migration phase</param>
        /// <param name="docTypes">Comma-separated document types</param>
        /// <param name="ct">Cancellation token</param>
        Task SetDocTypesAsync(MigrationPhase phase, string docTypes, CancellationToken ct = default);
    }
}
