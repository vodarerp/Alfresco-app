using Migration.Apstaction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Apstaction.Interfaces.Services
{
    public interface IFolderDiscoveryService
    {
        //Task<int> DiscoverAsync(FolderDiscoverRequest inRequest, CancellationToken ct);

        Task<FolderBatchResult> RunBatchAsync(FolderDiscoveryBatchRequest inRequest, CancellationToken ct);

        Task RunLoopAsync(FolderDiscoveryLoopOptions inOptions, CancellationToken ct);


    }


}
