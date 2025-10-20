using Alfresco.Contracts.Oracle.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Oracle.Abstraction.Interfaces
{
    public interface IDocStagingRepository : IRepository<DocStaging, long>
    {
        Task<IReadOnlyList<DocStaging>> TakeReadyForProcessingAsync(int take, CancellationToken ct);
        Task<long> CountReadyForProcessingAsync(CancellationToken ct);
        Task SetStatusAsync(long id, string status, string? error, CancellationToken ct);
        Task FailAsync(long id, string error, CancellationToken ct);
    }
}
