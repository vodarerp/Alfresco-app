using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Apstaction.Interfaces
{
    public interface IMoveExecutor
    {
        Task<int> ExecuteMoveAsync(int take, CancellationToken ct);

        Task MoveAsync(MoveExecutorRequest inRequest, CancellationToken ct);

    }
}
