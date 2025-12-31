using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Oracle.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Migration.Abstraction.Models;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace SqlServer.Infrastructure.Implementation
{
    public class DocStagingRepository : SqlServerRepository<DocStaging, long>, IDocStagingRepository
    {
        public DocStagingRepository(IUnitOfWork uow) : base(uow)
        {
        }

        public async Task FailAsync(long id, string error, CancellationToken ct)
        {
            var sql = @"UPDATE DocStaging
                        SET status = 'ERROR',
                           
                            ERRORMSG = @error,
                            updatedAt = SYSDATETIMEOFFSET()
                        WHERE id = @id";

            var dp = new DynamicParameters();

            error = error.Substring(0, Math.Min(4000, error.Length)); // SQL Server VARCHAR/NVARCHAR limit

            dp.Add("@error", error);
            dp.Add("@id", id);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);

        }

        public async Task SetStatusAsync(long id, string status, string? error, CancellationToken ct)
        {
            var sql = @"UPDATE DocStaging
                        SET status = @status,
                            ERRORMSG = @error,
                            updatedAt = SYSDATETIMEOFFSET()
                        WHERE id = @id";

            var dp = new DynamicParameters();
            if (error == null) error = "";
            error = error?.Substring(0, Math.Min(4000, error.Length)); // SQL Server VARCHAR/NVARCHAR limit

            dp.Add("@status", status);
            dp.Add("@error", error);
            dp.Add("@id", id);
            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<DocStaging>> TakeReadyForProcessingAsync(int take, CancellationToken ct)
        {
            
            var sql = @"
                WITH SelectedDocs AS (
                    SELECT TOP (@take) Id
                    FROM DocStaging WITH (ROWLOCK, UPDLOCK, READPAST)
                    WHERE status = 'READY'
                      AND DestinationFolderId IS NOT NULL
                    ORDER BY Id ASC  -- Deterministic ordering for consistency
                )
                UPDATE d
                SET d.status = 'IN PROGRESS',
                    d.updatedAt = SYSDATETIMEOFFSET()
                OUTPUT
                    INSERTED.Id,
                    INSERTED.NodeId,
                    INSERTED.Status,
                    INSERTED.FromPath,
                    INSERTED.ToPath,
                    INSERTED.DestinationFolderId,
                    INSERTED.DocumentType,
                    INSERTED.IsActive,
                    INSERTED.Version,
                    INSERTED.IsSigned,
                    INSERTED.NewAlfrescoStatus,
                    INSERTED.DocDescription,
                    INSERTED.CoreId,
                    INSERTED.ProductType,
                    INSERTED.ContractNumber,
                    INSERTED.AccountNumbers,
                    INSERTED.CategoryCode,
                    INSERTED.CategoryName,
                    INSERTED.OriginalCreatedAt,
                    INSERTED.DossierDestFolderId,
                    INSERTED.DossierDestFolderIsCreated
                FROM DocStaging d
                INNER JOIN SelectedDocs s ON d.Id = s.Id";

            var dp = new DynamicParameters();
            dp.Add("@take", take);
            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

            var res = await Conn.QueryAsync<DocStaging>(cmd).ConfigureAwait(false);

            return res.AsList();
        }

        public async Task<long> CountReadyForProcessingAsync(CancellationToken ct)
        {
            var sql = @"SELECT COUNT(*) FROM DocStaging
                        WHERE status = 'READY'
                          AND DestinationFolderId IS NOT NULL";

            var cmd = new CommandDefinition(sql, transaction: Tx, cancellationToken: ct);

            var count = await Conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);

            return count;
        }

        public async Task<List<UniqueFolderInfo>> GetUniqueDestinationFoldersAsync(CancellationToken ct = default)
        {
            // Query DocStaging for all DISTINCT combinations of (TargetDossierType, DossierDestFolderId)
            // These represent unique destination folders that need to be created
            // Only include documents that are READY for processing
            // Also fetch first document per folder to extract properties needed for folder creation
            var sql = @"
                WITH FirstDocPerFolder AS (
                    SELECT
                        TargetDossierType,
                        DossierDestFolderId,
                        ProductType,
                        CoreId,
                        OriginalCreatedAt,
                        ROW_NUMBER() OVER (PARTITION BY TargetDossierType, DossierDestFolderId ORDER BY Id) AS RowNum
                    FROM DocStaging
                    WHERE Status = 'READY'
                      AND TargetDossierType IS NOT NULL
                      AND DossierDestFolderId IS NOT NULL
                )
                SELECT
                    TargetDossierType,
                    DossierDestFolderId,
                    ProductType,
                    CoreId,
                    OriginalCreatedAt
                FROM FirstDocPerFolder
                WHERE RowNum = 1
                ORDER BY TargetDossierType, DossierDestFolderId";

            var cmd = new CommandDefinition(sql, transaction: Tx, cancellationToken: ct);

            var results = await Conn.QueryAsync<dynamic>(cmd).ConfigureAwait(false);

            // Map to UniqueFolderInfo
            var folders = results.Select(r => new UniqueFolderInfo
            {
                // TargetDossierType maps to root folder (e.g., 500 → PI folder)
                // This should be resolved by MigrationWorker/FolderPreparationService
                DestinationRootId = GetDossierRootId((int?)r.TargetDossierType),

                // DossierDestFolderId is the folder path (e.g., "PI102206" or "ACC-12345")
                FolderPath = r.DossierDestFolderId?.ToString() ?? string.Empty,

                // CacheKey for folder lookup
                CacheKey = $"{r.TargetDossierType}_{r.DossierDestFolderId}",

                // Store additional data for property creation
                TipProizvoda = r.ProductType?.ToString(),
                CoreId = r.CoreId?.ToString(),
                CreationDate = r.OriginalCreatedAt as DateTime?,
                TargetDossierType = r.TargetDossierType as int?,

                // Properties will be built in DocumentResolver when folder is created
                Properties = null
            }).ToList();

            return folders;
        }

        private string GetDossierRootId(int? targetDossierType)
        {
            // Map TargetDossierType (DossierType enum) to root folder IDs in Alfresco
            // This mapping should match the DossierType enum values
            // Actual folder names: DOSSIERS-LE, DOSSIERS-PI, DOSSIERS-D, DOSSIERS-ACC
            // TODO: This should be configured externally (e.g., from appsettings.json)
            return targetDossierType switch
            {
                300 => "DOSSIERS-ACC",  // AccountPackage (ACC)
                400 => "DOSSIERS-LE",   // ClientPL (Legal Entity)
                500 => "DOSSIERS-PI",   // ClientFL (Physical Individual)
                700 => "DOSSIERS-D",    // Deposit
                _ => throw new InvalidOperationException($"Unknown TargetDossierType: {targetDossierType}")
            };
        }

        public async Task<int> UpdateDestinationFolderIdAsync(
            string dossierDestFolderId,
            string alfrescoFolderId,
            bool isCreated,
            CancellationToken ct = default)
        {
            // Updates DestinationFolderId after folder is created
            // Status remains READY - documents will be picked up by MoveService
            var sql = @"
                UPDATE DocStaging
                SET DestinationFolderId = @AlfrescoFolderId,
                    DossierDestFolderIsCreated = @IsCreated,
                    UpdatedAt = GETUTCDATE()
                WHERE DossierDestFolderId = @DossierDestFolderId";

            var parameters = new DynamicParameters();
            parameters.Add("@DossierDestFolderId", dossierDestFolderId);
            parameters.Add("@AlfrescoFolderId", alfrescoFolderId);
            parameters.Add("@IsCreated", isCreated);

            var cmd = new CommandDefinition(sql, parameters, Tx, cancellationToken: ct);

            var rowsAffected = await Conn.ExecuteAsync(cmd).ConfigureAwait(false);

            return rowsAffected;
        }
    }
}
