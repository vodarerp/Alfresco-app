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
        public int StuckItemsTimeoutMinutes { get; set; } = 10;       
        public int MaxDocumentsToProcess { get; set; } = 0;
        public string? RootDestinationFolderId { get; set; }      
        public string? RootDiscoveryFolderId { get; set; }       
        public string? RootACCFolderId { get; set; }       
        public string? RootLEFolderId { get; set; }       
        public string? RootPIFolderId { get; set; }       
        public string? RootDocumentPath { get; set; }        
        public bool PreviewTypeMigration { get; set; } = false;
        public bool MigrationByDocument { get; set; } = false;

        public ServiceOptions MoveService { get; set; } = new();

        public ServiceOptions FolderDiscovery { get; set; } = new();

        public ServiceOptions DocumentDiscovery { get; set; } = new();
        
        public DocumentSearchOptions DocumentTypeDiscovery { get; set; } = new();
        
        public KdpProcessingOptions KdpProcessing { get; set; } = new();
        
        public Dictionary<string, string> FolderNodeTypeMapping { get; set; } = new();

    }

    public class ServiceOptions
    {
        public int? MaxDegreeOfParallelism { get; set; }
        public int? BatchSize { get; set; }
        public int? DelayBetweenBatchesInMs { get; set; }
        public string? NameFilter { get; set; }       
        public bool UseCopy { get; set; } = false;      
        public List<string>? FolderTypes { get; set; }      
        public List<string>? TargetCoreIds { get; set; }      
        public bool UseV2Reader { get; set; } = false;      
        public bool UseDateFilter { get; set; } = false;       
        public string? DateFrom { get; set; }       
        public string? DateTo { get; set; }
    }
}
