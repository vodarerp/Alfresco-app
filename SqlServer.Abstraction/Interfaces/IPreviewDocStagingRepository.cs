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
        // Bulk insert sa MERGE (ignore duplicates po NodeId) — originalna, row-by-row, batch 100
        Task<int> InsertBatchAsync(IEnumerable<PreviewDocStaging> documents, CancellationToken ct = default);

        // Bulk insert sa MERGE (ignore duplicates po NodeId) — nova metoda za FlushPendingBatches
        // InsertManyAsync (base) ostaje kao backup (plain INSERT)
        Task<int> InsertManyMergeAsync(IEnumerable<PreviewDocStaging> documents, CancellationToken ct = default);

        // Za checkpoint resume - broj već upisanih dokumenata za dati tip dosijea
        Task<long> GetCountByDossierTypeAsync(string dossierType, CancellationToken ct = default);

        // Za PreviewFolderPreparationService - atomično dohvata i zaključava foldere za obradu
        Task<IEnumerable<string>> GetDistinctPendingFoldersAsync(int batchSize, CancellationToken ct = default);

        // Ažuriranje nakon provere Alfresca (folder postoji ili ne postoji)
        Task UpdateFolderDataAsync(string folderName, string? folderId, int isCreated, string status, CancellationToken ct = default);

        // Kombinovani update: folder data + ClientAPI podaci u jednom SQL-u
        Task UpdateFolderDataAndClientApiAsync(string folderName, string? folderId, int isCreated, string status, Migration.Abstraction.Models.ClientData? clientData, CancellationToken ct = default);

        // Za UI DataGrid sa server-side paginacijom
        Task<(IEnumerable<PreviewDocStaging> Items, int TotalCount)> GetPagedAsync(
            int pageNumber, int pageSize,
            CancellationToken ct = default);

        // Ukupan broj zapisa (za statistiku u UI)
        Task<long> GetTotalCountAsync(CancellationToken ct = default);

        // Broj zapisa po statusu (za statistiku u UI)
        Task<long> GetCountByStatusAsync(string status, CancellationToken ct = default);

        // Brisanje svih zapisa (za testiranje)
        Task DeleteAllAsync(CancellationToken ct = default);

        // Upisuje ClientApi* podatke za FOLDER_PENDING_CREATION zapise (po DossierDestinationFolderName)
        Task UpdateClientApiDataAsync(string dossierDestinationFolderName, Migration.Abstraction.Models.ClientData clientData, CancellationToken ct = default);

        // Atomično uzima batch FOLDER_PENDING_CREATION foldera i postavlja ih na IN_PROGRESS
        Task<IEnumerable<string>> GetDistinctFoldersForCreationAsync(int batchSize, CancellationToken ct = default);

        // Vraca jedan reprezentativni zapis za dati folder name (sa ClientApi* podacima)
        Task<PreviewDocStaging?> GetFirstRecordByFolderNameAsync(string folderName, CancellationToken ct = default);

        // Dohvata zapise spremne za transfer u DocStaging (FOLDER_EXISTS ili FOLDER_CREATED),
        // opcionalno filtrirane po DossierType i TargetDossierType
        Task<IEnumerable<PreviewDocStaging>> GetForTransferAsync(
            string? dossierType = null,
            string? targetDossierType = null,
            CancellationToken ct = default);

        // Postavlja Status = 'TRANSFERRED' za dati skup ID-eva
        Task UpdateTransferredBatchAsync(IEnumerable<long> ids, CancellationToken ct = default);

        // Rollback Faze 3: dohvata distinct (FolderName, FolderId) za sve FOLDER_CREATED zapise
        Task<IEnumerable<(string FolderName, string FolderId)>> GetCreatedFolderIdsAsync(CancellationToken ct = default);

        // Atomično uzima batch foldera sa statusom FOLDER_PENDING_CREATION ili FOLDER_EXISTS i postavlja ih na IN_PROGRESS
        // NeedsCreation = true → FOLDER_PENDING_CREATION (treba kreirati), false → FOLDER_EXISTS (samo upis u FolderStaging)
        Task<IEnumerable<(string FolderName, bool NeedsCreation)>> GetDistinctFoldersForFolderStagingAsync(int batchSize, CancellationToken ct = default);

        // Dohvata zapise za eksport, opcionalno filtrirane po DossierType i TargetDossierType
        Task<IEnumerable<PreviewDocStaging>> GetForExportAsync(
            string? dossierType = null,
            string? targetDossierType = null,
            CancellationToken ct = default);

        // Streaming verzija za eksport (Dapper buffered:false) — konekcija mora biti otvorena tokom iteracije
        IEnumerable<PreviewDocStaging> GetForExportUnbuffered(
            string? dossierType = null,
            string? targetDossierType = null);

        // Dohvata distinct TargetDossierType vrednosti za eksport (lightweight query za određivanje sheet-ova)
        Task<IList<string?>> GetDistinctExportTargetTypesAsync(
            string? dossierType = null,
            CancellationToken ct = default);

        // Vraca count redova po TargetDossierType — za odluku o sheet splitting-u
        Task<IList<(string? TargetDossierType, long Count)>> GetExportTargetTypeCountsAsync(
            string? dossierType = null,
            CancellationToken ct = default);

        // Paged unbuffered za sheet splitting kada ima > 900k redova
        IEnumerable<PreviewDocStaging> GetForExportUnbufferedPaged(
            string? dossierType,
            string? targetDossierType,
            long offset,
            int pageSize);

        // Atomično uzima batch zapisa spremnih za transfer i postavlja ih na TRANSFER_IN_PROGRESS
        Task<IList<PreviewDocStaging>> TakeReadyForTransferAsync(
            int batchSize,
            string? dossierType,
            string? targetDossierType,
            CancellationToken ct = default);

        // Resetuje zapise sa statusom TRANSFER_IN_PROGRESS nazad na originalni status (za rollback pri grešci)
        Task ResetTransferInProgressAsync(IEnumerable<long> ids, CancellationToken ct = default);
    }
}
