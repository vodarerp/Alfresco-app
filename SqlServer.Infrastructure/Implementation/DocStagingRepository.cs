using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.SqlServer;
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
        public DocStagingRepository(IUnitOfWork uow, SqlServerOptions sqlServerOptions) : base(uow, sqlServerOptions)
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

            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);

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
            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);

            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<DocStaging>> TakeReadyForProcessingAsync(int take, CancellationToken ct)
        {
            // ✅ Takes documents with Status='PREPARED' (folder already created by FolderPreparationService)
            // Updates them to 'IN PROGRESS' for move operation
            var sql = @"
                WITH SelectedDocs AS (
                    SELECT TOP (@take) Id
                    FROM DocStaging WITH (ROWLOCK, UPDLOCK, READPAST)
                    WHERE status = 'PREPARED'
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
                    INSERTED.DossierDestFolderIsCreated,
                    INSERTED.FinalDocumentType,
                    INSERTED.NewDocumentCode,
                    INSERTED.NewDocumentName
                FROM DocStaging d
                INNER JOIN SelectedDocs s ON d.Id = s.Id";

            var dp = new DynamicParameters();
            dp.Add("@take", take);
            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);

            var res = await Conn.QueryAsync<DocStaging>(cmd).ConfigureAwait(false);

            return res.AsList();
        }

        public async Task<long> CountReadyForProcessingAsync(CancellationToken ct)
        {
            // ✅ Counts documents with Status='PREPARED' (ready for move operation)
            var sql = @"SELECT COUNT(*) FROM DocStaging
                        WHERE status = 'PREPARED'
                          AND DestinationFolderId IS NOT NULL";

            var cmd = new CommandDefinition(sql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);

            var count = await Conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);

            return count;
        }

        public async Task<List<UniqueFolderInfo>> GetUniqueDestinationFoldersAsync(CancellationToken ct = default)
        {
            // ✅ THREAD-SAFE: CTE + UPDATE + OUTPUT pattern
            // Selects all documents that belong to unique folders, then updates them to PREPARED
            // Returns ALL updated documents, then filters in C# to get only first per folder
            var sql = @"WITH Ranked AS (
      SELECT
          d.TargetDossierType,
          d.DossierDestFolderId,
          ROW_NUMBER() OVER (
              PARTITION BY d.TargetDossierType, d.DossierDestFolderId
              ORDER BY d.Id ASC
          ) AS rn
      FROM DocStaging d WITH (ROWLOCK, UPDLOCK, READPAST)
      WHERE d.Status = 'READY'
        AND d.TargetDossierType IS NOT NULL
        AND d.DossierDestFolderId IS NOT NULL
  ),
  SelectedFolders AS (
      SELECT TargetDossierType, DossierDestFolderId
      FROM Ranked
      WHERE rn = 1  -- Samo unique folderi
  )
  UPDATE d
  SET d.Status = 'PREPARED',
      d.UpdatedAt = SYSDATETIMEOFFSET()
  OUTPUT
      INSERTED.TargetDossierType,
      INSERTED.DossierDestFolderId,
      INSERTED.ProductType,
      INSERTED.CoreId,
      INSERTED.OriginalCreatedAt
  FROM DocStaging d
  INNER JOIN SelectedFolders sf
      ON d.TargetDossierType = sf.TargetDossierType
      AND d.DossierDestFolderId = sf.DossierDestFolderId
  WHERE d.Status = 'READY'";


            var cmd = new CommandDefinition(sql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);

            var results = await Conn.QueryAsync<dynamic>(cmd).ConfigureAwait(false);

            // Filter to only first document per folder (GroupBy in C#)
            var folders = results
                .GroupBy(r => new { TargetDossierType = (int?)r.TargetDossierType, DossierDestFolderId = r.DossierDestFolderId?.ToString() })
                .Select(g => g.OrderBy(r => r.Id).First())
                .Select(r => new UniqueFolderInfo
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

        public async Task<int> InsertManyIgnoreDuplicatesAsync(IEnumerable<DocStaging> documents, CancellationToken ct)
        {
            var listDocs = documents.ToList();
            if (listDocs.Count == 0) return 0;

            int totalInserted = 0;
            const int batchSize = 100;

            for (int offset = 0; offset < listDocs.Count; offset += batchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batch = listDocs.Skip(offset).Take(batchSize).ToList();

                foreach (var doc in batch)
                {
                    var sql = @"
                        MERGE INTO DocStaging AS target
                        USING (SELECT @NodeId AS NodeId) AS source
                        ON target.NodeId = source.NodeId
                        WHEN NOT MATCHED THEN
                            INSERT (NodeId, Name, IsFolder, IsFile, NodeType, ParentId, FromPath, ToPath,
                                    Status, ErrorMsg, CreatedAt, UpdatedAt, DocumentType, DocumentTypeMigration,
                                    Source, IsActive, CategoryCode, CategoryName, OriginalCreatedAt,
                                    ContractNumber, CoreId, Version, AccountNumbers, RequiresTypeTransformation,
                                    FinalDocumentType, IsSigned, DutOfferId, ProductType,
                                    OriginalDocumentName, NewDocumentName, OriginalDocumentCode, NewDocumentCode,
                                    TipDosijea, TargetDossierType, ClientSegment, OldAlfrescoStatus, NewAlfrescoStatus,
                                    WillReceiveMigrationSuffix, CodeWillChange, DocDescription,
                                    DossierDestFolderId, DestinationFolderId, DossierDestFolderIsCreated)
                            VALUES (@NodeId, @Name, @IsFolder, @IsFile, @NodeType, @ParentId, @FromPath, @ToPath,
                                    @Status, @ErrorMsg, @CreatedAt, @UpdatedAt, @DocumentType, @DocumentTypeMigration,
                                    @Source, @IsActive, @CategoryCode, @CategoryName, @OriginalCreatedAt,
                                    @ContractNumber, @CoreId, @Version, @AccountNumbers, @RequiresTypeTransformation,
                                    @FinalDocumentType, @IsSigned, @DutOfferId, @ProductType,
                                    @OriginalDocumentName, @NewDocumentName, @OriginalDocumentCode, @NewDocumentCode,
                                    @TipDosijea, @TargetDossierType, @ClientSegment, @OldAlfrescoStatus, @NewAlfrescoStatus,
                                    @WillReceiveMigrationSuffix, @CodeWillChange, @DocDescription,
                                    @DossierDestFolderId, @DestinationFolderId, @DossierDestFolderIsCreated);";

                    var dp = new DynamicParameters();
                    dp.Add("@NodeId", doc.NodeId);
                    dp.Add("@Name", doc.Name);
                    dp.Add("@IsFolder", doc.IsFolder);
                    dp.Add("@IsFile", doc.IsFile);
                    dp.Add("@NodeType", doc.NodeType);
                    dp.Add("@ParentId", doc.ParentId);
                    dp.Add("@FromPath", doc.FromPath);
                    dp.Add("@ToPath", doc.ToPath);
                    dp.Add("@Status", doc.Status);
                    dp.Add("@ErrorMsg", doc.ErrorMsg);
                    dp.Add("@CreatedAt", doc.CreatedAt);
                    dp.Add("@UpdatedAt", doc.UpdatedAt);
                    dp.Add("@DocumentType", doc.DocumentType);
                    dp.Add("@DocumentTypeMigration", doc.DocumentTypeMigration);
                    dp.Add("@Source", doc.Source);
                    dp.Add("@IsActive", doc.IsActive);
                    dp.Add("@CategoryCode", doc.CategoryCode);
                    dp.Add("@CategoryName", doc.CategoryName);
                    dp.Add("@OriginalCreatedAt", doc.OriginalCreatedAt);
                    dp.Add("@ContractNumber", doc.ContractNumber);
                    dp.Add("@CoreId", doc.CoreId);
                    dp.Add("@Version", doc.Version);
                    dp.Add("@AccountNumbers", doc.AccountNumbers);
                    dp.Add("@RequiresTypeTransformation", doc.RequiresTypeTransformation);
                    dp.Add("@FinalDocumentType", doc.FinalDocumentType);
                    dp.Add("@IsSigned", doc.IsSigned);
                    dp.Add("@DutOfferId", doc.DutOfferId);
                    dp.Add("@ProductType", doc.ProductType);
                    dp.Add("@OriginalDocumentName", doc.OriginalDocumentName);
                    dp.Add("@NewDocumentName", doc.NewDocumentName);
                    dp.Add("@OriginalDocumentCode", doc.OriginalDocumentCode);
                    dp.Add("@NewDocumentCode", doc.NewDocumentCode);
                    dp.Add("@TipDosijea", doc.TipDosijea);
                    dp.Add("@TargetDossierType", doc.TargetDossierType);
                    dp.Add("@ClientSegment", doc.ClientSegment);
                    dp.Add("@OldAlfrescoStatus", doc.OldAlfrescoStatus);
                    dp.Add("@NewAlfrescoStatus", doc.NewAlfrescoStatus);
                    dp.Add("@WillReceiveMigrationSuffix", doc.WillReceiveMigrationSuffix);
                    dp.Add("@CodeWillChange", doc.CodeWillChange);
                    dp.Add("@DocDescription", doc.DocDescription);
                    dp.Add("@DossierDestFolderId", doc.DossierDestFolderId);
                    dp.Add("@DestinationFolderId", doc.DestinationFolderId);
                    dp.Add("@DossierDestFolderIsCreated", doc.DossierDestFolderIsCreated);

                    var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
                    var rowsAffected = await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
                    totalInserted += rowsAffected;
                }
            }

            return totalInserted;
        }

        public async Task<int> UpdateDestinationFolderIdAsync(
            string dossierDestFolderId,
            string alfrescoFolderId,
            bool isCreated,
            string finalDocumentType,
            string? clientApiError = null,
            CancellationToken ct = default)
        {
            // Updates DestinationFolderId after folder is created
            // Status remains READY - documents will be picked up by MoveService
            // ErrorMsg is set if ClientAPI didn't return data for this client (ORA-01403: no data found)
            var sql = @"
                UPDATE DocStaging
                SET DestinationFolderId = @AlfrescoFolderId,
                    DossierDestFolderIsCreated = @IsCreated,
                    UpdatedAt = GETUTCDATE(),
                    FinalDocumentType = @FinalDocumentType,
                    ErrorMsg = @ErrorMsg
                WHERE DossierDestFolderId = @DossierDestFolderId";

            var parameters = new DynamicParameters();
            parameters.Add("@DossierDestFolderId", dossierDestFolderId);
            parameters.Add("@AlfrescoFolderId", alfrescoFolderId);
            parameters.Add("@IsCreated", isCreated);
            parameters.Add("@FinalDocumentType", finalDocumentType);
            // Set ErrorMsg to clientApiError if present, otherwise empty string
            parameters.Add("@ErrorMsg", clientApiError ?? string.Empty);

            var cmd = new CommandDefinition(sql, parameters, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);

            var rowsAffected = await Conn.ExecuteAsync(cmd).ConfigureAwait(false);

            return rowsAffected;
        }
    }
}
