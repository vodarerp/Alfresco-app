using Alfresco.Contracts.Enums;
using Dapper;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Extensions.SqlServer
{
    public static class RepositoryExtensions
    {
        #region Document extension

        /// <summary>
        /// SQL Server version - uses Dapper batch execution (not array binding like Oracle)
        /// </summary>
        public static async Task BatchSetDocumentStatusAsync_v1(this IDocStagingRepository repo, IDbConnection conn, IDbTransaction tran, IEnumerable<(long DocId, string Status, string? Error)> values, CancellationToken ct = default)
        {
            // SQL Server doesn't support array binding like Oracle, so we delegate to Dapper's batch execution
            await BatchSetDocumentStatusAsync(repo, conn, tran, values, ct).ConfigureAwait(false);
        }

        public static async Task BatchSetDocumentStatusAsync(this IDocStagingRepository repo, IDbConnection conn, IDbTransaction tran, IEnumerable<(long DocId, string Status, string? Error)> values, CancellationToken ct = default)
        {
            if (!values.Any()) return;

            var sql = @"UPDATE DocStaging
                        SET Status = @status,
                            ErrorMsg = @error,
                            UpdatedAt = GETUTCDATE()
                        WHERE Id = @id";

            var parameters = values.Select(o => new
            {
                status = o.Status,
                error = o.Error,
                id = o.DocId
            }).ToList();

            var cmd = new CommandDefinition(sql, parameters, transaction: tran, cancellationToken: ct);

            await conn.ExecuteAsync(cmd).ConfigureAwait(false);

        }

        public static async Task<Dictionary<string, long>> GetDocumentStatisticAsync(this IDocStagingRepository repo, IDbConnection conn, IDbTransaction tran, CancellationToken ct = default)
        {
            var sql = @"SELECT Status, COUNT(*) AS Count
                        FROM DocStaging
                        GROUP BY Status";

            var cmd = new CommandDefinition(sql, transaction: tran, cancellationToken: ct);
            var result = await conn.QueryAsync<(string Status, long Count)>(cmd).ConfigureAwait(false);

            return result.ToDictionary(o => o.Status, o => o.Count);

        }

        public static async Task<int> ResetStuckDocumentsAsync(this IDocStagingRepository repo, IDbConnection conn, IDbTransaction tran, TimeSpan timeSpan, CancellationToken ct = default)
        {
            var totalMinutes = (int)timeSpan.TotalMinutes;

            // SQL Server syntax: DATEADD(MINUTE, -n, GETUTCDATE())
            var sql = $@"UPDATE DocStaging
                        SET Status = '{MigrationStatus.Ready.ToDbString()}',
                            ErrorMsg = 'Reset from stuck IN PROGRESS state',
                            UpdatedAt = GETUTCDATE()
                        WHERE Status = '{MigrationStatus.InProgress.ToDbString()}'
                          AND UpdatedAt < DATEADD(MINUTE, -{totalMinutes}, GETUTCDATE())";

            var cmd = new CommandDefinition(sql, transaction: tran, cancellationToken: ct);
            return await conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        #endregion

        #region Folder Extension

        /// <summary>
        /// SQL Server version - uses Dapper batch execution (not array binding like Oracle)
        /// </summary>
        public static async Task BatchSetFolderStatusAsync_v1(this IFolderStagingRepository repo, IDbConnection conn, IDbTransaction tran, IEnumerable<(long FolderId, string Status, string? Error)> values, CancellationToken ct = default)
        {
            // SQL Server doesn't support array binding like Oracle, so we delegate to Dapper's batch execution
            await BatchSetFolderStatusAsync(repo, conn, tran, values, ct).ConfigureAwait(false);
        }

        public static async Task BatchSetFolderStatusAsync(this IFolderStagingRepository repo, IDbConnection conn, IDbTransaction tran, IEnumerable<(long FolderId, string Status, string? Error)> values, CancellationToken ct = default)
        {
            if (!values.Any()) return;

            var sql = @"UPDATE FolderStaging
                        SET Status = @status,
                            Error = @error,
                            UpdatedAt = GETUTCDATE()
                        WHERE Id = @id";

            var parameters = values.Select(o => new
            {
                id = o.FolderId,
                status = o.Status,
                error = o.Error
            }).ToList();

            var cmd = new CommandDefinition(sql, parameters, transaction: tran, cancellationToken: ct);

            await conn.ExecuteAsync(cmd).ConfigureAwait(false);

        }

        public static async Task<Dictionary<string, long>> GetFolderStatisticAsync(this IFolderStagingRepository repo, IDbConnection conn, IDbTransaction tran, CancellationToken ct = default)
        {
            var sql = @"SELECT Status, COUNT(*) AS Count
                        FROM FolderStaging
                        GROUP BY Status";

            var cmd = new CommandDefinition(sql, transaction: tran, cancellationToken: ct);
            var result = await conn.QueryAsync<(string Status, long Count)>(cmd).ConfigureAwait(false);

            return result.ToDictionary(o => o.Status, o => o.Count);

        }

        public static async Task<int> ResetStuckFolderAsync(this IFolderStagingRepository repo, IDbConnection conn, IDbTransaction tran, TimeSpan timeSpan, CancellationToken ct = default)
        {
            var totalMinutes = (int)timeSpan.TotalMinutes;

            // SQL Server syntax: DATEADD(MINUTE, -n, GETUTCDATE())
            var sql = $@"UPDATE FolderStaging
                        SET Status = '{MigrationStatus.Ready.ToDbString()}',
                            Error = 'Reset from stuck IN PROGRESS state',

                            UpdatedAt = GETUTCDATE()
                        WHERE Status = '{MigrationStatus.InProgress.ToDbString()}'
                          AND UpdatedAt < DATEADD(MINUTE, -{totalMinutes}, GETUTCDATE())";

            var cmd = new CommandDefinition(sql, transaction: tran, cancellationToken: ct);
            return await conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        /// <summary>
        /// Batch update FolderStaging records by DestFolderId with status and NodeId.
        /// Used by FolderPreparationService to update folder creation results.
        /// </summary>
        public static async Task BatchUpdateFoldersByDestFolderIdAsync(
            this IFolderStagingRepository repo,
            IDbConnection conn,
            IDbTransaction tran,
            IEnumerable<(string DestFolderId, string Status, string? NodeId)> updates,
            CancellationToken ct = default)
        {
            if (!updates.Any()) return;

            var sql = @"
                UPDATE FolderStaging
                SET Status = @Status,
                    NodeId = @NodeId,
                    UpdatedAt = @UpdatedAt
                WHERE DestFolderId = @DestFolderId";

            var parameters = updates.Select(u => new
            {
                Status = u.Status,
                NodeId = u.NodeId ?? string.Empty,
                UpdatedAt = DateTime.UtcNow,
                DestFolderId = u.DestFolderId
            });

            var cmd = new CommandDefinition(sql, parameters, transaction: tran, cancellationToken: ct);
            await conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        #endregion
    }
}
