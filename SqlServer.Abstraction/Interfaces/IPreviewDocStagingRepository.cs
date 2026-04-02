using Alfresco.Contracts.Oracle.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SqlServer.Abstraction.Interfaces
{
    public interface IPreviewDocStagingRepository : IRepository<PreviewDocStaging, long>
    {
        // Bulk insert sa MERGE (ignore duplicates po NodeId)
        Task<int> InsertBatchAsync(IEnumerable<PreviewDocStaging> documents, CancellationToken ct = default);

        // Za checkpoint resume - broj već upisanih dokumenata za dati tip dosijea
        Task<long> GetCountByDossierTypeAsync(string dossierType, CancellationToken ct = default);

        // Za PreviewFolderPreparationService - atomično dohvata i zaključava foldere za obradu
        Task<IEnumerable<string>> GetDistinctPendingFoldersAsync(int batchSize, CancellationToken ct = default);

        // Ažuriranje nakon provere Alfresca (folder postoji ili ne postoji)
        Task UpdateFolderDataAsync(string folderName, string? folderId, int isCreated, string status, CancellationToken ct = default);

        // Za UI DataGrid sa server-side paginacijom
        Task<(IEnumerable<PreviewDocStaging> Items, int TotalCount)> GetPagedAsync(int pageNumber, int pageSize, CancellationToken ct = default);

        // Ukupan broj zapisa (za statistiku u UI)
        Task<long> GetTotalCountAsync(CancellationToken ct = default);

        // Broj zapisa po statusu (za statistiku u UI)
        Task<long> GetCountByStatusAsync(string status, CancellationToken ct = default);

        // Brisanje svih zapisa (za testiranje)
        Task DeleteAllAsync(CancellationToken ct = default);
    }
}
