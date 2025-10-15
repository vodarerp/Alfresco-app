using Alfresco.Contracts.Models;
using Migration.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    public interface IFolderReader
    {
        //Task<IList<ListEntry>> ReadBatchAsync(FolderReaderRequest inRequest, CancellationToken ct);
        Task<FolderReaderResult> ReadBatchAsync(FolderReaderRequest inRequest, CancellationToken ct);

        /// <summary>
        /// Counts total number of folders matching the discovery criteria
        /// </summary>
        Task<long> CountTotalFoldersAsync(string rootId, string nameFilter, CancellationToken ct);
    }
}
