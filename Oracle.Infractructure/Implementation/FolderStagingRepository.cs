using Alfresco.Contracts.Oracle.Models;
using Dapper;
using Oracle.Apstaction.Interfaces;
using Oracle.ManagedDataAccess.Client;

namespace Oracle.Infractructure.Implementation
{
    public class FolderStagingRepository : OracleRepository<FolderStaging, long>, IFolderStagingRepository
    {
        public FolderStagingRepository(OracleConnection connection, OracleTransaction transaction) : base(connection, transaction)
        {
        }

        public async Task FailAsync(long id, string error, CancellationToken ct)
        {
            var sql = @"update FolderStaging
                        set status = 'ERROR',
                            RETRYCOUNT = NVL(RETRYCOUNT, 0) + 1,
                            error = :error,
                            updatedAt = SYSTIMESTAMP
                        where id = :id";

            var dp = new DynamicParameters();

            error = error.Substring(0, Math.Min(4000, error.Length)); // Oracle VARCHAR2 limit

            dp.Add(":error", error);
            dp.Add(":id", id);

            var cmd = new CommandDefinition(sql, dp, _transaction, cancellationToken: ct);

            await _connection.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task SetStatusAsync(long id, string status, string? error, CancellationToken ct)
        {
            var sql = @"update FolderStaging
                        set status = :status,
                            error = :error,
                            updatedAt = SYSTIMESTAMP
                        where id = :id";

            var dp = new DynamicParameters();

            error = error?.Substring(0, Math.Min(4000, error.Length)); // Oracle VARCHAR2 limit

            dp.Add(":status", status);
            dp.Add(":error", error);
            dp.Add(":id", id);
            var cmd = new CommandDefinition(sql, dp, _transaction, cancellationToken: ct);

            await _connection.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<FolderStaging>> TakeReadyForProcessingAsync(int take, CancellationToken ct)
        {
            var sql = @"select * from FolderStaging
                        where status = 'READY'
                        and ROWNUM <= :take
                        FOR UPDATE SKIP LOCKED";

            var dp = new DynamicParameters();
            dp.Add(":take", take);
            var cmd = new CommandDefinition(sql, dp, _transaction, cancellationToken: ct);

            var res = await _connection.QueryAsync<FolderStaging>(cmd).ConfigureAwait(false);

            return res.AsList();
        }
    }
}
