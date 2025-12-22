using Alfresco.Contracts.Oracle.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SqlServer.Abstraction.Interfaces
{
    /// <summary>
    /// Repository za rad sa KdpDocumentStaging tabelom
    /// </summary>
    public interface IKdpDocumentStagingRepository : IRepository<KdpDocumentStaging, long>
    {
        /// <summary>
        /// Briše sve zapise iz KdpDocumentStaging tabele
        /// </summary>
        Task ClearStagingAsync(CancellationToken ct = default);

        /// <summary>
        /// Vraća broj dokumenata u staging tabeli
        /// </summary>
        Task<long> CountAsync(CancellationToken ct = default);
    }
}
