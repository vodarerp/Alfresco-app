using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.SqlServer;
using Dapper;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SqlServer.Infrastructure.Implementation
{
    /// <summary>
    /// Repository implementacija za KdpExportResult tabelu
    /// </summary>
    public class KdpExportResultRepository : SqlServerRepository<KdpExportResult, long>, IKdpExportResultRepository
    {
        public KdpExportResultRepository(IUnitOfWork uow, SqlServerOptions sqlServerOptions) : base(uow, sqlServerOptions)
        {
        }

        /// <summary>
        /// Poziva stored procedure sp_ProcessKdpDocuments
        /// Vraća tuple sa (totalCandidates, totalDocuments)
        /// </summary>
        public async Task<(int totalCandidates, int totalDocuments)> ProcessKdpDocumentsAsync(CancellationToken ct = default)
        {
            var cmd = new CommandDefinition(
                commandText: "sp_ProcessKdpDocuments",
                commandType: CommandType.StoredProcedure,
                transaction: Tx,
                commandTimeout: _commandTimeoutSeconds,
                cancellationToken: ct
            );

            using var reader = await Conn.ExecuteReaderAsync(cmd).ConfigureAwait(false);

            //if (await reader.ReadAsync())
            //{
            //    var totalCandidates = reader.GetInt32(0);
            //    var totalDocuments = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);

            //    return (totalCandidates, totalDocuments);
            //}

            return (0, 0);
        }

        /// <summary>
        /// Vraća sve rezultate za eksport
        /// </summary>
        public async Task<IReadOnlyList<KdpExportResult>> GetAllExportResultsAsync(CancellationToken ct = default)
        {
            var sql = @"SELECT * FROM KdpExportResult
                        ORDER BY AccFolderName, DatumKreiranjaDokumenta DESC";

            var cmd = new CommandDefinition(sql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);

            var results = await Conn.QueryAsync<KdpExportResult>(cmd).ConfigureAwait(false);

            return results.AsList();
        }

        /// <summary>
        /// Vraća broj rezultata u tabeli
        /// </summary>
        public async Task<long> CountAsync(CancellationToken ct = default)
        {
            var sql = "SELECT COUNT(*) FROM KdpExportResult";
            var cmd = new CommandDefinition(sql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var count = await Conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);
            return count;
        }

        /// <summary>
        /// Briše sve zapise iz KdpExportResult tabele
        /// </summary>
        public async Task ClearResultsAsync(CancellationToken ct = default)
        {
            var sql = "TRUNCATE TABLE KdpExportResult";
            var cmd = new CommandDefinition(sql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        /// <summary>
        /// Pretrazuje rezultate po filterima sa paginacijom
        /// </summary>
        public async Task<(IReadOnlyList<KdpExportResult> Results, int TotalCount)> SearchAsync(
            string? coreId = null,
            string? oldStatus = null,
            string? newStatus = null,
            int? action = null,
            int skip = 0,
            int take = 25,
            CancellationToken ct = default)
        {
            var conditions = new List<string>();
            var parameters = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(coreId))
            {
                conditions.Add("KlijentskiBroj LIKE @CoreId");
                parameters.Add("@CoreId", $"%{coreId}%");
            }

            if (!string.IsNullOrWhiteSpace(oldStatus))
            {
                conditions.Add("OldDocumentStatus LIKE @OldStatus");
                parameters.Add("@OldStatus", $"%{oldStatus}%");
            }

            if (!string.IsNullOrWhiteSpace(newStatus))
            {
                conditions.Add("NewDocumentStatus LIKE @NewStatus");
                parameters.Add("@NewStatus", $"%{newStatus}%");
            }

            if (action.HasValue)
            {
                conditions.Add("Action = @Action");
                parameters.Add("@Action", action.Value);
            }

            var whereClause = conditions.Count > 0
                ? "WHERE " + string.Join(" AND ", conditions)
                : string.Empty;

            // Count query
            var countSql = $"SELECT COUNT(*) FROM KdpExportResult {whereClause}";
            var countCmd = new CommandDefinition(countSql, parameters, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var totalCount = await Conn.ExecuteScalarAsync<int>(countCmd).ConfigureAwait(false);

            // Data query with pagination
            var dataSql = $@"SELECT * FROM KdpExportResult
                            {whereClause}
                            ORDER BY Id DESC
                            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

            parameters.Add("@Skip", skip);
            parameters.Add("@Take", take);

            var dataCmd = new CommandDefinition(dataSql, parameters, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var results = await Conn.QueryAsync<KdpExportResult>(dataCmd).ConfigureAwait(false);

            return (results.AsList(), totalCount);
        }

        /// <summary>
        /// Vraća batch neažuriranih dokumenata za procesiranje
        /// </summary>
        public async Task<IReadOnlyList<KdpExportResult>> GetUnupdatedBatchAsync(int batchSize, CancellationToken ct = default)
        {
            //var sql = @"SELECT TOP (@BatchSize) *
            //            FROM KdpExportResult
            //            where 1 = 1
            //              and isnull(isUpdated, 0) = 0
            //              and isnull(Action, 0) > 0
            //              and isnull(Izuzetak, 0) = 0
            //              and 1 = case
            //                        when Action = 1 and isnull(ListaRacunaUpdated, 0) = 1 then 1
            //                        else 0
            //                      end
            //            ORDER BY Id";

            var sql = @"SELECT TOP (@BatchSize) *
                        FROM KdpExportResult
                        where 1 = 1
                          and isnull(isUpdated, 0) = 0
                          and isnull(action, 0) > 0
                          and isnull(izuzetak, 0) = 0                          
                        ORDER BY Id";

            var cmd = new CommandDefinition(sql, new { BatchSize = batchSize }, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var results = await Conn.QueryAsync<KdpExportResult>(cmd).ConfigureAwait(false);

            return results.AsList();
        }

        /// <summary>
        /// Označava batch dokumenata kao ažurirane
        /// </summary>
        public async Task MarkBatchAsUpdatedAsync(IEnumerable<long> documentIds, Dictionary<long, string>? updateMessages = null, CancellationToken ct = default)
        {
            var idList = documentIds.ToList();
            if (!idList.Any())
                return;

            // Ako imamo poruke za svaki dokument, ažuriramo pojedinačno
            if (updateMessages != null && updateMessages.Count > 0)
            {
                foreach (var id in idList)
                {
                    var message = updateMessages.TryGetValue(id, out var msg) ? msg : "OK";
                    var sql = @"UPDATE KdpExportResult
                                SET IsUpdated = 1,
                                    UpdatedDate = @UpdatedDate,
                                    UpdateMessage = @Message
                                WHERE Id = @Id";

                    var cmd = new CommandDefinition(sql,
                        new { Id = id, UpdatedDate = DateTime.Now, Message = message },
                        transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
                    await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
                }
            }
            else
            {
                // Bulk update bez pojedinačnih poruka
                var sql = @"UPDATE KdpExportResult
                            SET IsUpdated = 1,
                                UpdatedDate = @UpdatedDate,
                                UpdateMessage = 'OK'
                            WHERE Id IN @Ids";

                var cmd = new CommandDefinition(sql,
                    new { Ids = idList, UpdatedDate = DateTime.Now },
                    transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
                await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Vraća broj neažuriranih dokumenata
        /// </summary>
        public async Task<long> CountUnupdatedAsync(CancellationToken ct = default)
        {
            var sql = "SELECT COUNT(*) FROM KdpExportResult WHERE isnull(IsUpdated,0) = 0";
            var cmd = new CommandDefinition(sql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var count = await Conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);
            return count;
        }

        /// <summary>
        /// Ažurira pojedinačni dokument sa rezultatom update-a
        /// </summary>
        public async Task UpdateDocumentStatusAsync(long id, bool isUpdated, string? message, CancellationToken ct = default)
        {
            var sql = @"UPDATE KdpExportResult
                        SET IsUpdated = @IsUpdated,
                            UpdatedDate = @UpdatedDate,
                            UpdateMessage = @Message
                        WHERE Id = @Id";

            var cmd = new CommandDefinition(sql,
                new { Id = id, IsUpdated = isUpdated, UpdatedDate = DateTime.Now, Message = message },
                transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }
    }
}
