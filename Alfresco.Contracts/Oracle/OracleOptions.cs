using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Oracle
{
    public class OracleOptions
    {
        public string ConnectionString { get; init; } = string.Empty;
        public int CommandTimeoutSeconds { get; init; } = 120;

        public int BulkBatchSize { get; init; } = 1000;
    }
}
