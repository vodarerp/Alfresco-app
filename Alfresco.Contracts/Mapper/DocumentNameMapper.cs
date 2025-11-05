using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Mapper
{
    /// <summary>
    /// Mapper za nazive dokumenata koji koristi podatke iz HeimdallDocumentMapper
    /// Dokumenti sa sufiksom "-migracija" će biti migrirani kao NEAKTIVNI (status "poništen")
    /// Dokumenti koji NISU sa sufiksom ostaju aktivni (status "validiran")
    /// </summary>
    public static class DocumentNameMapper
    {
        public static string GetMigratedName(string originalName)
        {
            return HeimdallDocumentMapper.GetMigratedName(originalName);
        }

        public static bool WillReceiveMigrationSuffix(string originalName)
        {
            return HeimdallDocumentMapper.WillReceiveMigrationSuffix(originalName);
        }

        /// <summary>
        /// Vraća srpski naziv dokumenta
        /// </summary>
        public static string GetSerbianName(string originalName)
        {
            return HeimdallDocumentMapper.GetSerbianName(originalName);
        }

        /// <summary>
        /// Vraća tip dosijea za dokument
        /// </summary>
        public static string GetDossierType(string originalName)
        {
            return HeimdallDocumentMapper.GetDossierType(originalName);
        }
    }
}
