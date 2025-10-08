using System.ComponentModel.DataAnnotations.Schema;

namespace Alfresco.Contracts.Oracle.Models
{
    [Table("MigrationCheckpoint")]
    public class MigrationCheckpoint
    {
        public long Id { get; set; }

        /// <summary>
        /// Service name: FolderDiscovery, DocumentDiscovery, Move
        /// </summary>
        public string ServiceName { get; set; } = string.Empty;

        /// <summary>
        /// JSON serialized checkpoint data
        /// </summary>
        public string? CheckpointData { get; set; }

        /// <summary>
        /// Last processed item ID (for FolderDiscovery: LastObjectId)
        /// </summary>
        public string? LastProcessedId { get; set; }

        /// <summary>
        /// Last processed timestamp (for FolderDiscovery: LastObjectCreated)
        /// </summary>
        public DateTime? LastProcessedAt { get; set; }

        /// <summary>
        /// Total items processed
        /// </summary>
        public long TotalProcessed { get; set; }

        /// <summary>
        /// Total items failed
        /// </summary>
        public long TotalFailed { get; set; }

        /// <summary>
        /// Last update timestamp
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Created timestamp
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Optional: Batch counter
        /// </summary>
        public int BatchCounter { get; set; }
    }
}
