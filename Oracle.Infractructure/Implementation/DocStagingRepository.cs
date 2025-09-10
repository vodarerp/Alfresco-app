using Alfresco.Contracts.Oracle.Models;
using Dapper;
using Oracle.Apstaction.Interfaces;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace Oracle.Infractructure.Implementation
{
    public class DocStagingRepository : OracleRepository<DocStaging, long>, IDocStagingRepository
    {
        public DocStagingRepository(OracleConnection connection, OracleTransaction transaction) : base(connection, transaction)
        {
        }

        public async Task FailAsync(long id, string error, CancellationToken ct)
        {
            var sql = @"update DocStaging
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
            var sql = @"update DocStaging
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

        public async Task<IReadOnlyList<DocStaging>> TakeReadyForProcessingAsync(int take, CancellationToken ct)
        {            

            var sql = @"select * from DocStaging
                        where status = 'READY'
                        and ROWNUM <= :take
                        FOR UPDATE SKIP LOCKED";

            var dp = new DynamicParameters();
            dp.Add(":take", take);
            var cmd = new CommandDefinition(sql, dp, _transaction, cancellationToken: ct);

            var res = await _connection.QueryAsync<DocStaging>(cmd).ConfigureAwait(false);

            return res.AsList();
        }
    }
}
