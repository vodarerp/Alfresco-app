using Alfresco.Contracts.Oracle.Models;
using Oracle.Apstaction.Interfaces;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Oracle.Infractructure.Implementation
{
    public class DocStagingRepository : OracleRepository<DocStaging, long>,  IDocStagingRepository
    {
        public DocStagingRepository(OracleConnection connection, OracleTransaction transaction) : base(connection, transaction)
        {
        }
    }
}
