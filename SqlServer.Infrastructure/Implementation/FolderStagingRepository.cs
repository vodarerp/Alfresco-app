using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Oracle.Models;
using Dapper;
using SqlServer.Abstraction.Interfaces;
using Microsoft.Data.SqlClient;

namespace SqlServer.Infrastructure.Implementation
{
    public class FolderStagingRepository : SqlServerRepository<FolderStaging, long>, IFolderStagingRepository
    {
        public FolderStagingRepository(IUnitOfWork uow) : base(uow)
        {
        }

        public async Task FailAsync(long id, string error, CancellationToken ct)
        {
            var sql = @"UPDATE FolderStaging
                        SET status = 'ERROR',
                           
                            error = @error,
                            updatedAt = SYSDATETIMEOFFSET()
                        WHERE id = @id";

            var dp = new DynamicParameters();

            error = error.Length > 4000 ? error[..4000] : error; // SQL Server VARCHAR/NVARCHAR limit

            dp.Add("@error", error);
            dp.Add("@id", id);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task SetStatusAsync(long id, string status, string? error, CancellationToken ct)
        {
            try
            {
                var sql = @"UPDATE FolderStaging
                        SET status = @status,
                            error = @error,
                            updatedAt = SYSDATETIMEOFFSET()
                        WHERE id = @id";

                var dp = new DynamicParameters();

                error = error?.Length > 4000 ? error[..4000] : error; // SQL Server VARCHAR/NVARCHAR limit

                if (error == null) error = "";

                dp.Add("@status", status);
                dp.Add("@error", error);
                dp.Add("@id", id);
                var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

                await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Exception handling - transaction managed by UnitOfWork
            }
        }

        public async Task<IReadOnlyList<FolderStaging>> TakeReadyForProcessingAsync(int take, CancellationToken ct)
        {
            // SQL Server uses WITH (ROWLOCK, UPDLOCK, READPAST) for similar behavior to Oracle's FOR UPDATE SKIP LOCKED
            var sql = @$"SELECT TOP (@take) *
                         FROM FolderStaging WITH (ROWLOCK, UPDLOCK, READPAST)
                         WHERE status = '{MigrationStatus.Ready.ToDbString()}'";

            var dp = new DynamicParameters();
            dp.Add("@take", take);
            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

            var res = await Conn.QueryAsync<FolderStaging>(cmd).ConfigureAwait(false);

            return res.AsList();
        }

        public async Task<long> CountReadyForProcessingAsync(CancellationToken ct)
        {
            var sql = @$"SELECT COUNT(*) FROM FolderStaging
                         WHERE status = '{MigrationStatus.Ready.ToDbString()}'";

            var cmd = new CommandDefinition(sql, transaction: Tx, cancellationToken: ct);

            var count = await Conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);

            return count;
        }

        /// <summary>
        /// Inserts multiple folders, ignoring duplicates based on NodeId.
        /// Uses MERGE statement to handle duplicates efficiently.
        /// </summary>
        public async Task<int> InsertManyIgnoreDuplicatesAsync(IEnumerable<FolderStaging> folders, CancellationToken ct)
        {
            var listFolders = folders.ToList();
            if (listFolders.Count == 0) return 0;

            int totalInserted = 0;

            // Process in batches to avoid parameter limits
            const int batchSize = 100;

            for (int offset = 0; offset < listFolders.Count; offset += batchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batch = listFolders.Skip(offset).Take(batchSize).ToList();

                foreach (var folder in batch)
                {
                    // Use MERGE to insert only if NodeId doesn't exist
                    var sql = @"
                        MERGE INTO FolderStaging AS target
                        USING (SELECT @NodeId AS NodeId) AS source
                        ON target.NodeId = source.NodeId
                        WHEN NOT MATCHED THEN
                            INSERT (NodeId, ParentId, Name, Status, DestFolderId, DossierDestFolderId,
                                    CreatedAt, UpdatedAt, ClientType, CoreId, ClientName, MbrJmbg,
                                    ProductType, ContractNumber, Batch, Source, UniqueIdentifier,
                                    ProcessDate, Residency, Segment, ClientSubtype, Staff, OpuUser,
                                    OpuRealization, Barclex, Collaborator, BarCLEXName, BarCLEXOpu,
                                    BarCLEXGroupName, BarCLEXGroupCode, BarCLEXCode, Creator, ArchivedAt,
                                    TipDosijea, TargetDossierType, ClientSegment)
                            VALUES (@NodeId, @ParentId, @Name, @Status, @DestFolderId, @DossierDestFolderId,
                                    @CreatedAt, @UpdatedAt, @ClientType, @CoreId, @ClientName, @MbrJmbg,
                                    @ProductType, @ContractNumber, @Batch, @Source, @UniqueIdentifier,
                                    @ProcessDate, @Residency, @Segment, @ClientSubtype, @Staff, @OpuUser,
                                    @OpuRealization, @Barclex, @Collaborator, @BarCLEXName, @BarCLEXOpu,
                                    @BarCLEXGroupName, @BarCLEXGroupCode, @BarCLEXCode, @Creator, @ArchivedAt,
                                    @TipDosijea, @TargetDossierType, @ClientSegment);";

                    var dp = new DynamicParameters();
                    dp.Add("@NodeId", folder.NodeId);
                    dp.Add("@ParentId", folder.ParentId);
                    dp.Add("@Name", folder.Name);
                    dp.Add("@Status", folder.Status);
                    dp.Add("@DestFolderId", folder.DestFolderId);
                    dp.Add("@DossierDestFolderId", folder.DossierDestFolderId);
                    dp.Add("@CreatedAt", folder.CreatedAt);
                    dp.Add("@UpdatedAt", folder.UpdatedAt);
                    dp.Add("@ClientType", folder.ClientType);
                    dp.Add("@CoreId", folder.CoreId);
                    dp.Add("@ClientName", folder.ClientName);
                    dp.Add("@MbrJmbg", folder.MbrJmbg);
                    dp.Add("@ProductType", folder.ProductType);
                    dp.Add("@ContractNumber", folder.ContractNumber);
                    dp.Add("@Batch", folder.Batch);
                    dp.Add("@Source", folder.Source);
                    dp.Add("@UniqueIdentifier", folder.UniqueIdentifier);
                    dp.Add("@ProcessDate", folder.ProcessDate);
                    dp.Add("@Residency", folder.Residency);
                    dp.Add("@Segment", folder.Segment);
                    dp.Add("@ClientSubtype", folder.ClientSubtype);
                    dp.Add("@Staff", folder.Staff);
                    dp.Add("@OpuUser", folder.OpuUser);
                    dp.Add("@OpuRealization", folder.OpuRealization);
                    dp.Add("@Barclex", folder.Barclex);
                    dp.Add("@Collaborator", folder.Collaborator);
                    dp.Add("@BarCLEXName", folder.BarCLEXName);
                    dp.Add("@BarCLEXOpu", folder.BarCLEXOpu);
                    dp.Add("@BarCLEXGroupName", folder.BarCLEXGroupName);
                    dp.Add("@BarCLEXGroupCode", folder.BarCLEXGroupCode);
                    dp.Add("@BarCLEXCode", folder.BarCLEXCode);
                    dp.Add("@Creator", folder.Creator);
                    dp.Add("@ArchivedAt", folder.ArchivedAt);
                    dp.Add("@TipDosijea", folder.TipDosijea);
                    dp.Add("@TargetDossierType", folder.TargetDossierType);
                    dp.Add("@ClientSegment", folder.ClientSegment);

                    var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
                    var rowsAffected = await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
                    totalInserted += rowsAffected;
                }
            }

            return totalInserted;
        }
    }
}
