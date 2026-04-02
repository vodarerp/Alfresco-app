using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.SqlServer;
using Dapper;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SqlServer.Infrastructure.Implementation
{
    public class PreviewLoadCheckpointRepository : SqlServerRepository<PreviewLoadCheckpoint, long>, IPreviewLoadCheckpointRepository
    {
        public PreviewLoadCheckpointRepository(IUnitOfWork uow, SqlServerOptions sqlServerOptions) : base(uow, sqlServerOptions)
        {
        }

        public async Task<long> GetFetchedCountAsync(string folderType, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT ISNULL(TotalFetched, 0)
                FROM PreviewLoadCheckpoint
                WHERE FolderType = @FolderType";

            var dp = new DynamicParameters();
            dp.Add("@FolderType", folderType);
            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            return await Conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);
        }

        public async Task UpsertAsync(string folderType, long totalFetched, CancellationToken ct = default)
        {
            const string sql = @"
                MERGE INTO PreviewLoadCheckpoint AS target
                USING (SELECT @FolderType AS FolderType) AS source
                ON target.FolderType = source.FolderType
                WHEN MATCHED THEN
                    UPDATE SET TotalFetched = @TotalFetched, UpdatedAt = SYSDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (FolderType, TotalFetched, UpdatedAt)
                    VALUES (@FolderType, @TotalFetched, SYSDATETIME());";

            var dp = new DynamicParameters();
            dp.Add("@FolderType", folderType);
            dp.Add("@TotalFetched", totalFetched);
            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task ResetAllAsync(CancellationToken ct = default)
        {
            const string sql = "DELETE FROM PreviewLoadCheckpoint";
            var cmd = new CommandDefinition(sql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }
    }
}
