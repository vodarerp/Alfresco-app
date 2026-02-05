using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.SqlServer;
using Dapper;
using SqlServer.Abstraction.Interfaces;

namespace SqlServer.Infrastructure.Implementation
{
    public class MigrationCheckpointRepository : SqlServerRepository<MigrationCheckpoint, long>, IMigrationCheckpointRepository
    {
        public MigrationCheckpointRepository(IUnitOfWork uow, SqlServerOptions sqlServerOptions) : base(uow, sqlServerOptions)
        {
        }

        public async Task<MigrationCheckpoint?> GetByServiceNameAsync(string serviceName, CancellationToken ct = default)
        {
            var sql = @"SELECT * FROM MigrationCheckpoint
                        WHERE ServiceName = @serviceName";

            var dp = new DynamicParameters();
            dp.Add("@serviceName", serviceName);

            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
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
                            SET CheckpointData = @checkpointData,
                                LastProcessedId = @lastProcessedId,
                                LastProcessedAt = @lastProcessedAt,
                                TotalProcessed = @totalProcessed,
                                TotalFailed = @totalFailed,
                                UpdatedAt = @updatedAt,
                                BatchCounter = @batchCounter
                            WHERE Id = @id";

                var dp = new DynamicParameters();
                dp.Add("@checkpointData", checkpoint.CheckpointData);
                dp.Add("@lastProcessedId", checkpoint.LastProcessedId);
                dp.Add("@lastProcessedAt", checkpoint.LastProcessedAt);
                dp.Add("@totalProcessed", checkpoint.TotalProcessed);
                dp.Add("@totalFailed", checkpoint.TotalFailed);
                dp.Add("@updatedAt", checkpoint.UpdatedAt);
                dp.Add("@batchCounter", checkpoint.BatchCounter);
                dp.Add("@id", checkpoint.Id);

                var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
                await Conn.ExecuteAsync(cmd).ConfigureAwait(false);

                return checkpoint.Id;
            }
            else
            {
                // Insert - SQL Server uses OUTPUT instead of RETURNING
                checkpoint.CreatedAt = DateTime.UtcNow;
                checkpoint.UpdatedAt = DateTime.UtcNow;

                var sql = @"INSERT INTO MigrationCheckpoint
                            (ServiceName, CheckpointData, LastProcessedId, LastProcessedAt,
                             TotalProcessed, TotalFailed, UpdatedAt, CreatedAt, BatchCounter)
                            OUTPUT INSERTED.Id
                            VALUES
                            (@serviceName, @checkpointData, @lastProcessedId, @lastProcessedAt,
                             @totalProcessed, @totalFailed, @updatedAt, @createdAt, @batchCounter)";

                var dp = new DynamicParameters();
                dp.Add("@serviceName", checkpoint.ServiceName);
                dp.Add("@checkpointData", checkpoint.CheckpointData);
                dp.Add("@lastProcessedId", checkpoint.LastProcessedId);
                dp.Add("@lastProcessedAt", checkpoint.LastProcessedAt);
                dp.Add("@totalProcessed", checkpoint.TotalProcessed);
                dp.Add("@totalFailed", checkpoint.TotalFailed);
                dp.Add("@updatedAt", checkpoint.UpdatedAt);
                dp.Add("@createdAt", checkpoint.CreatedAt);
                dp.Add("@batchCounter", checkpoint.BatchCounter);

                var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
                var insertedId = await Conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);

                return insertedId;
            }
        }

        public async Task DeleteByServiceNameAsync(string serviceName, CancellationToken ct = default)
        {
            var sql = @"DELETE FROM MigrationCheckpoint WHERE ServiceName = @serviceName";

            var dp = new DynamicParameters();
            dp.Add("@serviceName", serviceName);

            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }
    }
}
