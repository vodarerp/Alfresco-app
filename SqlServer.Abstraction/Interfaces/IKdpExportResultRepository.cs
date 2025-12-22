using Alfresco.Contracts.Oracle.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SqlServer.Abstraction.Interfaces
{
    /// <summary>
    /// Repository za rad sa KdpExportResult tabelom
    /// </summary>
    public interface IKdpExportResultRepository : IRepository<KdpExportResult, long>
    {
        /// <summary>
        /// Poziva stored procedure sp_ProcessKdpDocuments
        /// Vraća tuple sa (totalCandidates, totalDocuments)
        /// </summary>
        Task<(int totalCandidates, int totalDocuments)> ProcessKdpDocumentsAsync(CancellationToken ct = default);

        /// <summary>
        /// Vraća sve rezultate za eksport
        /// </summary>
        Task<IReadOnlyList<KdpExportResult>> GetAllExportResultsAsync(CancellationToken ct = default);

        /// <summary>
        /// Vraća broj rezultata u tabeli
        /// </summary>
        Task<long> CountAsync(CancellationToken ct = default);

        /// <summary>
        /// Briše sve zapise iz KdpExportResult tabele
        /// </summary>
        Task ClearResultsAsync(CancellationToken ct = default);
    }
}
