using Alfresco.Contracts.Oracle.Models;
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
        /// Upisuje ili ažurira checkpoint za dati tip dosijea (MERGE on FolderType).
        /// </summary>
        Task UpsertAsync(string folderType, long totalFetched, CancellationToken ct = default);

        /// <summary>
        /// Briše sve checkpointe - poziva se pre novog pokretanja od nule.
        /// </summary>
        Task ResetAllAsync(CancellationToken ct = default);
    }
}
