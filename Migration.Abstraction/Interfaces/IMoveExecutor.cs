using Migration.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    public interface IMoveExecutor
    {
       // Task<int> ExecuteMoveAsync(int take, CancellationToken ct);

        Task<bool> MoveAsync(string DocumentId, string DestFolderId, CancellationToken ct);

    }
}
