using Migration.Apstaction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Apstaction.Interfaces
{
    public interface IMoveReader
    {
        public Task<IReadOnlyList<MoveReaderResponse>> ReadBatchAsync(int take, CancellationToken ct);
    }
}
