namespace Migration.Abstraction.Interfaces
{
    /// <summary>
    /// Result of database preparation before migration.
    /// Contains statistics about deleted incomplete items.
    /// </summary>
    public class MigrationPreparationResult
    {
        /// <summary>
        /// Number of incomplete documents deleted from DocStaging
        /// </summary>
        public int DeletedDocuments { get; set; }

        /// <summary>
        /// Number of incomplete folders deleted from FolderStaging
        /// </summary>
        public int DeletedFolders { get; set; }

        /// <summary>
        /// Total number of items deleted
        /// </summary>
        public int TotalDeleted => DeletedDocuments + DeletedFolders;

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
