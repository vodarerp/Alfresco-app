using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Oracle.Models;
using Dapper;
using SqlServer.Abstraction.Interfaces;
using Microsoft.Data.SqlClient;

namespace SqlServer.Infrastructure.Implementation
{
    public class FolderStagingRepository : SqlServerRepository<FolderStaging, long>, IFolderStagingRepository
    {
        public FolderStagingRepository(IUnitOfWork uow) : base(uow)
        {
        }

        public async Task FailAsync(long id, string error, CancellationToken ct)
        {
            var sql = @"UPDATE FolderStaging
                        SET status = 'ERROR',
                           
                            error = @error,
                            updatedAt = SYSDATETIMEOFFSET()
                        WHERE id = @id";

            var dp = new DynamicParameters();

            error = error.Length > 4000 ? error[..4000] : error; // SQL Server VARCHAR/NVARCHAR limit

            dp.Add("@error", error);
            dp.Add("@id", id);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task SetStatusAsync(long id, string status, string? error, CancellationToken ct)
        {
            try
            {
                var sql = @"UPDATE FolderStaging
                        SET status = @status,
                            error = @error,
                            updatedAt = SYSDATETIMEOFFSET()
                        WHERE id = @id";

                var dp = new DynamicParameters();

                error = error?.Length > 4000 ? error[..4000] : error; // SQL Server VARCHAR/NVARCHAR limit

                if (error == null) error = "";

                dp.Add("@status", status);
                dp.Add("@error", error);
                dp.Add("@id", id);
                var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

                await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Exception handling - transaction managed by UnitOfWork
            }
        }

        public async Task<IReadOnlyList<FolderStaging>> TakeReadyForProcessingAsync(int take, CancellationToken ct)
        {
            // SQL Server uses WITH (ROWLOCK, UPDLOCK, READPAST) for similar behavior to Oracle's FOR UPDATE SKIP LOCKED
            var sql = @$"SELECT TOP (@take) *
                         FROM FolderStaging WITH (ROWLOCK, UPDLOCK, READPAST)
                         WHERE status = '{MigrationStatus.Ready.ToDbString()}'";

            var dp = new DynamicParameters();
            dp.Add("@take", take);
            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

            var res = await Conn.QueryAsync<FolderStaging>(cmd).ConfigureAwait(false);

            return res.AsList();
        }

        public async Task<long> CountReadyForProcessingAsync(CancellationToken ct)
        {
            var sql = @$"SELECT COUNT(*) FROM FolderStaging
                         WHERE status = '{MigrationStatus.Ready.ToDbString()}'";

            var cmd = new CommandDefinition(sql, transaction: Tx, cancellationToken: ct);

            var count = await Conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);

            return count;
        }
    }
}
