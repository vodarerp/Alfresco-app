using Migration.Apstraction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Apstraction.Interfaces.Services
{
    public interface IFolderDiscoveryService
    {
        //Task<int> DiscoverAsync(FolderDiscoverRequest inRequest, CancellationToken ct);

        Task<FolderBatchResult> RunBatchAsync( CancellationToken ct);

        Task RunLoopAsync(CancellationToken ct);


    }


}
