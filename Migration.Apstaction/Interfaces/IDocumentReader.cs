using Alfresco.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Apstaction.Interfaces
{
    public interface IDocumentReader
    {
        public Task<IReadOnlyList<ListEntry>> ReadBatchAsync(string folderNodeId, CancellationToken ct);
    }
}
