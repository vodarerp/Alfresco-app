using Alfresco.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    public interface IDocumentReader
    {
        public Task<IReadOnlyList<ListEntry>> ReadBatchAsync(string folderNodeId, CancellationToken ct);

        public Task<IReadOnlyList<ListEntry>> ReadBatchWithPaginationAsync(string folderNodeId, int skipCount, int maxItems, CancellationToken ct);
    }
}
