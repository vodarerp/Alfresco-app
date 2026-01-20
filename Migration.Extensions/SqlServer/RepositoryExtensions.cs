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
                            ErrorMsg = CONCAT(ISNULL(ErrorMsg, ''), @error),
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

        #region Migration Preparation Extensions

        /// <summary>
        /// Deletes all incomplete documents from DocStaging table.
        /// Incomplete = Status is NOT 'DONE' and NOT 'ERROR' (includes READY, PREPARATION, PREPARED, IN_PROGRESS, NULL).
        /// ERROR documents are preserved because they may have been physically moved but failed on property update.
        /// Use this before starting migration to ensure clean state.
        /// </summary>
        public static async Task<int> DeleteIncompleteDocumentsAsync(
            this IDocStagingRepository repo,
            IDbConnection conn,
            IDbTransaction tran,
            CancellationToken ct = default)
        {
            // NE brišemo ERROR dokumente jer mogu biti fizički premešteni ali sa neuspešnim update-om propertija
            var sql = @"
                DELETE FROM DocStaging
                WHERE (Status != 'DONE' AND Status != 'ERROR')
                   OR Status IS NULL";

            var cmd = new CommandDefinition(sql, transaction: tran, cancellationToken: ct);
            return await conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes all incomplete folders from FolderStaging table.
        /// Incomplete = Status does NOT start with 'DONE' (includes READY, IN_PROGRESS, ERROR, RESETED, NULL).
        /// Use this before starting migration to ensure clean state.
        /// </summary>
        public static async Task<int> DeleteIncompleteFoldersAsync(
            this IFolderStagingRepository repo,
            IDbConnection conn,
            IDbTransaction tran,
            CancellationToken ct = default)
        {
            var sql = @"
                DELETE FROM FolderStaging
                WHERE Status NOT LIKE 'DONE%'
                   OR Status IS NULL";

            var cmd = new CommandDefinition(sql, transaction: tran, cancellationToken: ct);
            return await conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets count of incomplete documents in DocStaging.
        /// Excludes ERROR documents (same logic as DeleteIncompleteDocumentsAsync).
        /// Useful for logging before deletion.
        /// </summary>
        public static async Task<long> CountIncompleteDocumentsAsync(
            this IDocStagingRepository repo,
            IDbConnection conn,
            IDbTransaction tran,
            CancellationToken ct = default)
        {
            // Konzistentno sa DeleteIncompleteDocumentsAsync - NE brojimo ERROR dokumente
            var sql = @"
                SELECT COUNT(*)
                FROM DocStaging
                WHERE (Status != 'DONE' AND Status != 'ERROR')
                   OR Status IS NULL";

            var cmd = new CommandDefinition(sql, transaction: tran, cancellationToken: ct);
            return await conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets count of incomplete folders in FolderStaging.
        /// Useful for logging before deletion.
        /// </summary>
        public static async Task<long> CountIncompleteFoldersAsync(
            this IFolderStagingRepository repo,
            IDbConnection conn,
            IDbTransaction tran,
            CancellationToken ct = default)
        {
            var sql = @"
                SELECT COUNT(*)
                FROM FolderStaging
                WHERE Status NOT LIKE 'DONE%'
                   OR Status IS NULL";

            var cmd = new CommandDefinition(sql, transaction: tran, cancellationToken: ct);
            return await conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);
        }

        #endregion
    }
}
