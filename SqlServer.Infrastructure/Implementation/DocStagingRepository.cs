using Alfresco.Contracts.Oracle.Models;
using Dapper;
using SqlServer.Abstraction.Interfaces;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace SqlServer.Infrastructure.Implementation
{
    public class DocStagingRepository : SqlServerRepository<DocStaging, long>, IDocStagingRepository
    {
        public DocStagingRepository(IUnitOfWork uow) : base(uow)
        {
        }

        public async Task FailAsync(long id, string error, CancellationToken ct)
        {
            var sql = @"UPDATE DocStaging
                        SET status = 'ERROR',
                            RETRYCOUNT = ISNULL(RETRYCOUNT, 0) + 1,
                            ERRORMSG = @error,
                            updatedAt = SYSDATETIMEOFFSET()
                        WHERE id = @id";

            var dp = new DynamicParameters();

            error = error.Substring(0, Math.Min(4000, error.Length)); // SQL Server VARCHAR/NVARCHAR limit

            dp.Add("@error", error);
            dp.Add("@id", id);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);

        }

        public async Task SetStatusAsync(long id, string status, string? error, CancellationToken ct)
        {
            var sql = @"UPDATE DocStaging
                        SET status = @status,
                            ERRORMSG = @error,
                            updatedAt = SYSDATETIMEOFFSET()
                        WHERE id = @id";

            var dp = new DynamicParameters();
            if (error == null) error = "";
            error = error?.Substring(0, Math.Min(4000, error.Length)); // SQL Server VARCHAR/NVARCHAR limit

            dp.Add("@status", status);
            dp.Add("@error", error);
            dp.Add("@id", id);
            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<DocStaging>> TakeReadyForProcessingAsync(int take, CancellationToken ct)
        {
            // SQL Server uses WITH (ROWLOCK, UPDLOCK, READPAST) for similar behavior to Oracle's FOR UPDATE SKIP LOCKED
            var sql = @"SELECT TOP (@take) *
                        FROM DocStaging WITH (ROWLOCK, UPDLOCK, READPAST)
                        WHERE status = 'READY'";

            var dp = new DynamicParameters();
            dp.Add("@take", take);
            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

            var res = await Conn.QueryAsync<DocStaging>(cmd).ConfigureAwait(false);

            return res.AsList();
        }

        public async Task<long> CountReadyForProcessingAsync(CancellationToken ct)
        {
            var sql = @"SELECT COUNT(*) FROM DocStaging
                        WHERE status = 'READY'";

            var cmd = new CommandDefinition(sql, transaction: Tx, cancellationToken: ct);

            var count = await Conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);

            return count;
        }
    }
}
