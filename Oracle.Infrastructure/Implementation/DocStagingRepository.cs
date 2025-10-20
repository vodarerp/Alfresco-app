using Alfresco.Contracts.Oracle.Models;
using Dapper;
using Oracle.Abstraction.Interfaces;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace Oracle.Infrastructure.Implementation
{
    public class DocStagingRepository : OracleRepository<DocStaging, long>, IDocStagingRepository
    {
        public DocStagingRepository(IUnitOfWork uow) : base(uow)
        {
        }

        public async Task FailAsync(long id, string error, CancellationToken ct)
        {
            var sql = @"update DocStaging
                        set status = 'ERROR',
                            RETRYCOUNT = NVL(RETRYCOUNT, 0) + 1,
                            ERRORMSG = :error,
                            updatedAt = SYSTIMESTAMP
                        where id = :id";

            var dp = new DynamicParameters();

            error = error.Substring(0, Math.Min(4000, error.Length)); // Oracle VARCHAR2 limit

            dp.Add(":error", error);
            dp.Add(":id", id);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);

        }

        public async Task SetStatusAsync(long id, string status, string? error, CancellationToken ct)
        {
            var sql = @"update DocStaging
                        set status = :status,
                            ERRORMSG = :error,
                            updatedAt = SYSTIMESTAMP
                        where id = :id";

            var dp = new DynamicParameters();
            if (error == null) error = "";
            error = error?.Substring(0, Math.Min(4000, error.Length)); // Oracle VARCHAR2 limit

            dp.Add(":status", status);
            dp.Add(":error", error);
            dp.Add(":id", id);
            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<DocStaging>> TakeReadyForProcessingAsync(int take, CancellationToken ct)
        {

            //var sql = @"select * from DocStaging
            //            where status = 'READY'
            //            and ROWNUM <= :take
            //            FOR UPDATE SKIP LOCKED";

            var sql = @"select * from DocStaging
                         where status = 'READY'
                         FETCH FIRST :take ROWS ONLY
                         FOR UPDATE SKIP LOCKED
                         ";

            var dp = new DynamicParameters();
            dp.Add(":take", take);
            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

            var res = await Conn.QueryAsync<DocStaging>(cmd).ConfigureAwait(false);

            return res.AsList();
        }

        public async Task<long> CountReadyForProcessingAsync(CancellationToken ct)
        {
            var sql = @"select COUNT(*) from DocStaging
                         where status = 'READY'";

            var cmd = new CommandDefinition(sql, transaction: Tx, cancellationToken: ct);

            var count = await Conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);

            return count;
        }
    }
}
