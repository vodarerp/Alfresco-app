using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Mapper
{
    public static class DocumentStatusDetector
    {
        /// <summary>
        /// Određuje da li dokument treba da bude aktivan NAKON migracije
        /// Logika:
        /// - TC 1: Ako dokument dobija sufiks "-migracija" → NEAKTIVAN (status "poništen")
        /// - TC 2: Ako dokument NE dobija sufiks → AKTIVAN (status "validiran")
        /// - TC 11: Ako je dokument već bio neaktivan u starom sistemu → ostaje NEAKTIVAN
        /// </summary>
        public static bool ShouldBeActiveAfterMigration(
            string originalDocumentName,
            string? existingStatus = null)
        {
            // TC 11: Provera starog statusa
            if (!string.IsNullOrWhiteSpace(existingStatus))
            {
                var normalized = existingStatus.Trim().ToLowerInvariant();
                if (normalized == "poništen" ||
                    normalized == "ponisten" ||
                    normalized == "inactive" ||
                    normalized == "cancelled" ||
                    normalized == "canceled")
                    return false;
            }

            // TC 1 & 2: Provera da li dobija sufiks "-migracija"
            bool willReceiveSuffix = DocumentNameMapper.WillReceiveMigrationSuffix(originalDocumentName);

            return !willReceiveSuffix;
        }

        public static string GetAlfrescoStatus(bool isActive)
        {
            return isActive ? "validiran" : "poništen";
        }

        /// <summary>
        /// Vraća kompletne informacije o migraciji dokumenta
        /// </summary>
        public static DocumentMigrationInfo GetMigrationInfo(
            string originalName,
            string? originalCode = null,
            string? existingStatus = null)
        {
            var newName = DocumentNameMapper.GetMigratedName(originalName);
            var newCode = originalCode != null
                ? DocumentCodeMapper.GetMigratedCode(originalCode)
                : null;

            var isActive = ShouldBeActiveAfterMigration(originalName, existingStatus);
            var status = GetAlfrescoStatus(isActive);

            var willReceiveSuffix = DocumentNameMapper.WillReceiveMigrationSuffix(originalName);
            var codeWillChange = originalCode != null && DocumentCodeMapper.CodeWillChange(originalCode);

            return new DocumentMigrationInfo
            {
                OriginalName = originalName,
                NewName = newName,
                OriginalCode = originalCode,
                NewCode = newCode,
                IsActive = isActive,
                Status = status,
                WillReceiveMigrationSuffix = willReceiveSuffix,
                CodeWillChange = codeWillChange
            };
        }
    }

    public record DocumentMigrationInfo
    {
        public string OriginalName { get; init; } = string.Empty;
        public string NewName { get; init; } = string.Empty;
        public string? OriginalCode { get; init; }
        public string? NewCode { get; init; }
        public bool IsActive { get; init; }
        public string Status { get; init; } = string.Empty;
        public bool WillReceiveMigrationSuffix { get; init; }
        public bool CodeWillChange { get; init; }
    }
}
