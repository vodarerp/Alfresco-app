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
        /// Reads a batch of folders using CMIS query language with pagination support.
        /// This method supports date filtering and proper skip/take logic for large result sets.
        /// </summary>
        /// <param name="inRequest">Request containing folder search criteria</param>
        /// <param name="dateFrom">Optional start date for filtering (format: yyyy-MM-ddTHH:mm:ss.fffZ)</param>
        /// <param name="dateTo">Optional end date for filtering (format: yyyy-MM-ddTHH:mm:ss.fffZ)</param>
        /// <param name="useDateFilter">If true, applies date filtering to the query</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Folder reader result with items and pagination cursor</returns>
        Task<FolderReaderResult> ReadBatchAsync_v2(
            FolderReaderRequest inRequest,
            string? dateFrom,
            string? dateTo,
            bool useDateFilter,
            CancellationToken ct);

        /// <summary>
        /// Counts total number of folders matching the discovery criteria
        /// </summary>
        Task<long> CountTotalFoldersAsync(string rootId, string nameFilter, CancellationToken ct);

        /// <summary>
        /// Finds subfolders matching the DOSSIER-{Type} pattern
        /// </summary>
        /// <param name="rootId">Root folder ID to search in</param>
        /// <param name="folderTypes">List of folder types (e.g., "PL", "FL"). If null/empty, returns all DOSSIER-* folders</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Dictionary mapping folder type to folder ID</returns>
        Task<Dictionary<string, string>> FindDossierSubfoldersAsync(string rootId, List<string>? folderTypes, CancellationToken ct);
    }
}
