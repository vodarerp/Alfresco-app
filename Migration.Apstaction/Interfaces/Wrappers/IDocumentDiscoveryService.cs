using Migration.Apstaction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Apstaction.Interfaces.Wrappers
{
    public interface IDocumentDiscoveryService
    {
        //Task<int> DiscoverAsync(DocumentDiscoverRequest inRequest, CancellationToken ct);

        Task<DocumentBatchResult> RunBatchAsync(CancellationToken ct);

        Task RunLoopAsync(CancellationToken ct);



    }
}
