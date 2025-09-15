using Migration.Apstaction.Interfaces.Wrappers;
using Migration.Apstaction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    internal class MoveService : IMoveService
    {
        public Task<MoveBatchResult> RunBatchAsync(MoveBatchRequest inRequest, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task RunLoopAsync(MoveLoopOptions inOptions, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}
