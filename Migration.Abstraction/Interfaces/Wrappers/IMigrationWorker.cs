using Alfresco.Contracts.Enums;

namespace Migration.Abstraction.Interfaces.Wrappers
{
    /// <summary>
    /// Orchestrator for the entire migration pipeline.
    /// Executes all 4 phases sequentially: FolderDiscovery → DocumentDiscovery → FolderPreparation → Move
    /// Provides phase-level checkpoint management and resumability.
    /// </summary>
    public interface IMigrationWorker
    {
        /// <summary>
        /// Runs the entire migration pipeline: all 4 phases sequentially.
        /// Each phase must complete before the next one begins.
        /// Supports resume from last completed phase.
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Task</returns>
        Task RunAsync(CancellationToken ct = default);

        /// <summary>
        /// Gets current migration status (which phase is running, progress, etc.)
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Migration status with current phase info</returns>
        Task<MigrationPipelineStatus> GetStatusAsync(CancellationToken ct = default);

        /// <summary>
        /// Resets all phase checkpoints to NotStarted and clears all progress.
        /// Use this to restart migration from the beginning.
        /// WARNING: This does NOT clear DocStaging/FolderStaging data!
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Task</returns>
        Task ResetAsync(CancellationToken ct = default);

        /// <summary>
        /// Resets a specific phase to NotStarted.
        /// Use this to re-run a specific phase (e.g., after fixing an error).
        /// </summary>
        /// <param name="phase">The phase to reset</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Task</returns>
        Task ResetPhaseAsync(MigrationPhase phase, CancellationToken ct = default);
    }

    /// <summary>
    /// Represents the current status of the migration pipeline
    /// (Renamed from MigrationStatus to avoid collision with Alfresco.Contracts.Enums.MigrationStatus)
    /// </summary>
    public class MigrationPipelineStatus
    {
        /// <summary>
        /// Current phase being executed
        /// </summary>
        public MigrationPhase CurrentPhase { get; set; }

        /// <summary>
        /// Status of the current phase
        /// </summary>
        public PhaseStatus CurrentPhaseStatus { get; set; }

        /// <summary>
        /// Progress of current phase (0-100%)
        /// </summary>
        public int CurrentPhaseProgress { get; set; }

        /// <summary>
        /// When the migration started (first phase started)
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// Total elapsed time since migration started
        /// </summary>
        public TimeSpan? ElapsedTime { get; set; }

        /// <summary>
        /// Estimated time remaining for entire migration
        /// </summary>
        public TimeSpan? EstimatedRemainingTime { get; set; }

        /// <summary>
        /// Total items processed across all phases
        /// </summary>
        public long TotalProcessed { get; set; }

        /// <summary>
        /// Error message if any phase failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Overall migration status message
        /// </summary>
        public string StatusMessage { get; set; } = string.Empty;
    }
}
