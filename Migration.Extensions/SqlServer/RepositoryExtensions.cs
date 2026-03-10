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
    /// <summary>
    /// Status constants for DocStaging table.
    /// Represents the document migration lifecycle.
    /// </summary>
    public static class DocStagingStatus
    {
        public const string READY = "READY";               // DocumentSearch populates - FolderPreparation updates DestinationFolderId, then ready for move
        public const string IN_PROGRESS = "IN_PROGRESS";   // Move service processing - moving document
        public const string DONE = "DONE";                 // Move completed successfully
        public const string ERROR = "ERROR";               // Error occurred during any phase
    }

    public static class RepositoryExtensions
    {
        #region Document extension

        /// <summary>
        /// SQL Server version - uses Dapper batch execution (not array binding like Oracle)
        /// </summary>
        public static async Task BatchSetDocumentStatusAsync_v1(this IDocStagingRepository repo, IDbConnection conn, IDbTransaction tran, IEnumerable<(long DocId, string Status, string? Error)> values, CancellationToken ct = default, int? commandTimeout = null)
        {
            // SQL Server doesn't support array binding like Oracle, so we delegate to Dapper's batch execution
            await BatchSetDocumentStatusAsync(repo, conn, tran, values, ct, commandTimeout).ConfigureAwait(false);
        }

        public static async Task BatchSetDocumentStatusAsync(this IDocStagingRepository repo, IDbConnection conn, IDbTransaction tran, IEnumerable<(long DocId, string Status, string? Error)> values, CancellationToken ct = default, int? commandTimeout = null)
        {
            if (!values.Any()) return;

            var sql = @"UPDATE DocStaging
                        SET Status = @status,
                            ErrorMsg = CONCAT(ISNULL(ErrorMsg, ''), @error),
                            UpdatedAt = GETUTCDATE()
                        WHERE Id = @id";

            var parameters = values.Select(o => new
            {
                status = o.Status,
                error = o.Error,
                id = o.DocId
            }).ToList();

            var cmd = new CommandDefinition(sql, parameters, transaction: tran, commandTimeout: commandTimeout, cancellationToken: ct);

            await conn.ExecuteAsync(cmd).ConfigureAwait(false);

        }

        public static async Task<Dictionary<string, long>> GetDocumentStatisticAsync(this IDocStagingRepository repo, IDbConnection conn, IDbTransaction tran, CancellationToken ct = default, int? commandTimeout = null)
        {
            var sql = @"SELECT Status, COUNT(*) AS Count
                        FROM DocStaging
                        GROUP BY Status";

            var cmd = new CommandDefinition(sql, transaction: tran, commandTimeout: commandTimeout, cancellationToken: ct);
            var result = await conn.QueryAsync<(string Status, long Count)>(cmd).ConfigureAwait(false);

            return result.ToDictionary(o => o.Status, o => o.Count);

        }

        public static async Task<int> ResetStuckDocumentsAsync(this IDocStagingRepository repo, IDbConnection conn, IDbTransaction tran, TimeSpan timeSpan, CancellationToken ct = default, int? commandTimeout = null)
        {
            var totalMinutes = (int)timeSpan.TotalMinutes;

            // SQL Server syntax: DATEADD(MINUTE, -n, GETUTCDATE())
            var sql = $@"UPDATE DocStaging
                        SET Status = '{MigrationStatus.Ready.ToDbString()}',
                            ErrorMsg = 'Reset from stuck IN PROGRESS state',
                            UpdatedAt = GETUTCDATE()
                        WHERE Status = '{MigrationStatus.InProgress.ToDbString()}'
                          AND UpdatedAt < DATEADD(MINUTE, -{totalMinutes}, GETUTCDATE())";

            var cmd = new CommandDefinition(sql, transaction: tran, commandTimeout: commandTimeout, cancellationToken: ct);
            return await conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        #endregion

        #region Folder Extension

        /// <summary>
        /// SQL Server version - uses Dapper batch execution (not array binding like Oracle)
        /// </summary>
        public static async Task BatchSetFolderStatusAsync_v1(this IFolderStagingRepository repo, IDbConnection conn, IDbTransaction tran, IEnumerable<(long FolderId, string Status, string? Error)> values, CancellationToken ct = default, int? commandTimeout = null)
        {
            // SQL Server doesn't support array binding like Oracle, so we delegate to Dapper's batch execution
            await BatchSetFolderStatusAsync(repo, conn, tran, values, ct, commandTimeout).ConfigureAwait(false);
        }

        public static async Task BatchSetFolderStatusAsync(this IFolderStagingRepository repo, IDbConnection conn, IDbTransaction tran, IEnumerable<(long FolderId, string Status, string? Error)> values, CancellationToken ct = default, int? commandTimeout = null)
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

            var cmd = new CommandDefinition(sql, parameters, transaction: tran, commandTimeout: commandTimeout, cancellationToken: ct);

            await conn.ExecuteAsync(cmd).ConfigureAwait(false);

        }

        public static async Task<Dictionary<string, long>> GetFolderStatisticAsync(this IFolderStagingRepository repo, IDbConnection conn, IDbTransaction tran, CancellationToken ct = default, int? commandTimeout = null)
        {
            var sql = @"SELECT Status, COUNT(*) AS Count
                        FROM FolderStaging
                        GROUP BY Status";

            var cmd = new CommandDefinition(sql, transaction: tran, commandTimeout: commandTimeout, cancellationToken: ct);
            var result = await conn.QueryAsync<(string Status, long Count)>(cmd).ConfigureAwait(false);

            return result.ToDictionary(o => o.Status, o => o.Count);

        }

        public static async Task<int> ResetStuckFolderAsync(this IFolderStagingRepository repo, IDbConnection conn, IDbTransaction tran, TimeSpan timeSpan, CancellationToken ct = default, int? commandTimeout = null)
        {
            var totalMinutes = (int)timeSpan.TotalMinutes;

            // SQL Server syntax: DATEADD(MINUTE, -n, GETUTCDATE())
            var sql = $@"UPDATE FolderStaging
                        SET Status = '{MigrationStatus.Ready.ToDbString()}',
                            Error = 'Reset from stuck IN PROGRESS state',

                            UpdatedAt = GETUTCDATE()
                        WHERE Status = '{MigrationStatus.InProgress.ToDbString()}'
                          AND UpdatedAt < DATEADD(MINUTE, -{totalMinutes}, GETUTCDATE())";

            var cmd = new CommandDefinition(sql, transaction: tran, commandTimeout: commandTimeout, cancellationToken: ct);
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
            CancellationToken ct = default,
            int? commandTimeout = null)
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

            var cmd = new CommandDefinition(sql, parameters, transaction: tran, commandTimeout: commandTimeout, cancellationToken: ct);
            await conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        #endregion

        #region Migration Preparation Extensions

        /// <summary>
        /// Resets incomplete documents in DocStaging to their safe restart state:
        /// - PREPARATION → READY (folder prep was in progress when interrupted)
        /// - IN_PROGRESS → PREPARED (move was in progress when interrupted)
        /// - NULL → READY (documents inserted without status)
        /// READY, PREPARED, DONE, ERROR remain unchanged.
        /// </summary>
        public static async Task<int> ResetIncompleteDocumentsAsync(
            this IDocStagingRepository repo,
            IDbConnection conn,
            IDbTransaction tran,
            CancellationToken ct = default,
            int? commandTimeout = null)
        {
            var sql = @"
                UPDATE DocStaging
                SET Status = 'READY',
                    UpdatedAt = GETUTCDATE()
                WHERE Status = 'PREPARED' OR Status IS NULL;

                UPDATE DocStaging
                SET Status = 'PREPARED',
                    UpdatedAt = GETUTCDATE()
                WHERE Status = 'IN_PROGRESS';";

            var cmd = new CommandDefinition(sql, transaction: tran, commandTimeout: commandTimeout, cancellationToken: ct);
            return await conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        /// <summary>
        /// Resets incomplete folders in FolderStaging to their safe restart state:
        /// - IN_PROGRESS → READY (folder creation was in progress when interrupted)
        /// - NULL → READY (folders inserted without status)
        /// READY, DONE*, ERROR remain unchanged.
        /// </summary>
        public static async Task<int> ResetIncompleteFoldersAsync(
            this IFolderStagingRepository repo,
            IDbConnection conn,
            IDbTransaction tran,
            CancellationToken ct = default,
            int? commandTimeout = null)
        {
            var sql = @"
                UPDATE FolderStaging
                SET Status = 'READY',
                    UpdatedAt = GETUTCDATE()
                WHERE Status = 'IN_PROGRESS' OR Status IS NULL";

            var cmd = new CommandDefinition(sql, transaction: tran, commandTimeout: commandTimeout, cancellationToken: ct);
            return await conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets count of resettable documents in DocStaging.
        /// Counts documents that would be affected by ResetIncompleteDocumentsAsync.
        /// </summary>
        public static async Task<long> CountResettableDocumentsAsync(
            this IDocStagingRepository repo,
            IDbConnection conn,
            IDbTransaction tran,
            CancellationToken ct = default,
            int? commandTimeout = null)
        {
            var sql = @"
                SELECT COUNT(*)
                FROM DocStaging
                WHERE Status IN ('PREPARATION', 'IN_PROGRESS')
                   OR Status IS NULL";

            var cmd = new CommandDefinition(sql, transaction: tran, commandTimeout: commandTimeout, cancellationToken: ct);
            return await conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets count of resettable folders in FolderStaging.
        /// Counts folders that would be affected by ResetIncompleteFoldersAsync.
        /// </summary>
        public static async Task<long> CountResettableFoldersAsync(
            this IFolderStagingRepository repo,
            IDbConnection conn,
            IDbTransaction tran,
            CancellationToken ct = default,
            int? commandTimeout = null)
        {
            var sql = @"
                SELECT COUNT(*)
                FROM FolderStaging
                WHERE Status = 'IN_PROGRESS'
                   OR Status IS NULL";

            var cmd = new CommandDefinition(sql, transaction: tran, commandTimeout: commandTimeout, cancellationToken: ct);
            return await conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);
        }

        #endregion
    }
}
