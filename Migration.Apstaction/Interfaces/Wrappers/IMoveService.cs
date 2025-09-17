using Migration.Apstaction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Apstaction.Interfaces.Wrappers
{
    public interface IMoveService
    {
        //Task<int> MoveAsync(MoveRequest inRequest, CancellationToken ct);
        Task<MoveBatchResult> RunBatchAsync(  CancellationToken ct);

        Task RunLoopAsync( CancellationToken ct);
    }
}
