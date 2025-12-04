namespace Alfresco.Contracts.Enums
{
    /// <summary>
    /// Defines the sequential phases of the migration process.
    /// Each phase must complete before the next one begins.
    /// </summary>
    public enum MigrationPhase
    {
        /// <summary>
        /// FAZA 1 (MigrationByDocument mode): Search documents by ecm:docType and store in DocStaging + FolderStaging
        /// Alternative to FolderDiscovery + DocumentDiscovery phases
        /// </summary>
        DocumentSearch = 0,

        /// <summary>
        /// FAZA 1: Discover all folders from Alfresco and store in FolderStaging
        /// </summary>
        FolderDiscovery = 1,

        /// <summary>
        /// FAZA 2: Discover all documents from Alfresco and store in DocStaging
        /// </summary>
        DocumentDiscovery = 2,

        /// <summary>
        /// FAZA 3: Prepare all destination folders in parallel (NEW PHASE)
        /// </summary>
        FolderPreparation = 3,

        /// <summary>
        /// FAZA 4: Move documents to prepared destination folders
        /// </summary>
        Move = 4
    }
}
