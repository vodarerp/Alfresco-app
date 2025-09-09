using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Apstaction.Interfaces.Services
{
    public interface IMovePlanningService
    {
        Task<int> MovePlanningAsync(int folderTake, int pageSIze, CancellationToken ct);
    }
}
