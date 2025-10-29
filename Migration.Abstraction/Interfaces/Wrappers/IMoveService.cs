using Migration.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces.Wrappers
{
    public interface IMoveService
    {
        //Task<int> MoveAsync(MoveRequest inRequest, CancellationToken ct);
        Task<MoveBatchResult> RunBatchAsync(  CancellationToken ct);

        Task<bool> RunLoopAsync( CancellationToken ct);
        Task<bool> RunLoopAsync( CancellationToken ct, Action<WorkerProgress>? progressCallback);
    }
}
