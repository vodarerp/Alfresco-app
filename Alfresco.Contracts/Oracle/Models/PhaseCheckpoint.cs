using System.ComponentModel.DataAnnotations.Schema;
using Alfresco.Contracts.Enums;

namespace Alfresco.Contracts.Oracle.Models
{
    /// <summary>
    /// Tracks the status and progress of migration phases.
    /// Each phase must complete before the next one begins.
    /// Provides phase-level resumability.
    /// </summary>
    [Table("PhaseCheckpoints")]
    public class PhaseCheckpoint
    {
        public long Id { get; set; }

        /// <summary>
        /// The migration phase (FolderDiscovery, DocumentDiscovery, FolderPreparation, Move)
        /// </summary>
        public MigrationPhase Phase { get; set; }

        /// <summary>
        /// Current status of the phase (NotStarted, InProgress, Completed, Failed)
        /// </summary>
        public PhaseStatus Status { get; set; }

        /// <summary>
        /// For resumability: last processed index within the phase
        /// Used by the service to resume from checkpoint
        /// </summary>
        public int? LastProcessedIndex { get; set; }

        /// <summary>
        /// For resumability: last processed item ID within the phase
        /// Used by the service to resume from checkpoint
        /// </summary>
        public string? LastProcessedId { get; set; }

        /// <summary>
        /// Timestamp when phase started
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// Timestamp when phase completed (successfully or with failure)
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Error message if phase failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Total items processed in this phase
        /// </summary>
        public long TotalProcessed { get; set; }

        /// <summary>
        /// Total items to process (if known)
        /// </summary>
        public long? TotalItems { get; set; }

        /// <summary>
        /// Created timestamp
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Last update timestamp
        /// </summary>
        public DateTime UpdatedAt { get; set; }
    }
}
