using Alfresco.Contracts.Enums;
using Dapper;
using Oracle.Apstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Extensions.Oracle
{
    public static class RepositoryExtensions
    {
        #region Document extension
        public static async Task BatchSetDocumentStatusAsync(this IDocStagingRepository repo, IDbConnection conn, IDbTransaction tran, IEnumerable<(long DocId, string Status, string? Error)> values, CancellationToken ct = default)
        {
            if (!values.Any()) return;

            var sql = @"update DocStaging
                        SET Status = :status, 
                            Error = :error
                            UpdatedAt = SYSTIMESTAMP
                        WHERE Id = :id";

            var parameters = values.Select(o => new
            {
                id = o.DocId,
                status = o.Status,
                error = o.Error
            }).ToList();

            var cmd = new CommandDefinition(sql, parameters, transaction: tran, cancellationToken: ct);

            await conn.ExecuteAsync(cmd).ConfigureAwait(false);

        }

        public static async Task<Dictionary<string, long>> GetDocumentStatisticAsync(this IDocStagingRepository repo, IDbConnection conn, IDbTransaction tran, CancellationToken ct = default)
        {
            var sql = @"Select Status, Count(*)
                        From DocStaging
                        Group By Status";

            var cmd = new CommandDefinition(sql, transaction: tran, cancellationToken: ct);
            var result = await conn.QueryAsync<(string Status, long Count)>(cmd).ConfigureAwait(false);

            return result.ToDictionary(o => o.Status, o => o.Count);

        }

        public static async Task<int> ResetStuckDocumentsAsync(this IDocStagingRepository repo, IDbConnection conn, IDbTransaction tran, TimeSpan timeSpan, CancellationToken ct = default)
        {

            var sql = @$"Update DocStaging
                        Set Status = '{MigrationStatus.Ready}',
                            Error = 'Reset from stuck state',
                            RetryCount = NVL(RetryCount, 0) + 1,
                            UpdatedAt = SYSTIMESTAMP
                        Where Status = '{MigrationStatus.InProgress}' 
                          and UpdatedAt < SYSTIMESTAMP - INTERVAL ':timespan' SECOND";
            var parameters = new
            {
                timespan = (int)timeSpan.TotalMinutes
            };
            var cmd = new CommandDefinition(sql, parameters, transaction: tran, cancellationToken: ct);
            return await conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        #endregion

        #region Folder Extensinon
        public static async Task BatchSetFolderStatusAsync(this IFolderStagingRepository repo, IDbConnection conn, IDbTransaction tran, IEnumerable<(long DocId, string Status, string? Error)> values, CancellationToken ct = default)
        {
            if (!values.Any()) return;

            var sql = @"update FolderStaging
                        SET Status = :status, 
                            Error = :error
                            UpdatedAt = SYSTIMESTAMP
                        WHERE Id = :id";

            var parameters = values.Select(o => new
            {
                id = o.DocId,
                status = o.Status,
                error = o.Error
            }).ToList();

            var cmd = new CommandDefinition(sql, parameters, transaction: tran, cancellationToken: ct);

            await conn.ExecuteAsync(cmd).ConfigureAwait(false);

        }

        public static async Task<Dictionary<string, long>> GetFolderStatisticAsync(this IFolderStagingRepository repo, IDbConnection conn, IDbTransaction tran, CancellationToken ct = default)
        {
            var sql = @"Select Status, Count(*)
                        From FolderStaging
                        Group By Status";

            var cmd = new CommandDefinition(sql, transaction: tran, cancellationToken: ct);
            var result = await conn.QueryAsync<(string Status, long Count)>(cmd).ConfigureAwait(false);

            return result.ToDictionary(o => o.Status, o => o.Count);

        }

        public static async Task<int> ResetStuckFolderAsync(this IFolderStagingRepository repo, IDbConnection conn, IDbTransaction tran, TimeSpan timeSpan, CancellationToken ct = default)
        {

            var sql = @$"Update FolderStaging
                        Set Status = '{MigrationStatus.Ready}',
                            Error = 'Reset from stuck state',
                            RetryCount = NVL(RetryCount, 0) + 1,
                            UpdatedAt = SYSTIMESTAMP
                        Where Status = '{MigrationStatus.InProgress}' 
                          and UpdatedAt < SYSTIMESTAMP - INTERVAL ':timespan' SECOND";
            var parameters = new
            {
                timespan = (int)timeSpan.TotalMinutes
            };
            var cmd = new CommandDefinition(sql, parameters, transaction: tran, cancellationToken: ct);
            return await conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }
        #endregion
    }
}
