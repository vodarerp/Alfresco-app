using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.SqlServer;
using Dapper;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SqlServer.Infrastructure.Implementation
{
    public class PreviewDocStagingRepository : SqlServerRepository<PreviewDocStaging, long>, IPreviewDocStagingRepository
    {
        public PreviewDocStagingRepository(IUnitOfWork uow, SqlServerOptions sqlServerOptions) : base(uow, sqlServerOptions)
        {
        }

        public async Task<int> InsertBatchAsync(IEnumerable<PreviewDocStaging> documents, CancellationToken ct = default)
        {
            var list = documents.ToList();
            if (list.Count == 0) return 0;

            const int batchSize = 100;
            int totalInserted = 0;

            const string sql = @"
                MERGE INTO PreviewDocStaging AS target
                USING (SELECT @NodeId AS NodeId) AS source
                ON target.NodeId = source.NodeId
                WHEN NOT MATCHED THEN
                    INSERT (
                        NodeId, Name, NodeType, ParentId, DocDescription,
                        OriginalDocumentCode, NewDocumentCode, OldAlfrescoStatus, NewAlfrescoStatus,
                        IsActive, DocumentType, DocumentTypeMigration, DossierType, TargetDossierType,
                        DossierDestinationFolderId, DossierDestinationFolderName, DossierDestinationFolderIsCreated,
                        Status, CoreId, ClientSegment, Source, CategoryCode, CategoryName,
                        ContractNumber, ProductType, AccountNumbers, OriginalCreatedAt,
                        NewDocumentName, OriginalDocumentName, FinalDocumentType,
                        RecordInserted, RecordExportedMigration,
                        ClientApiMbrJmbg, ClientApiClientName, ClientApiClientType, ClientApiClientSubtype,
                        ClientApiResidency, ClientApiSegment, ClientApiStaff, ClientApiOpuUser,
                        ClientApiOpuRealization, ClientApiBarclex, ClientApiCollaborator,
                        ClientApiBarCLEXName, ClientApiBarCLEXOpu, ClientApiBarCLEXGroupName,
                        ClientApiBarCLEXGroupCode, ClientApiBarCLEXCode, Properties
                    )
                    VALUES (
                        @NodeId, @Name, @NodeType, @ParentId, @DocDescription,
                        @OriginalDocumentCode, @NewDocumentCode, @OldAlfrescoStatus, @NewAlfrescoStatus,
                        @IsActive, @DocumentType, @DocumentTypeMigration, @DossierType, @TargetDossierType,
                        @DossierDestinationFolderId, @DossierDestinationFolderName, @DossierDestinationFolderIsCreated,
                        @Status, @CoreId, @ClientSegment, @Source, @CategoryCode, @CategoryName,
                        @ContractNumber, @ProductType, @AccountNumbers, @OriginalCreatedAt,
                        @NewDocumentName, @OriginalDocumentName, @FinalDocumentType,
                        @RecordInserted, @RecordExportedMigration,
                        @ClientApiMbrJmbg, @ClientApiClientName, @ClientApiClientType, @ClientApiClientSubtype,
                        @ClientApiResidency, @ClientApiSegment, @ClientApiStaff, @ClientApiOpuUser,
                        @ClientApiOpuRealization, @ClientApiBarclex, @ClientApiCollaborator,
                        @ClientApiBarCLEXName, @ClientApiBarCLEXOpu, @ClientApiBarCLEXGroupName,
                        @ClientApiBarCLEXGroupCode, @ClientApiBarCLEXCode, @Properties
                    );";

            for (int offset = 0; offset < list.Count; offset += batchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batch = list.Skip(offset).Take(batchSize).ToList();

                foreach (var doc in batch)
                {
                    var dp = new DynamicParameters();
                    dp.Add("@NodeId", doc.NodeId);
                    dp.Add("@Name", doc.Name);
                    dp.Add("@NodeType", doc.NodeType);
                    dp.Add("@ParentId", doc.ParentId);
                    dp.Add("@DocDescription", doc.DocDescription);
                    dp.Add("@OriginalDocumentCode", doc.OriginalDocumentCode);
                    dp.Add("@NewDocumentCode", doc.NewDocumentCode);
                    dp.Add("@OldAlfrescoStatus", doc.OldAlfrescoStatus);
                    dp.Add("@NewAlfrescoStatus", doc.NewAlfrescoStatus);
                    dp.Add("@IsActive", doc.IsActive);
                    dp.Add("@DocumentType", doc.DocumentType);
                    dp.Add("@DocumentTypeMigration", doc.DocumentTypeMigration);
                    dp.Add("@DossierType", doc.DossierType);
                    dp.Add("@TargetDossierType", doc.TargetDossierType);
                    dp.Add("@DossierDestinationFolderId", doc.DossierDestinationFolderId);
                    dp.Add("@DossierDestinationFolderName", doc.DossierDestinationFolderName);
                    dp.Add("@DossierDestinationFolderIsCreated", doc.DossierDestinationFolderIsCreated);
                    dp.Add("@Status", doc.Status);
                    dp.Add("@CoreId", doc.CoreId);
                    dp.Add("@ClientSegment", doc.ClientSegment);
                    dp.Add("@Source", doc.Source);
                    dp.Add("@CategoryCode", doc.CategoryCode);
                    dp.Add("@CategoryName", doc.CategoryName);
                    dp.Add("@ContractNumber", doc.ContractNumber);
                    dp.Add("@ProductType", doc.ProductType);
                    dp.Add("@AccountNumbers", doc.AccountNumbers);
                    dp.Add("@OriginalCreatedAt", doc.OriginalCreatedAt);
                    dp.Add("@NewDocumentName", doc.NewDocumentName);
                    dp.Add("@OriginalDocumentName", doc.OriginalDocumentName);
                    dp.Add("@FinalDocumentType", doc.FinalDocumentType);
                    dp.Add("@RecordInserted", doc.RecordInserted);
                    dp.Add("@RecordExportedMigration", doc.RecordExportedMigration);
                    dp.Add("@ClientApiMbrJmbg", doc.ClientApiMbrJmbg);
                    dp.Add("@ClientApiClientName", doc.ClientApiClientName);
                    dp.Add("@ClientApiClientType", doc.ClientApiClientType);
                    dp.Add("@ClientApiClientSubtype", doc.ClientApiClientSubtype);
                    dp.Add("@ClientApiResidency", doc.ClientApiResidency);
                    dp.Add("@ClientApiSegment", doc.ClientApiSegment);
                    dp.Add("@ClientApiStaff", doc.ClientApiStaff);
                    dp.Add("@ClientApiOpuUser", doc.ClientApiOpuUser);
                    dp.Add("@ClientApiOpuRealization", doc.ClientApiOpuRealization);
                    dp.Add("@ClientApiBarclex", doc.ClientApiBarclex);
                    dp.Add("@ClientApiCollaborator", doc.ClientApiCollaborator);
                    dp.Add("@ClientApiBarCLEXName", doc.ClientApiBarCLEXName);
                    dp.Add("@ClientApiBarCLEXOpu", doc.ClientApiBarCLEXOpu);
                    dp.Add("@ClientApiBarCLEXGroupName", doc.ClientApiBarCLEXGroupName);
                    dp.Add("@ClientApiBarCLEXGroupCode", doc.ClientApiBarCLEXGroupCode);
                    dp.Add("@ClientApiBarCLEXCode", doc.ClientApiBarCLEXCode);
                    dp.Add("@Properties", doc.Properties);

                    var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
                    totalInserted += await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
                }
            }

            return totalInserted;
        }

        public async Task<int> InsertManyMergeAsync(IEnumerable<PreviewDocStaging> documents, CancellationToken ct = default)
        {
            var list = documents.ToList();
            if (list.Count == 0) return 0;

            const int batchSize = 100;
            int totalInserted = 0;

            const string sql = @"
                MERGE INTO PreviewDocStaging AS target
                USING (SELECT @NodeId AS NodeId) AS source
                ON target.NodeId = source.NodeId
                WHEN NOT MATCHED THEN
                    INSERT (
                        NodeId, Name, NodeType, ParentId, ParentFolderName, DocDescription,
                        OriginalDocumentCode, NewDocumentCode, OldAlfrescoStatus, NewAlfrescoStatus,
                        IsActive, DocumentType, DocumentTypeMigration, DossierType, TargetDossierType,
                        DossierDestinationFolderId, DossierDestinationFolderName, DossierDestinationFolderIsCreated,
                        Status, CoreId, ClientSegment, Source, CategoryCode, CategoryName,
                        ContractNumber, ProductType, AccountNumbers, OriginalCreatedAt,
                        NewDocumentName, OriginalDocumentName, FinalDocumentType,
                        RecordInserted, RecordExportedMigration,
                        ClientApiMbrJmbg, ClientApiClientName, ClientApiClientType, ClientApiClientSubtype,
                        ClientApiResidency, ClientApiSegment, ClientApiStaff, ClientApiOpuUser,
                        ClientApiOpuRealization, ClientApiBarclex, ClientApiCollaborator,
                        ClientApiBarCLEXName, ClientApiBarCLEXOpu, ClientApiBarCLEXGroupName,
                        ClientApiBarCLEXGroupCode, ClientApiBarCLEXCode, Properties
                    )
                    VALUES (
                        @NodeId, @Name, @NodeType, @ParentId, @ParentFolderName, @DocDescription,
                        @OriginalDocumentCode, @NewDocumentCode, @OldAlfrescoStatus, @NewAlfrescoStatus,
                        @IsActive, @DocumentType, @DocumentTypeMigration, @DossierType, @TargetDossierType,
                        @DossierDestinationFolderId, @DossierDestinationFolderName, @DossierDestinationFolderIsCreated,
                        @Status, @CoreId, @ClientSegment, @Source, @CategoryCode, @CategoryName,
                        @ContractNumber, @ProductType, @AccountNumbers, @OriginalCreatedAt,
                        @NewDocumentName, @OriginalDocumentName, @FinalDocumentType,
                        @RecordInserted, @RecordExportedMigration,
                        @ClientApiMbrJmbg, @ClientApiClientName, @ClientApiClientType, @ClientApiClientSubtype,
                        @ClientApiResidency, @ClientApiSegment, @ClientApiStaff, @ClientApiOpuUser,
                        @ClientApiOpuRealization, @ClientApiBarclex, @ClientApiCollaborator,
                        @ClientApiBarCLEXName, @ClientApiBarCLEXOpu, @ClientApiBarCLEXGroupName,
                        @ClientApiBarCLEXGroupCode, @ClientApiBarCLEXCode, @Properties
                    );";

            for (int offset = 0; offset < list.Count; offset += batchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batch = list.Skip(offset).Take(batchSize).ToList();

                foreach (var doc in batch)
                {
                    var dp = new DynamicParameters();
                    dp.Add("@NodeId", doc.NodeId);
                    dp.Add("@Name", doc.Name);
                    dp.Add("@NodeType", doc.NodeType);
                    dp.Add("@ParentId", doc.ParentId);
                    dp.Add("@ParentFolderName", doc.ParentFolderName);
                    dp.Add("@DocDescription", doc.DocDescription);
                    dp.Add("@OriginalDocumentCode", doc.OriginalDocumentCode);
                    dp.Add("@NewDocumentCode", doc.NewDocumentCode);
                    dp.Add("@OldAlfrescoStatus", doc.OldAlfrescoStatus);
                    dp.Add("@NewAlfrescoStatus", doc.NewAlfrescoStatus);
                    dp.Add("@IsActive", doc.IsActive);
                    dp.Add("@DocumentType", doc.DocumentType);
                    dp.Add("@DocumentTypeMigration", doc.DocumentTypeMigration);
                    dp.Add("@DossierType", doc.DossierType);
                    dp.Add("@TargetDossierType", doc.TargetDossierType);
                    dp.Add("@DossierDestinationFolderId", doc.DossierDestinationFolderId);
                    dp.Add("@DossierDestinationFolderName", doc.DossierDestinationFolderName);
                    dp.Add("@DossierDestinationFolderIsCreated", doc.DossierDestinationFolderIsCreated);
                    dp.Add("@Status", doc.Status);
                    dp.Add("@CoreId", doc.CoreId);
                    dp.Add("@ClientSegment", doc.ClientSegment);
                    dp.Add("@Source", doc.Source);
                    dp.Add("@CategoryCode", doc.CategoryCode);
                    dp.Add("@CategoryName", doc.CategoryName);
                    dp.Add("@ContractNumber", doc.ContractNumber);
                    dp.Add("@ProductType", doc.ProductType);
                    dp.Add("@AccountNumbers", doc.AccountNumbers);
                    dp.Add("@OriginalCreatedAt", doc.OriginalCreatedAt);
                    dp.Add("@NewDocumentName", doc.NewDocumentName);
                    dp.Add("@OriginalDocumentName", doc.OriginalDocumentName);
                    dp.Add("@FinalDocumentType", doc.FinalDocumentType);
                    dp.Add("@RecordInserted", doc.RecordInserted);
                    dp.Add("@RecordExportedMigration", doc.RecordExportedMigration);
                    dp.Add("@ClientApiMbrJmbg", doc.ClientApiMbrJmbg);
                    dp.Add("@ClientApiClientName", doc.ClientApiClientName);
                    dp.Add("@ClientApiClientType", doc.ClientApiClientType);
                    dp.Add("@ClientApiClientSubtype", doc.ClientApiClientSubtype);
                    dp.Add("@ClientApiResidency", doc.ClientApiResidency);
                    dp.Add("@ClientApiSegment", doc.ClientApiSegment);
                    dp.Add("@ClientApiStaff", doc.ClientApiStaff);
                    dp.Add("@ClientApiOpuUser", doc.ClientApiOpuUser);
                    dp.Add("@ClientApiOpuRealization", doc.ClientApiOpuRealization);
                    dp.Add("@ClientApiBarclex", doc.ClientApiBarclex);
                    dp.Add("@ClientApiCollaborator", doc.ClientApiCollaborator);
                    dp.Add("@ClientApiBarCLEXName", doc.ClientApiBarCLEXName);
                    dp.Add("@ClientApiBarCLEXOpu", doc.ClientApiBarCLEXOpu);
                    dp.Add("@ClientApiBarCLEXGroupName", doc.ClientApiBarCLEXGroupName);
                    dp.Add("@ClientApiBarCLEXGroupCode", doc.ClientApiBarCLEXGroupCode);
                    dp.Add("@ClientApiBarCLEXCode", doc.ClientApiBarCLEXCode);
                    dp.Add("@Properties", doc.Properties);

                    var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
                    totalInserted += await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
                }
            }

            return totalInserted;
        }

        public async Task<long> GetCountByDossierTypeAsync(string dossierType, CancellationToken ct = default)
        {
            const string sql = "SELECT COUNT(*) FROM PreviewDocStaging WHERE DossierType = @DossierType";
            var dp = new DynamicParameters();
            dp.Add("@DossierType", dossierType);
            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            return await Conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);
        }

        public async Task<IEnumerable<string>> GetDistinctPendingFoldersAsync(int batchSize, CancellationToken ct = default)
        {
            const string sql = @"
                WITH SelectedFolders AS (
                    SELECT DISTINCT TOP (@BatchSize) DossierDestinationFolderName
                    FROM PreviewDocStaging WITH (UPDLOCK, READPAST)
                    WHERE Status = 'PENDING'
                      AND ISNULL(DossierDestinationFolderName, '') <> ''
                )
                UPDATE d
                SET d.Status = 'IN_PROGRESS'
                OUTPUT INSERTED.DossierDestinationFolderName
                FROM PreviewDocStaging d
                JOIN SelectedFolders s ON d.DossierDestinationFolderName = s.DossierDestinationFolderName;";

            var dp = new DynamicParameters();
            dp.Add("@BatchSize", batchSize);
            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var result = await Conn.QueryAsync<string>(cmd).ConfigureAwait(false);
            return result.Distinct();
        }

        public async Task UpdateFolderDataAsync(string folderName, string? folderId, int isCreated, string status, CancellationToken ct = default)
        {
            const string sql = @"
                UPDATE PreviewDocStaging
                SET DossierDestinationFolderId = @FolderId,
                    DossierDestinationFolderIsCreated = @IsCreated,
                    Status = @Status
                WHERE DossierDestinationFolderName = @FolderName";

            var dp = new DynamicParameters();
            dp.Add("@FolderName", folderName);
            dp.Add("@FolderId", folderId);
            dp.Add("@IsCreated", isCreated);
            dp.Add("@Status", status);
            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task UpdateFolderDataAndClientApiAsync(
            string folderName,
            string? folderId,
            int isCreated,
            string status,
            Migration.Abstraction.Models.ClientData? clientData,
            CancellationToken ct = default)
        {
            const string sql = @"
                UPDATE PreviewDocStaging
                SET DossierDestinationFolderId          = @FolderId,
                    DossierDestinationFolderIsCreated   = @IsCreated,
                    Status                              = @Status,
                    ClientApiMbrJmbg                   = @MbrJmbg,
                    ClientApiClientName                = @ClientName,
                    ClientApiClientType                = @ClientType,
                    ClientApiClientSubtype             = @ClientSubtype,
                    ClientApiResidency                 = @Residency,
                    ClientApiSegment                   = @Segment,
                    ClientApiStaff                     = @Staff,
                    ClientApiOpuUser                   = @OpuUser,
                    ClientApiOpuRealization            = @OpuRealization,
                    ClientApiBarclex                   = @Barclex,
                    ClientApiCollaborator              = @Collaborator,
                    ClientApiBarCLEXName               = @BarCLEXName,
                    ClientApiBarCLEXOpu                = @BarCLEXOpu,
                    ClientApiBarCLEXGroupName          = @BarCLEXGroupName,
                    ClientApiBarCLEXGroupCode          = @BarCLEXGroupCode,
                    ClientApiBarCLEXCode               = @BarCLEXCode
                WHERE DossierDestinationFolderName = @FolderName";

            var dp = new DynamicParameters();
            dp.Add("@FolderName", folderName);
            dp.Add("@FolderId", folderId);
            dp.Add("@IsCreated", isCreated);
            dp.Add("@Status", status);
            dp.Add("@MbrJmbg",          clientData?.MbrJmbg);
            dp.Add("@ClientName",       clientData?.ClientName);
            dp.Add("@ClientType",       clientData?.ClientType);
            dp.Add("@ClientSubtype",    clientData?.ClientSubtype);
            dp.Add("@Residency",        clientData?.Residency);
            dp.Add("@Segment",          clientData?.Segment);
            dp.Add("@Staff",            clientData?.Staff);
            dp.Add("@OpuUser",          clientData?.OpuUser);
            dp.Add("@OpuRealization",   clientData?.OpuRealization);
            dp.Add("@Barclex",          clientData?.Barclex);
            dp.Add("@Collaborator",     clientData?.Collaborator);
            dp.Add("@BarCLEXName",      clientData?.BarCLEXName);
            dp.Add("@BarCLEXOpu",       clientData?.BarCLEXOpu);
            dp.Add("@BarCLEXGroupName", clientData?.BarCLEXGroupName);
            dp.Add("@BarCLEXGroupCode", clientData?.BarCLEXGroupCode);
            dp.Add("@BarCLEXCode",      clientData?.BarCLEXCode);

            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task<long> GetTotalCountAsync(CancellationToken ct = default)
        {
            const string sql = "SELECT COUNT(*) FROM PreviewDocStaging";
            var cmd = new CommandDefinition(sql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            return await Conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);
        }

        public async Task<long> GetCountByStatusAsync(string status, CancellationToken ct = default)
        {
            const string sql = "SELECT COUNT(*) FROM PreviewDocStaging WHERE Status = @Status";
            var dp = new DynamicParameters();
            dp.Add("@Status", status);
            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            return await Conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);
        }

        public async Task<IReadOnlyDictionary<string, long>> GetDistinctFolderCountsPerStatusAsync(CancellationToken ct = default)
        {
            const string sql = @"
                SELECT Status, COUNT(DISTINCT DossierDestinationFolderName) AS Cnt
                FROM PreviewDocStaging
                GROUP BY Status";
            var cmd = new CommandDefinition(sql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var rows = await Conn.QueryAsync<StatusFolderCount>(cmd).ConfigureAwait(false);
            return rows.ToDictionary(r => r.Status, r => r.Cnt);
        }

        private sealed class StatusFolderCount
        {
            public string Status { get; set; } = "";
            public long Cnt { get; set; }
        }

        public async Task UpdateClientApiDataAsync(string dossierDestinationFolderName, Migration.Abstraction.Models.ClientData clientData, CancellationToken ct = default)
        {
            const string sql = @"
                UPDATE PreviewDocStaging
                SET ClientApiMbrJmbg        = @MbrJmbg,
                    ClientApiClientName     = @ClientName,
                    ClientApiClientType     = @ClientType,
                    ClientApiClientSubtype  = @ClientSubtype,
                    ClientApiResidency      = @Residency,
                    ClientApiSegment        = @Segment,
                    ClientApiStaff          = @Staff,
                    ClientApiOpuUser        = @OpuUser,
                    ClientApiOpuRealization = @OpuRealization,
                    ClientApiBarclex        = @Barclex,
                    ClientApiCollaborator   = @Collaborator,
                    ClientApiBarCLEXName    = @BarCLEXName,
                    ClientApiBarCLEXOpu     = @BarCLEXOpu,
                    ClientApiBarCLEXGroupName = @BarCLEXGroupName,
                    ClientApiBarCLEXGroupCode = @BarCLEXGroupCode,
                    ClientApiBarCLEXCode    = @BarCLEXCode
                WHERE DossierDestinationFolderName = @FolderName";

            var dp = new DynamicParameters();
            dp.Add("@FolderName", dossierDestinationFolderName);
            dp.Add("@MbrJmbg", clientData.MbrJmbg);
            dp.Add("@ClientName", clientData.ClientName);
            dp.Add("@ClientType", clientData.ClientType);
            dp.Add("@ClientSubtype", clientData.ClientSubtype);
            dp.Add("@Residency", clientData.Residency);
            dp.Add("@Segment", clientData.Segment);
            dp.Add("@Staff", clientData.Staff);
            dp.Add("@OpuUser", clientData.OpuUser);
            dp.Add("@OpuRealization", clientData.OpuRealization);
            dp.Add("@Barclex", clientData.Barclex);
            dp.Add("@Collaborator", clientData.Collaborator);
            dp.Add("@BarCLEXName", clientData.BarCLEXName);
            dp.Add("@BarCLEXOpu", clientData.BarCLEXOpu);
            dp.Add("@BarCLEXGroupName", clientData.BarCLEXGroupName);
            dp.Add("@BarCLEXGroupCode", clientData.BarCLEXGroupCode);
            dp.Add("@BarCLEXCode", clientData.BarCLEXCode);

            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task<IEnumerable<string>> GetDistinctFoldersForCreationAsync(int batchSize, CancellationToken ct = default)
        {
            const string sql = @"
                WITH SelectedFolders AS (
                    SELECT DISTINCT TOP (@BatchSize) DossierDestinationFolderName
                    FROM PreviewDocStaging WITH (UPDLOCK, READPAST)
                    WHERE Status = 'FOLDER_PENDING_CREATION'
                      AND ISNULL(DossierDestinationFolderName, '') <> ''
                )
                UPDATE d
                SET d.Status = 'IN_PROGRESS'
                OUTPUT INSERTED.DossierDestinationFolderName
                FROM PreviewDocStaging d
                JOIN SelectedFolders s ON d.DossierDestinationFolderName = s.DossierDestinationFolderName;";

            var dp = new DynamicParameters();
            dp.Add("@BatchSize", batchSize);
            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var result = await Conn.QueryAsync<string>(cmd).ConfigureAwait(false);
            return result.Distinct();
        }

        public async Task<PreviewDocStaging?> GetFirstRecordByFolderNameAsync(string folderName, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT TOP 1 * FROM PreviewDocStaging
                WHERE DossierDestinationFolderName = @FolderName";

            var dp = new DynamicParameters();
            dp.Add("@FolderName", folderName);
            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            return await Conn.QueryFirstOrDefaultAsync<PreviewDocStaging>(cmd).ConfigureAwait(false);
        }

        public async Task<IEnumerable<PreviewDocStaging>> GetForExportAsync(
            string? dossierType = null,
            string? targetDossierType = null,
            CancellationToken ct = default)
        {
            var sql = "SELECT * FROM PreviewDocStaging WHERE 1=1";
            var dp = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(dossierType))
            {
                sql += " AND DossierType = @DossierType";
                dp.Add("@DossierType", dossierType);
            }

            if (!string.IsNullOrWhiteSpace(targetDossierType))
            {
                sql += " AND TargetDossierType = @TargetDossierType";
                dp.Add("@TargetDossierType", targetDossierType);
            }

            sql += " ORDER BY DossierType, DossierDestinationFolderName, Id";

            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            return await Conn.QueryAsync<PreviewDocStaging>(cmd).ConfigureAwait(false);
        }

        public IEnumerable<PreviewDocStaging> GetForExportUnbuffered(
            string? dossierType = null,
            string? targetDossierType = null)
        {
            var sql = "SELECT * FROM PreviewDocStaging WHERE 1=1";
            var dp = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(dossierType))
            {
                sql += " AND DossierType = @DossierType";
                dp.Add("@DossierType", dossierType);
            }

            if (!string.IsNullOrWhiteSpace(targetDossierType))
            {
                sql += " AND TargetDossierType = @TargetDossierType";
                dp.Add("@TargetDossierType", targetDossierType);
            }

            sql += " ORDER BY Id";

            return Conn.Query<PreviewDocStaging>(sql, dp, Tx, buffered: false, commandTimeout: _commandTimeoutSeconds);
        }

        public async Task<IList<string?>> GetDistinctExportTargetTypesAsync(
            string? dossierType = null,
            CancellationToken ct = default)
        {
            var sql = "SELECT DISTINCT TargetDossierType FROM PreviewDocStaging WHERE 1=1";
            var dp = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(dossierType))
            {
                sql += " AND DossierType = @DossierType";
                dp.Add("@DossierType", dossierType);
            }

            sql += " ORDER BY TargetDossierType";

            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var result = await Conn.QueryAsync<string?>(cmd).ConfigureAwait(false);
            return result.ToList();
        }

        public async Task<IList<(string? TargetDossierType, long Count)>> GetExportTargetTypeCountsAsync(
            string? dossierType = null,
            CancellationToken ct = default)
        {
            var sql = @"
                SELECT TargetDossierType, COUNT(*) AS Count
                FROM PreviewDocStaging
                WHERE 1=1";
            var dp = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(dossierType))
            {
                sql += " AND DossierType = @DossierType";
                dp.Add("@DossierType", dossierType);
            }

            sql += " GROUP BY TargetDossierType ORDER BY TargetDossierType";

            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var rows = await Conn.QueryAsync<ExportCountRow>(cmd).ConfigureAwait(false);
            return rows.Select(r => (r.TargetDossierType, r.Count)).ToList();
        }

        public IEnumerable<PreviewDocStaging> GetForExportUnbufferedPaged(
            string? dossierType,
            string? targetDossierType,
            long offset,
            int pageSize)
        {
            var sql = "SELECT * FROM PreviewDocStaging WHERE 1=1";
            var dp = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(dossierType))
            {
                sql += " AND DossierType = @DossierType";
                dp.Add("@DossierType", dossierType);
            }
            if (!string.IsNullOrWhiteSpace(targetDossierType))
            {
                sql += " AND TargetDossierType = @TargetDossierType";
                dp.Add("@TargetDossierType", targetDossierType);
            }

            sql += " ORDER BY Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
            dp.Add("@Offset", offset);
            dp.Add("@PageSize", pageSize);

            return Conn.Query<PreviewDocStaging>(sql, dp, Tx, buffered: false, commandTimeout: _commandTimeoutSeconds);
        }

        public async Task<IEnumerable<(string FolderName, bool NeedsCreation)>> GetDistinctFoldersForFolderStagingAsync(int batchSize, CancellationToken ct = default)
        {
            const string sql = @"
                WITH SelectedFolders AS (
                    SELECT DISTINCT TOP (@BatchSize) DossierDestinationFolderName
                    FROM PreviewDocStaging WITH (UPDLOCK, READPAST)
                    WHERE Status IN ('FOLDER_PENDING_CREATION', 'FOLDER_PENDING_EXISTS')
                      AND ISNULL(DossierDestinationFolderName, '') <> ''
                )
                UPDATE d
                SET d.Status = 'IN_PROGRESS'
                OUTPUT INSERTED.DossierDestinationFolderName, DELETED.Status AS OriginalStatus
                FROM PreviewDocStaging d
                JOIN SelectedFolders s ON d.DossierDestinationFolderName = s.DossierDestinationFolderName;";

            var dp = new DynamicParameters();
            dp.Add("@BatchSize", batchSize);
            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var rows = await Conn.QueryAsync<FolderSyncRow>(cmd).ConfigureAwait(false);
            return rows
                .GroupBy(r => r.DossierDestinationFolderName)
                .Select(g => (g.Key, g.First().OriginalStatus == "FOLDER_PENDING_CREATION"))
                .ToList();
        }

        private sealed class FolderSyncRow
        {
            public string DossierDestinationFolderName { get; set; } = "";
            public string OriginalStatus               { get; set; } = "";
        }

        private sealed class ExportCountRow
        {
            public string? TargetDossierType { get; set; }
            public long Count { get; set; }
        }

        public async Task<IList<PreviewDocStaging>> TakeReadyForTransferAsync(
            int batchSize,
            string? dossierType,
            string? targetDossierType,
            CancellationToken ct = default)
        {
            var whereExtra = "";
            var dp = new DynamicParameters();
            dp.Add("@BatchSize", batchSize);

            if (!string.IsNullOrWhiteSpace(dossierType))
            {
                whereExtra += " AND DossierType = @DossierType";
                dp.Add("@DossierType", dossierType);
            }
            if (!string.IsNullOrWhiteSpace(targetDossierType))
            {
                whereExtra += " AND TargetDossierType = @TargetDossierType";
                dp.Add("@TargetDossierType", targetDossierType);
            }

            var sql = $@"
                WITH Selected AS (
                    SELECT TOP (@BatchSize) Id
                    FROM PreviewDocStaging WITH (ROWLOCK, UPDLOCK, READPAST)
                    WHERE Status IN ('FOLDER_EXISTS', 'FOLDER_CREATED')
                    {whereExtra}
                    ORDER BY Id ASC
                )
                UPDATE p
                SET p.Status = 'TRANSFER_IN_PROGRESS'
                OUTPUT INSERTED.*
                FROM PreviewDocStaging p
                INNER JOIN Selected s ON p.Id = s.Id";

            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var result = await Conn.QueryAsync<PreviewDocStaging>(cmd).ConfigureAwait(false);
            return result.AsList();
        }

        public async Task ResetTransferInProgressAsync(IEnumerable<long> ids, CancellationToken ct = default)
        {
            var idList = ids.ToList();
            if (idList.Count == 0) return;

            const string sql = @"
                UPDATE PreviewDocStaging
                SET Status = CASE
                    WHEN DossierDestinationFolderIsCreated = 1 THEN 'FOLDER_CREATED'
                    ELSE 'FOLDER_EXISTS'
                END
                WHERE Id IN @Ids
                  AND Status = 'TRANSFER_IN_PROGRESS'";

            var dp = new DynamicParameters();
            dp.Add("@Ids", idList);
            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task<IEnumerable<PreviewDocStaging>> GetForTransferAsync(
            string? dossierType = null,
            string? targetDossierType = null,
            CancellationToken ct = default)
        {
            var sql = @"
                SELECT * FROM PreviewDocStaging
                WHERE Status IN ('FOLDER_EXISTS', 'FOLDER_CREATED')";

            var dp = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(dossierType))
            {
                sql += " AND DossierType = @DossierType";
                dp.Add("@DossierType", dossierType);
            }

            if (!string.IsNullOrWhiteSpace(targetDossierType))
            {
                sql += " AND TargetDossierType = @TargetDossierType";
                dp.Add("@TargetDossierType", targetDossierType);
            }

            sql += " ORDER BY Id";

            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            return await Conn.QueryAsync<PreviewDocStaging>(cmd).ConfigureAwait(false);
        }

        public async Task UpdateTransferredBatchAsync(IEnumerable<long> ids, CancellationToken ct = default)
        {
            var idList = ids.ToList();
            if (idList.Count == 0) return;

            const string sql = @"
                UPDATE PreviewDocStaging
                SET Status = 'TRANSFERRED'
                WHERE Id IN @Ids";

            var dp = new DynamicParameters();
            dp.Add("@Ids", idList);

            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task<IEnumerable<(string FolderName, string FolderId)>> GetCreatedFolderIdsAsync(CancellationToken ct = default)
        {
            const string sql = @"
                SELECT DISTINCT DossierDestinationFolderName, DossierDestinationFolderId
                FROM PreviewDocStaging
                WHERE Status = 'FOLDER_CREATED'
                  AND ISNULL(DossierDestinationFolderId, '') <> ''
                  AND ISNULL(DossierDestinationFolderName, '') <> ''";

            var cmd = new CommandDefinition(sql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var rows = await Conn.QueryAsync<FolderRow>(cmd).ConfigureAwait(false);
            return rows.Select(r => (r.DossierDestinationFolderName, r.DossierDestinationFolderId));
        }

        private sealed class FolderRow
        {
            public string DossierDestinationFolderName { get; set; } = "";
            public string DossierDestinationFolderId { get; set; } = "";
        }

        public async Task DeleteAllAsync(CancellationToken ct = default)
        {
            const string sql = "DELETE FROM PreviewDocStaging";
            var cmd = new CommandDefinition(sql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task<(IEnumerable<PreviewDocStaging> Items, int TotalCount)> GetPagedAsync(
            int pageNumber, int pageSize,
            CancellationToken ct = default)
        {
            const string countSql = "SELECT COUNT(*) FROM PreviewDocStaging";
            var countCmd = new CommandDefinition(countSql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var totalCount = await Conn.ExecuteScalarAsync<int>(countCmd).ConfigureAwait(false);

            var dp = new DynamicParameters();
            dp.Add("@Offset", (pageNumber - 1) * pageSize);
            dp.Add("@PageSize", pageSize);

            const string dataSql = @"
                SELECT * FROM PreviewDocStaging
                ORDER BY Id
                OFFSET @Offset ROWS
                FETCH NEXT @PageSize ROWS ONLY";

            var cmd = new CommandDefinition(dataSql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var items = await Conn.QueryAsync<PreviewDocStaging>(cmd).ConfigureAwait(false);

            return (items, totalCount);
        }
    }
}
