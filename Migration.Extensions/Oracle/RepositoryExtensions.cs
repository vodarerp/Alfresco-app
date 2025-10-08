using Alfresco.Contracts.Enums;
using Dapper;
using Oracle.Abstraction.Interfaces;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Migration.Extensions.Oracle
{
    public static class RepositoryExtensions
    {
        #region Document extension

        public static async Task BatchSetDocumentStatusAsync_v1(this IDocStagingRepository repo, IDbConnection conn, IDbTransaction tran, IEnumerable<(long DocId, string Status, string? Error)> values, CancellationToken ct = default)
        {
            if (!values.Any()) return;

            var sql = @"update docStaging
                        SET Status = :status, 
                            ErrorMsg = :error,
                            updatedAt = SYSTIMESTAMP
                        WHERE Id = :id";

            var ids = values.Select(o => o.DocId).ToArray();
            var statuses = values.Select(o => o.Status).ToArray();
            var errors = values.Select(o => o.Error).ToArray();
            var count = values.Count();

            using var cmd = (OracleCommand)conn.CreateCommand();
            cmd.Transaction = (OracleTransaction)tran;
            cmd.BindByName = true;
            cmd.ArrayBindCount = count;
            cmd.CommandText = sql;

            cmd.Parameters.Add(":status", OracleDbType.Varchar2, statuses, ParameterDirection.Input);
            cmd.Parameters.Add(":error", OracleDbType.Varchar2, errors, ParameterDirection.Input);
            cmd.Parameters.Add(":id", OracleDbType.Int64, ids, ParameterDirection.Input);

            var res = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);



        }
        public static async Task BatchSetDocumentStatusAsync(this IDocStagingRepository repo, IDbConnection conn, IDbTransaction tran, IEnumerable<(long DocId, string Status, string? Error)> values, CancellationToken ct = default)
        {
            if (!values.Any()) return;

            var sql = @"update docStaging
                        SET Status = :status, 
                            ErrorMsg = :error,
                            updatedAt = SYSTIMESTAMP
                        WHERE Id = :id";

            var parameters = values.Select(o => new
            {
                status = o.Status,
                error = o.Error,
                id = o.DocId
            }).ToList();

            var cmd = new CommandDefinition(sql, parameters, transaction: tran, cancellationToken: ct);

            //var t = await conn.ExecuteAsync(sql, parameters, transaction: tran, commandType: CommandType.Text);

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
            var totalMinutes = (int)timeSpan.TotalMinutes;

            // Oracle syntax: SYSTIMESTAMP - INTERVAL 'n' MINUTE
            var sql = $@"UPDATE DocStaging
                        SET Status = '{MigrationStatus.Ready.ToDbString()}',
                            ErrorMsg = 'Reset from stuck IN PROGRESS state',
                            RetryCount = NVL(RetryCount, 0) + 1,
                            UpdatedAt = SYSTIMESTAMP
                        WHERE Status = '{MigrationStatus.InProgress.ToDbString()}'
                          AND UpdatedAt < SYSTIMESTAMP - INTERVAL '{totalMinutes}' MINUTE";

            var cmd = new CommandDefinition(sql, transaction: tran, cancellationToken: ct);
            return await conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        #endregion

        #region Folder Extensinon

        public static async Task BatchSetFolderStatusAsync_v1(this IFolderStagingRepository repo, IDbConnection conn, IDbTransaction tran, IEnumerable<(long FolderId, string Status, string? Error)> values, CancellationToken ct = default)
        {
            if (!values.Any()) return;

            var sql = @"update FolderStaging
                        SET Status = :status,
                            Error = :error,
                            UpdatedAt = SYSTIMESTAMP
                        WHERE Id = :id";

            var ids = values.Select(o => o.FolderId).ToArray();
            var statuses = values.Select(o => o.Status).ToArray();
            var errors = values.Select(o => o.Error).ToArray();
            var count = values.Count();

            using var cmd = (OracleCommand)conn.CreateCommand();
            cmd.Transaction = (OracleTransaction)tran;
            cmd.BindByName = true;
            cmd.ArrayBindCount = count;
            cmd.CommandText = sql;

            cmd.Parameters.Add(":status", OracleDbType.Varchar2, statuses, ParameterDirection.Input);
            cmd.Parameters.Add(":error", OracleDbType.Varchar2, errors, ParameterDirection.Input);
            cmd.Parameters.Add(":id", OracleDbType.Int64, ids, ParameterDirection.Input);

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        public static async Task BatchSetFolderStatusAsync(this IFolderStagingRepository repo, IDbConnection conn, IDbTransaction tran, IEnumerable<(long DocId, string Status, string? Error)> values, CancellationToken ct = default)
        {
            if (!values.Any()) return;

            var sql = @"update FolderStaging
                        SET Status = :status,
                            Error = :error,
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
            var totalMinutes = (int)timeSpan.TotalMinutes;

            // Oracle syntax: SYSTIMESTAMP - INTERVAL 'n' MINUTE
            var sql = $@"UPDATE FolderStaging
                        SET Status = '{MigrationStatus.Ready.ToDbString()}',
                            Error = 'Reset from stuck IN PROGRESS state',
                            RetryCount = NVL(RetryCount, 0) + 1,
                            UpdatedAt = SYSTIMESTAMP
                        WHERE Status = '{MigrationStatus.InProgress.ToDbString()}'
                          AND UpdatedAt < SYSTIMESTAMP - INTERVAL '{totalMinutes}' MINUTE";

            var cmd = new CommandDefinition(sql, transaction: tran, cancellationToken: ct);
            return await conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }
        #endregion
    }
}
