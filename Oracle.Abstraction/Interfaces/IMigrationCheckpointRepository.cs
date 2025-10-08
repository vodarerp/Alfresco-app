using Alfresco.Contracts.Oracle.Models;

namespace Oracle.Abstraction.Interfaces
{
    public interface IMigrationCheckpointRepository : IRepository<MigrationCheckpoint, long>
    {
        /// <summary>
        /// Get checkpoint for specific service
        /// </summary>
        Task<MigrationCheckpoint?> GetByServiceNameAsync(string serviceName, CancellationToken ct = default);

        /// <summary>
        /// Save or update checkpoint
        /// </summary>
        Task<long> UpsertAsync(MigrationCheckpoint checkpoint, CancellationToken ct = default);

        /// <summary>
        /// Delete checkpoint for service
        /// </summary>
        Task DeleteByServiceNameAsync(string serviceName, CancellationToken ct = default);
    }
}
