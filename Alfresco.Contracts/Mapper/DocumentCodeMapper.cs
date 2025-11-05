using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Mapper
{
    /// <summary>
    /// Mapper za šifre dokumenata koji koristi podatke iz HeimdallDocumentMapper
    /// </summary>
    public static class DocumentCodeMapper
    {
        public static string GetMigratedCode(string originalCode)
        {
            return HeimdallDocumentMapper.GetMigratedCode(originalCode);
        }

        public static bool CodeWillChange(string originalCode)
        {
            return HeimdallDocumentMapper.CodeWillChange(originalCode);
        }
    }
}
