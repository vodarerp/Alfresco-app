using Alfresco.Contracts.Oracle.Models;
using Oracle.Apstaction.Interfaces;
using Oracle.ManagedDataAccess.Client;


namespace Oracle.Infractructure.Implementation
{
    public class FolderStagingRepository : OracleRepository<FolderStaging, long>, IFolderStagingRepository
    {
        public FolderStagingRepository(OracleConnection connection, OracleTransaction transaction) : base(connection, transaction)
        {
        }
    }
}
