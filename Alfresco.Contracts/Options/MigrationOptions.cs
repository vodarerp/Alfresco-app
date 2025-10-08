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

        public string RootDestinationFolderId { get; set; }

        public string RootDiscoveryFolderId { get; set; }

        public ServiceOptions MoveService { get; set; } = new();

        public ServiceOptions FolderDiscovery { get; set; } = new();

        public ServiceOptions DocumentDiscovery { get; set; } = new();

    }

    public class ServiceOptions
    {
        public int? MaxDegreeOfParallelism { get; set; }
        public int? BatchSize { get; set; }
        public int? DelayBetweenBatchesInMs { get; set; }

        public string? NameFilter { get; set; }
    }
}
