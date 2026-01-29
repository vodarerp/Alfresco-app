using Alfresco.Contracts.Oracle.Models;
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
        public KdpExportResultRepository(IUnitOfWork uow) : base(uow)
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
                commandTimeout: 300, // 5 minuta
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

            var cmd = new CommandDefinition(sql, transaction: Tx, cancellationToken: ct);

            var results = await Conn.QueryAsync<KdpExportResult>(cmd).ConfigureAwait(false);

            return results.AsList();
        }

        /// <summary>
        /// Vraća broj rezultata u tabeli
        /// </summary>
        public async Task<long> CountAsync(CancellationToken ct = default)
        {
            var sql = "SELECT COUNT(*) FROM KdpExportResult";
            var cmd = new CommandDefinition(sql, transaction: Tx, cancellationToken: ct);
            var count = await Conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);
            return count;
        }

        /// <summary>
        /// Briše sve zapise iz KdpExportResult tabele
        /// </summary>
        public async Task ClearResultsAsync(CancellationToken ct = default)
        {
            var sql = "TRUNCATE TABLE KdpExportResult";
            var cmd = new CommandDefinition(sql, transaction: Tx, cancellationToken: ct);
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
            var countCmd = new CommandDefinition(countSql, parameters, transaction: Tx, cancellationToken: ct);
            var totalCount = await Conn.ExecuteScalarAsync<int>(countCmd).ConfigureAwait(false);

            // Data query with pagination
            var dataSql = $@"SELECT * FROM KdpExportResult
                            {whereClause}
                            ORDER BY Id DESC
                            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

            parameters.Add("@Skip", skip);
            parameters.Add("@Take", take);

            var dataCmd = new CommandDefinition(dataSql, parameters, transaction: Tx, cancellationToken: ct);
            var results = await Conn.QueryAsync<KdpExportResult>(dataCmd).ConfigureAwait(false);

            return (results.AsList(), totalCount);
        }
    }
}
