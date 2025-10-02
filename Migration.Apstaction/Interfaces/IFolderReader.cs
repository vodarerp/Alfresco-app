using Alfresco.Contracts.Models;
using Migration.Apstraction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Apstraction.Interfaces
{
    public interface IFolderReader
    {
        //Task<IList<ListEntry>> ReadBatchAsync(FolderReaderRequest inRequest, CancellationToken ct);
        Task<FolderReaderResult> ReadBatchAsync(FolderReaderRequest inRequest, CancellationToken ct);

    }
}
