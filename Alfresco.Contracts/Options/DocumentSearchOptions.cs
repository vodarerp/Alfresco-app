using System;
using System.Collections.Generic;

namespace Alfresco.Contracts.Options
{
    /// <summary>
    /// Configuration options for DocumentSearchService (MigrationByDocument mode)
    /// This service searches documents directly by ecm:docType instead of discovering folders first
    /// </summary>
    public class DocumentSearchOptions
    {
        /// <summary>
        /// List of document type codes to search for (ecm:docType values)
        /// Example: ["00099", "00824", "00125"]
        /// </summary>
        public List<string> DocTypes { get; set; } = new();

        /// <summary>
        /// List of folder types (DOSSIER-{type}) to search within
        /// Example: ["PI", "LE", "D"]
        /// Each type corresponds to DOSSIER-PI, DOSSIER-LE, DOSSIER-D folders
        /// </summary>
        public List<string> FolderTypes { get; set; } = new();

        /// <summary>
        /// Number of documents to process in each batch
        /// Default: 100
        /// </summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>
        /// Maximum degree of parallelism for processing
        /// Default: 5
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = 5;

        /// <summary>
        /// Delay between batches in milliseconds
        /// Default: 0 (no delay)
        /// </summary>
        public int DelayBetweenBatchesInMs { get; set; } = 0;

        /// <summary>
        /// If true, applies date filtering on ecm:docCreationDate
        /// </summary>
        public bool UseDateFilter { get; set; } = false;

        /// <summary>
        /// Start date for date filtering (inclusive)
        /// Format: yyyy-MM-dd or yyyy-MM-ddTHH:mm:ss.fffZ
        /// Only used when UseDateFilter is true
        /// </summary>
        public string? DateFrom { get; set; }

        /// <summary>
        /// End date for date filtering (inclusive)
        /// Format: yyyy-MM-dd or yyyy-MM-ddTHH:mm:ss.fffZ
        /// Only used when UseDateFilter is true
        /// </summary>
        public string? DateTo { get; set; }
    }
}
