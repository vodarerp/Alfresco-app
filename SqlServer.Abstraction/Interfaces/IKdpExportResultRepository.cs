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

        /// <summary>
        /// Pretrazuje rezultate po filterima sa paginacijom
        /// </summary>
        Task<(IReadOnlyList<KdpExportResult> Results, int TotalCount)> SearchAsync(
            string? coreId = null,
            string? oldStatus = null,
            string? newStatus = null,
            int? action = null,
            int skip = 0,
            int take = 25,
            CancellationToken ct = default);

        /// <summary>
        /// Vraća batch neažuriranih dokumenata za procesiranje
        /// </summary>
        /// <param name="batchSize">Veličina batcha</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Lista neažuriranih dokumenata</returns>
        Task<IReadOnlyList<KdpExportResult>> GetUnupdatedBatchAsync(int batchSize, CancellationToken ct = default);

        /// <summary>
        /// Označava batch dokumenata kao ažurirane
        /// </summary>
        /// <param name="documentIds">Lista ID-jeva dokumenata</param>
        /// <param name="updateMessages">Dictionary sa ID-jem i porukom za svaki dokument</param>
        /// <param name="ct">Cancellation token</param>
        Task MarkBatchAsUpdatedAsync(IEnumerable<long> documentIds, Dictionary<long, string>? updateMessages = null, CancellationToken ct = default);

        /// <summary>
        /// Vraća broj neažuriranih dokumenata
        /// </summary>
        Task<long> CountUnupdatedAsync(CancellationToken ct = default);

        /// <summary>
        /// Ažurira pojedinačni dokument sa rezultatom update-a
        /// </summary>
        Task UpdateDocumentStatusAsync(long id, bool isUpdated, string? message, CancellationToken ct = default);
    }
}
