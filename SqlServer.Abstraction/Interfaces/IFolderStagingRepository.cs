using Alfresco.Contracts.Oracle.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SqlServer.Abstraction.Interfaces
{
    public interface IFolderStagingRepository : IRepository<FolderStaging, long>
    {
        Task<IReadOnlyList<FolderStaging>> TakeReadyForProcessingAsync(int take, CancellationToken ct);
        Task<long> CountReadyForProcessingAsync(CancellationToken ct);
        Task SetStatusAsync(long id, string status, string? error, CancellationToken ct);
        Task FailAsync(long id, string error, CancellationToken ct);

        /// <summary>
        /// Inserts multiple folders, ignoring duplicates based on NodeId.
        /// Used by DocumentSearchService to insert unique folders from document batches.
        /// </summary>
        /// <param name="folders">Folders to insert</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Number of folders actually inserted (excluding duplicates)</returns>
        Task<int> InsertManyIgnoreDuplicatesAsync(IEnumerable<FolderStaging> folders, CancellationToken ct);
    }
}
