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

       
        Task<FolderReaderResult> ReadBatchAsync_v2(
            FolderReaderRequest inRequest,
            string? dateFrom,
            string? dateTo,
            bool useDateFilter,
            CancellationToken ct);

        Task<Dictionary<string, string>> FindDossierSubfoldersAsync(string rootId, List<string>? folderTypes, CancellationToken ct);
    }
}
