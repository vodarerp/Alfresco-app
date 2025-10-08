using Alfresco.Contracts.Oracle.Models;
using Dapper;
using Oracle.Abstraction.Interfaces;

namespace Oracle.Infrastructure.Implementation
{
    public class MigrationCheckpointRepository : OracleRepository<MigrationCheckpoint, long>, IMigrationCheckpointRepository
    {
        public MigrationCheckpointRepository(IUnitOfWork uow) : base(uow)
        {
        }

        public async Task<MigrationCheckpoint?> GetByServiceNameAsync(string serviceName, CancellationToken ct = default)
        {
            var sql = @"SELECT * FROM MigrationCheckpoint
                        WHERE ServiceName = :serviceName";

            var dp = new DynamicParameters();
            dp.Add(":serviceName", serviceName);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
            return await Conn.QueryFirstOrDefaultAsync<MigrationCheckpoint>(cmd).ConfigureAwait(false);
        }

        public async Task<long> UpsertAsync(MigrationCheckpoint checkpoint, CancellationToken ct = default)
        {
            // Try to get existing
            var existing = await GetByServiceNameAsync(checkpoint.ServiceName, ct).ConfigureAwait(false);

            if (existing != null)
            {
                // Update
                checkpoint.Id = existing.Id;
                checkpoint.UpdatedAt = DateTime.UtcNow;

                var sql = @"UPDATE MigrationCheckpoint
                            SET CheckpointData = :checkpointData,
                                LastProcessedId = :lastProcessedId,
                                LastProcessedAt = :lastProcessedAt,
                                TotalProcessed = :totalProcessed,
                                TotalFailed = :totalFailed,
                                UpdatedAt = :updatedAt,
                                BatchCounter = :batchCounter
                            WHERE Id = :id";

                var dp = new DynamicParameters();
                dp.Add(":checkpointData", checkpoint.CheckpointData);
                dp.Add(":lastProcessedId", checkpoint.LastProcessedId);
                dp.Add(":lastProcessedAt", checkpoint.LastProcessedAt);
                dp.Add(":totalProcessed", checkpoint.TotalProcessed);
                dp.Add(":totalFailed", checkpoint.TotalFailed);
                dp.Add(":updatedAt", checkpoint.UpdatedAt);
                dp.Add(":batchCounter", checkpoint.BatchCounter);
                dp.Add(":id", checkpoint.Id);

                var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
                await Conn.ExecuteAsync(cmd).ConfigureAwait(false);

                return checkpoint.Id;
            }
            else
            {
                // Insert
                checkpoint.CreatedAt = DateTime.UtcNow;
                checkpoint.UpdatedAt = DateTime.UtcNow;

                var sql = @"INSERT INTO MigrationCheckpoint
                            (ServiceName, CheckpointData, LastProcessedId, LastProcessedAt,
                             TotalProcessed, TotalFailed, UpdatedAt, CreatedAt, BatchCounter)
                            VALUES
                            (:serviceName, :checkpointData, :lastProcessedId, :lastProcessedAt,
                             :totalProcessed, :totalFailed, :updatedAt, :createdAt, :batchCounter)
                            RETURNING Id INTO :outId";

                var dp = new DynamicParameters();
                dp.Add(":serviceName", checkpoint.ServiceName);
                dp.Add(":checkpointData", checkpoint.CheckpointData);
                dp.Add(":lastProcessedId", checkpoint.LastProcessedId);
                dp.Add(":lastProcessedAt", checkpoint.LastProcessedAt);
                dp.Add(":totalProcessed", checkpoint.TotalProcessed);
                dp.Add(":totalFailed", checkpoint.TotalFailed);
                dp.Add(":updatedAt", checkpoint.UpdatedAt);
                dp.Add(":createdAt", checkpoint.CreatedAt);
                dp.Add(":batchCounter", checkpoint.BatchCounter);
                dp.Add(":outId", dbType: System.Data.DbType.Int64, direction: System.Data.ParameterDirection.Output);

                var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
                await Conn.ExecuteAsync(cmd).ConfigureAwait(false);

                return dp.Get<long>(":outId");
            }
        }

        public async Task DeleteByServiceNameAsync(string serviceName, CancellationToken ct = default)
        {
            var sql = @"DELETE FROM MigrationCheckpoint WHERE ServiceName = :serviceName";

            var dp = new DynamicParameters();
            dp.Add(":serviceName", serviceName);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }
    }
}
