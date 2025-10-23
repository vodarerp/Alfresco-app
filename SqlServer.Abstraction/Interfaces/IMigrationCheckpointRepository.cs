using Alfresco.Contracts.Oracle.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SqlServer.Abstraction.Interfaces
{
    public interface IMigrationCheckpointRepository : IRepository<MigrationCheckpoint, long>
    {
        Task<MigrationCheckpoint?> GetByServiceNameAsync(string serviceName, CancellationToken ct = default);
        Task<long> UpsertAsync(MigrationCheckpoint checkpoint, CancellationToken ct = default);
        Task DeleteByServiceNameAsync(string serviceName, CancellationToken ct = default);
    }
}
