using System;
using System.Collections.Generic;

namespace Alfresco.Contracts.Options
{
    /// <summary>
    /// Configuration options for KDP Document Processing Service
    /// This service searches for KDP documents (types 00824 and 00099) and processes them
    /// </summary>
    public class KdpProcessingOptions
    {
        /// <summary>
        /// Ancestor folder ID where KDP documents should be searched
        /// This is the root folder ID in Alfresco for KDP document discovery
        /// Format: workspace://SpacesStore/{guid} or just {guid}
        /// Example: "workspace://SpacesStore/32f14d10-59e6-4783-b14d-1059e64783f4"
        /// </summary>
        public string? AncestorFolderId { get; set; }

        /// <summary>
        /// Number of documents to fetch per batch
        /// Default: 1000
        /// </summary>
        public int BatchSize { get; set; } = 1000;

        /// <summary>
        /// List of document type codes to search for (ecm:docType values)
        /// Default: ["00824", "00099"]
        /// </summary>
        public List<string> DocTypes { get; set; } = new() { "00824", "00099" };
    }
}
