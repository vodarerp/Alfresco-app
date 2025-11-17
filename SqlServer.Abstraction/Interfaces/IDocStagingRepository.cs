using Alfresco.Contracts.Models;
using Alfresco.Contracts.Oracle.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SqlServer.Abstraction.Interfaces
{
    public interface IDocStagingRepository : IRepository<DocStaging, long>
    {
        Task<IReadOnlyList<DocStaging>> TakeReadyForProcessingAsync(int take, CancellationToken ct);
        Task<long> CountReadyForProcessingAsync(CancellationToken ct);
        Task SetStatusAsync(long id, string status, string? error, CancellationToken ct);
        Task FailAsync(long id, string error, CancellationToken ct);

        /// <summary>
        /// Gets all unique destination folders from DocStaging (DISTINCT by TargetDossierType + DossierDestFolderId).
        /// Used by FolderPreparationService to create all folders before document move.
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>List of unique folder information</returns>
        Task<List<UniqueFolderInfo>> GetUniqueDestinationFoldersAsync(CancellationToken ct = default);
    }
}
