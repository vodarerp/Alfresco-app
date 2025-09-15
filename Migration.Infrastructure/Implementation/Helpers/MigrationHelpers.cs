using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Helpers
{
    public static class MigrationHelpers
    {
        public static string NormalizeName(this string inString)
        {
            return inString.Replace("-", "").Trim();
        }
    }
}
