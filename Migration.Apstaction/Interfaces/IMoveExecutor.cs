using Migration.Apstraction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Apstraction.Interfaces
{
    public interface IMoveExecutor
    {
       // Task<int> ExecuteMoveAsync(int take, CancellationToken ct);

        Task<bool> MoveAsync(string DocumentId, string DestFolderId, CancellationToken ct);

    }
}
