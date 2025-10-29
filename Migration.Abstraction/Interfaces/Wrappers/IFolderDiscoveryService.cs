using Migration.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces.Services
{
    public interface IFolderDiscoveryService
    {
        //Task<int> DiscoverAsync(FolderDiscoverRequest inRequest, CancellationToken ct);

        Task<FolderBatchResult> RunBatchAsync( CancellationToken ct);

        Task<bool> RunLoopAsync(CancellationToken ct);
        Task<bool> RunLoopAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback);



    }


}
