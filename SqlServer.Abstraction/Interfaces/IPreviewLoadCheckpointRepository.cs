using Alfresco.Contracts.Oracle.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SqlServer.Abstraction.Interfaces
{
    public interface IPreviewLoadCheckpointRepository : IRepository<PreviewLoadCheckpoint, long>
    {
        /// <summary>
        /// Vraća broj dokumenata koji su već fetchovani sa Alfresca za dati tip dosijea.
        /// Koristi se za generisanje skipValues pri resume-u.
        /// </summary>
        Task<long> GetFetchedCountAsync(string folderType, CancellationToken ct = default);

        /// <summary>
        /// Vraća puno stanje checkpointa — processed/failed skip setovi + high-water mark.
        /// </summary>
        Task<CheckpointState> GetCheckpointStateAsync(string folderType, CancellationToken ct = default);

        /// <summary>
        /// Upisuje ili ažurira checkpoint za dati tip dosijea (MERGE on FolderType).
        /// Thin wrapper — ne dira nove kolone (backward compat).
        /// </summary>
        Task UpsertAsync(string folderType, long totalFetched, CancellationToken ct = default);

        /// <summary>
        /// Upisuje ili ažurira checkpoint sa punim skip setovima.
        /// totalFetched je high-water mark (najveći kontinuirani blok × batchSize).
        /// </summary>
        Task UpsertCheckpointStateAsync(
            string folderType,
            long fetchedCount,
            IEnumerable<int> processedSkips,
            IEnumerable<int> failedSkips,
            CancellationToken ct = default);

        /// <summary>
        /// Briše sve checkpointe - poziva se pre novog pokretanja od nule.
        /// </summary>
        Task ResetAllAsync(CancellationToken ct = default);
    }
}
