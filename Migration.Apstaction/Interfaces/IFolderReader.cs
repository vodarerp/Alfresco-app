using Alfresco.Contracts.Models;
using Migration.Apstaction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Apstaction.Interfaces
{
    public interface IFolderReader
    {
        Task<IList<ListEntry>> ReadBatchAsync(FolderReaderRequest inRequest, CancellationToken ct);
    }
}
