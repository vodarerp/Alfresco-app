namespace Migration.Abstraction.Interfaces
{
    /// <summary>
    /// Result of database preparation before migration.
    /// Contains statistics about reset incomplete items for resume support.
    /// </summary>
    public class MigrationPreparationResult
    {
        /// <summary>
        /// Number of incomplete documents reset in DocStaging
        /// (PREPARATION → READY, IN_PROGRESS → PREPARED, NULL → READY)
        /// </summary>
        public int ResetDocuments { get; set; }

        /// <summary>
        /// Number of incomplete folders reset in FolderStaging
        /// (IN_PROGRESS → READY, NULL → READY)
        /// </summary>
        public int ResetFolders { get; set; }

        /// <summary>
        /// Total number of items reset
        /// </summary>
        public int TotalReset => ResetDocuments + ResetFolders;

        /// <summary>
        /// Whether preparation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if preparation failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}
