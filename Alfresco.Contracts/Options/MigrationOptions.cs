using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Options
{
    public class MigrationOptions
    {
        public int MaxDegreeOfParallelism { get; set; } = 5;
        public int BatchSize { get; set; } = 1000;
        public int DelayBetweenBatchesInMs { get; set; } = 0;

        public int IdleDelayInMs { get; set; } = 100;
        public int BreakEmptyResults { get; set; }

        /// <summary>
        /// Timeout in minutes for stuck items in IN PROGRESS status
        /// After this time, they will be reset to READY on service startup
        /// </summary>
        public int StuckItemsTimeoutMinutes { get; set; } = 10;

        /// <summary>
        /// Root folder ID in Alfresco for destination (migration target)
        /// </summary>
        public string RootDestinationFolderId { get; set; }

        /// <summary>
        /// Root folder ID in Alfresco for discovery (source scanning)
        /// </summary>
        public string RootDiscoveryFolderId { get; set; }

        /// <summary>
        /// Physical root folder path on filesystem for document storage
        /// Structure: ROOT -> dosie-{ClientType} -> {ClientType}{CoreId} -> documents
        /// Example: C:\DocumentsRoot or /mnt/documents
        /// NOTE: This should be stored in environment variable or external config, not in database
        /// </summary>
        public string? RootDocumentPath { get; set; }

        /// <summary>
        /// If true, uses MigrationByDocument mode (DocumentSearchService)
        /// which searches documents directly by ecm:docType.
        /// If false (default), uses MigrationByFolder mode (FolderDiscovery + DocumentDiscovery)
        /// </summary>
        public bool MigrationByDocument { get; set; } = false;

        public ServiceOptions MoveService { get; set; } = new();

        public ServiceOptions FolderDiscovery { get; set; } = new();

        public ServiceOptions DocumentDiscovery { get; set; } = new();

        /// <summary>
        /// Configuration for DocumentSearchService (used when MigrationByDocument = true)
        /// </summary>
        public DocumentSearchOptions DocumentTypeDiscovery { get; set; } = new();

        /// <summary>
        /// Configuration for KDP Document Processing Service
        /// </summary>
        public KdpProcessingOptions KdpProcessing { get; set; } = new();

        /// <summary>
        /// Mapping of folder node types to Alfresco content model types
        /// Used when creating new folders in Alfresco
        /// </summary>
        public Dictionary<string, string> FolderNodeTypeMapping { get; set; } = new();

    }

    public class ServiceOptions
    {
        public int? MaxDegreeOfParallelism { get; set; }
        public int? BatchSize { get; set; }
        public int? DelayBetweenBatchesInMs { get; set; }

        public string? NameFilter { get; set; }

        /// <summary>
        /// If true, uses copy instead of move when migrating documents.
        /// Default: false (use move)
        /// </summary>
        public bool UseCopy { get; set; } = false;

        /// <summary>
        /// List of folder types to process (e.g., "PL", "FL", "ACC", "D", "PI").
        /// If null or empty, all folder types will be processed.
        /// Folders are expected to be in subfolders named: DOSSIER-{FolderType}
        /// </summary>
        public List<string>? FolderTypes { get; set; }

        /// <summary>
        /// List of CoreIds to process. Only folders/dossiers containing these CoreIds will be processed.
        /// If null or empty, all folders will be processed (no CoreId filtering).
        /// CoreId is typically extracted from folder names (e.g., "PL-10000003" contains CoreId "10000003")
        /// </summary>
        public List<string>? TargetCoreIds { get; set; }

        /// <summary>
        /// If true, uses ReadBatchAsync_v2 method with CMIS query language instead of AFTS.
        /// This enables skip/take pagination and date filtering.
        /// Default: false (use AFTS-based ReadBatchAsync)
        /// </summary>
        public bool UseV2Reader { get; set; } = false;

        /// <summary>
        /// If true, includes date filtering in CMIS queries (cmis:creationDate).
        /// Used by ReadBatchAsync_v2 method.
        /// </summary>
        public bool UseDateFilter { get; set; } = false;

        /// <summary>
        /// Start date for date filtering in CMIS queries (inclusive).
        /// Format: yyyy-MM-ddTHH:mm:ss.fffZ
        /// Only used when UseDateFilter is true.
        /// </summary>
        public string? DateFrom { get; set; }

        /// <summary>
        /// End date for date filtering in CMIS queries (inclusive).
        /// Format: yyyy-MM-ddTHH:mm:ss.fffZ
        /// Only used when UseDateFilter is true.
        /// </summary>
        public string? DateTo { get; set; }
    }
}
