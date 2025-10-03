using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Oracle.Models;
using Dapper;
using Oracle.Apstraction.Interfaces;
using Oracle.ManagedDataAccess.Client;

namespace Oracle.Infractructure.Implementation
{
    public class FolderStagingRepository : OracleRepository<FolderStaging, long>, IFolderStagingRepository
    {
        public FolderStagingRepository(IUnitOfWork uow) : base(uow)
        {
        }

        public async Task FailAsync(long id, string error, CancellationToken ct)
        {
            var sql = @"update FolderStaging
                        set status = 'ERROR',
                            RETRYCOUNT = NVL(RETRYCOUNT, 0) + 1,
                            error = :error,
                            updatedAt = SYSTIMESTAMP
                        where id = :id";

            var dp = new DynamicParameters();

            error = error.Substring(0, Math.Min(4000, error.Length)); // Oracle VARCHAR2 limit

            dp.Add(":error", error);
            dp.Add(":id", id);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task SetStatusAsync(long id, string status, string? error, CancellationToken ct)
        {
            //var trans = _connection.BeginTransaction();
            try
            {
                
                var sql = @"update FolderStaging
                        set status = :status,
                            error = :error,
                            updatedAt = SYSTIMESTAMP
                        where id = :id";

                var dp = new DynamicParameters();

                error = error?.Substring(0, Math.Min(4000, error.Length)); // Oracle VARCHAR2 limit

                if (error == null) error = "";

                dp.Add(":status", status);
                dp.Add(":error", error);
                dp.Add(":id", id);
                var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

                await Conn.ExecuteAsync(cmd).ConfigureAwait(false);

                //trans.Commit();
            }
            catch (Exception)
            {
               // trans.Rollback();
                //throw;
            }
            
            //_transaction.Commit();

        }

        public async Task<IReadOnlyList<FolderStaging>> TakeReadyForProcessingAsync(int take, CancellationToken ct)
        {
            //var sql = @"select * from FolderStaging
            //            where status = 'READY'
            //            and ROWNUM <= :take
            //
            // FOR UPDATE SKIP LOCKED";
            //var sql = @"select * from FolderStaging
            //            where status = 'READY'                        
            //            FETCH FIRST :take ROWS ONLY                        
            //            FOR UPDATE SKIP LOCKED";

            var sql = @$"select * from FolderStaging  
                         where status = '{MigrationStatus.Ready.ToDbString()}'                           
                         FETCH FIRST :take ROWS ONLY 
                         FOR UPDATE SKIP LOCKED
                         ";

            var dp = new DynamicParameters();
            dp.Add(":take", take);
            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);

            var res = await Conn.QueryAsync<FolderStaging>(cmd).ConfigureAwait(false);

            return res.AsList();
        }
    }
}
