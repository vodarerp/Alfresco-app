using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Apstaction.Interfaces
{
    public interface IMovePlanner
    {
        Task<int> PlanAsync(string srcFolderId, string destFodlerId, int pageSize, CancellationToken ct);
    }
}
