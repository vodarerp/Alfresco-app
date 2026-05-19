using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.SqlServer;
using Dapper;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Text.Json;
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

        public async Task<CheckpointState> GetCheckpointStateAsync(string folderType, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT TotalFetched, ProcessedSkipsJson, FailedSkipsJson, LastUpdatedAt
                FROM PreviewLoadCheckpoint
                WHERE FolderType = @FolderType";

            var dp = new DynamicParameters();
            dp.Add("@FolderType", folderType);
            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);

            var row = await Conn.QuerySingleOrDefaultAsync<CheckpointRow>(cmd).ConfigureAwait(false);

            if (row == null)
                return new CheckpointState(0, new HashSet<int>(), new HashSet<int>(), null);

            return new CheckpointState(
                row.TotalFetched,
                Deserialize(row.ProcessedSkipsJson),
                Deserialize(row.FailedSkipsJson),
                row.LastUpdatedAt);
        }

        private sealed class CheckpointRow
        {
            public long TotalFetched { get; set; }
            public string? ProcessedSkipsJson { get; set; }
            public string? FailedSkipsJson { get; set; }
            public DateTime? LastUpdatedAt { get; set; }
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

        public async Task UpsertCheckpointStateAsync(
            string folderType,
            long fetchedCount,
            IEnumerable<int> processedSkips,
            IEnumerable<int> failedSkips,
            CancellationToken ct = default)
        {
            var processedJson = JsonSerializer.Serialize(processedSkips);
            var failedJson = JsonSerializer.Serialize(failedSkips);

            const string sql = @"
                MERGE INTO PreviewLoadCheckpoint AS target
                USING (SELECT @FolderType AS FolderType) AS source
                ON target.FolderType = source.FolderType
                WHEN MATCHED THEN
                    UPDATE SET TotalFetched      = @TotalFetched,
                               ProcessedSkipsJson = @ProcessedSkipsJson,
                               FailedSkipsJson    = @FailedSkipsJson,
                               LastUpdatedAt      = SYSDATETIME(),
                               UpdatedAt          = SYSDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (FolderType, TotalFetched, ProcessedSkipsJson, FailedSkipsJson, LastUpdatedAt, UpdatedAt)
                    VALUES (@FolderType, @TotalFetched, @ProcessedSkipsJson, @FailedSkipsJson, SYSDATETIME(), SYSDATETIME());";

            var dp = new DynamicParameters();
            dp.Add("@FolderType", folderType);
            dp.Add("@TotalFetched", fetchedCount);
            dp.Add("@ProcessedSkipsJson", processedJson);
            dp.Add("@FailedSkipsJson", failedJson);
            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task ResetAllAsync(CancellationToken ct = default)
        {
            const string sql = "DELETE FROM PreviewLoadCheckpoint";
            var cmd = new CommandDefinition(sql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        private static HashSet<int> Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new HashSet<int>();
            try
            {
                var arr = JsonSerializer.Deserialize<int[]>(json);
                return arr != null ? new HashSet<int>(arr) : new HashSet<int>();
            }
            catch
            {
                return new HashSet<int>();
            }
        }
    }
}
