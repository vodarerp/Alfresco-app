using Alfresco.Contracts.Oracle.Models;
using Migration.Abstraction.Interfaces;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation
{
    /// <summary>
    /// Određuje status dokumenta nakon migracije.
    /// VERZIJA 3.0: Koristi DocumentMappingService koji čita podatke iz SQL tabele DocumentMappings.
    /// </summary>
    public class DocumentStatusDetectorV2
    {
        private readonly IDocumentMappingService _mappingService;

        public DocumentStatusDetectorV2(IDocumentMappingService mappingService)
        {
            _mappingService = mappingService ?? throw new ArgumentNullException(nameof(mappingService));
        }

        /// <summary>
        /// Određuje da li dokument treba da bude aktivan NAKON migracije.
        /// Logika:
        /// - TC 1: Ako dokument dobija sufiks "-migracija" → NEAKTIVAN (status "poništen")
        /// - TC 2: Ako dokument NE dobija sufiks → AKTIVAN (status "validiran")
        /// - TC 11: Ako je dokument već bio neaktivan u starom sistemu → ostaje NEAKTIVAN
        /// </summary>
        public async Task<bool> ShouldBeActiveAfterMigrationAsync(
            string originalDocumentName,
            string? existingStatus = null,
            CancellationToken ct = default)
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
            bool willReceiveSuffix = await _mappingService.WillReceiveMigrationSuffixAsync(originalDocumentName, ct).ConfigureAwait(false);

            return !willReceiveSuffix;
        }

        public static string GetAlfrescoStatus(bool isActive)
        {
            return isActive ? "validiran" : "poništen";
        }

        /// <summary>
        /// Determines if a document should be active based on ecm:opisDokumenta property.
        /// (NEW - Per Analiza_migracije_v2.md)
        ///
        /// Logic:
        /// - TC 11: If document was already inactive in old system → remains INACTIVE
        /// - TC 1 & 2: If ecm:opisDokumenta contains "-migracija" suffix → INACTIVE (status "poništen")
        /// - Otherwise → ACTIVE (status "validiran")
        /// </summary>
        /// <param name="opisDokumenta">ecm:opisDokumenta - Document description</param>
        /// <param name="existingStatus">ecm:status - Existing status from old Alfresco (optional)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>True if document should be active, false if it should be inactive</returns>
        public static bool ShouldBeActiveByOpis(
            string? opisDokumenta,
            string? existingStatus = null)
        {
            // TC 11: Check existing status from old system
            if (!string.IsNullOrWhiteSpace(existingStatus))
            {
                var normalized = existingStatus.Trim().ToLowerInvariant();

                // If already inactive in old system, keep it inactive
                if (normalized == "poništen" ||
                    normalized == "ponisten" ||
                    normalized == "inactive" ||
                    normalized == "cancelled" ||
                    normalized == "canceled")
                {
                    return false;
                }
            }

            // TC 1 & 2: Check for "-migracija" suffix in DESCRIPTION (ecm:opisDokumenta)
            if (!string.IsNullOrWhiteSpace(opisDokumenta))
            {
                // Check both "- migracija" and "-migracija" variants
                if (opisDokumenta.Contains("- migracija", StringComparison.OrdinalIgnoreCase) ||
                    opisDokumenta.Contains("-migracija", StringComparison.OrdinalIgnoreCase))
                {
                    return false; // Inactive if has migration suffix
                }
            }

            // Default: Active
            return true;
        }

        /// <summary>
        /// Returns complete status information for a document based on ecm:opisDokumenta.
        /// (NEW - Per Analiza_migracije_v2.md)
        /// </summary>
        /// <param name="opisDokumenta">ecm:opisDokumenta - Document description</param>
        /// <param name="existingStatus">ecm:status - Existing status from old Alfresco (optional)</param>
        /// <returns>DocumentStatusInfo with all status details</returns>
        public static DocumentStatusInfo GetStatusInfoByOpis(
            string? opisDokumenta,
            string? existingStatus = null)
        {
            var isActive = ShouldBeActiveByOpis(opisDokumenta, existingStatus);
            var status = GetAlfrescoStatus(isActive);

            var hasMigrationSuffix = !string.IsNullOrWhiteSpace(opisDokumenta) &&
                (opisDokumenta.Contains("- migracija", StringComparison.OrdinalIgnoreCase) ||
                 opisDokumenta.Contains("-migracija", StringComparison.OrdinalIgnoreCase));

            var wasInactiveInOldSystem = !string.IsNullOrWhiteSpace(existingStatus) &&
                (existingStatus.Contains("poništen", StringComparison.OrdinalIgnoreCase) ||
                 existingStatus.Contains("ponisten", StringComparison.OrdinalIgnoreCase) ||
                 existingStatus.Contains("inactive", StringComparison.OrdinalIgnoreCase) ||
                 existingStatus.Contains("cancelled", StringComparison.OrdinalIgnoreCase) ||
                 existingStatus.Contains("canceled", StringComparison.OrdinalIgnoreCase));

            return new DocumentStatusInfo
            {
                IsActive = isActive,
                Status = status,
                HasMigrationSuffixInOpis = hasMigrationSuffix,
                WasInactiveInOldSystem = wasInactiveInOldSystem
            };
        }

        /// <summary>
        /// Checks if a document description (ecm:opisDokumenta) has the "-migracija" suffix.
        /// (NEW - Per Analiza_migracije_v2.md)
        /// </summary>
        /// <param name="opisDokumenta">Document description</param>
        /// <returns>True if has migration suffix, false otherwise</returns>
        public static bool HasMigrationSuffixInOpis(string? opisDokumenta)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return false;

            return opisDokumenta.Contains("- migracija", StringComparison.OrdinalIgnoreCase) ||
                   opisDokumenta.Contains("-migracija", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Vraća kompletne informacije o migraciji dokumenta.
        /// NAPOMENA: Ova metoda koristi staru logiku bazirano na imenu dokumenta.
        /// Za novu logiku koristiti GetMigrationInfoByDocDescAsync koja koristi ecm:docDesc.
        /// </summary>
        public async Task<DocumentMigrationInfo> GetMigrationInfoAsync(
            string originalName,
            string? originalCode = null,
            string? existingStatus = null,
            CancellationToken ct = default)
        {
            var newName = await _mappingService.GetMigratedNameAsync(originalName, ct).ConfigureAwait(false);
            var newCode = originalCode != null
                ? await _mappingService.GetMigratedCodeAsync(originalCode, ct).ConfigureAwait(false)
                : null;

            var isActive = await ShouldBeActiveAfterMigrationAsync(originalName, existingStatus, ct).ConfigureAwait(false);
            var status = GetAlfrescoStatus(isActive);

            var willReceiveSuffix = await _mappingService.WillReceiveMigrationSuffixAsync(originalName, ct).ConfigureAwait(false);
            var codeWillChange = originalCode != null && await _mappingService.CodeWillChangeAsync(originalCode, ct).ConfigureAwait(false);

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

        /// <summary>
        /// Vraća kompletne informacije o migraciji dokumenta koristeći ecm:docDesc.
        /// NOVA LOGIKA: Ime dokumenta može biti random GUID, koristimo ecm:docDesc za mapiranje.
        /// ecm:docDesc sadrži vrednost iz polja Naziv ili NazivDokumenta iz DocumentMappings tabele.
        /// </summary>
        /// <param name="docDesc">ecm:docDesc - Opis dokumenta (Naziv ili NazivDokumenta iz tabele)</param>
        /// <param name="originalCode">Originalna šifra dokumenta</param>
        /// <param name="existingStatus">Postojeći status iz starog Alfresco-a</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Informacije o migraciji dokumenta</returns>
        public async Task<DocumentMigrationInfo> GetMigrationInfoByDocDescAsync(
            string docDesc,
            string? originalCode = null,
            string? existingStatus = null,
            CancellationToken ct = default)
        {
            // Pronađi mapping na osnovu ecm:docDesc (može biti Naziv ili NazivDokumenta)
            var mapping = await _mappingService.FindByOriginalNameAsync(docDesc, ct).ConfigureAwait(false);

            // Ako nije pronađen po engleskom nazivu, probaj po srpskom nazivu
            if (mapping == null)
            {
                mapping = await _mappingService.FindBySerbianNameAsync(docDesc, ct).ConfigureAwait(false);
            }

            string newName;
            string newCode;
            string tipDosiea = string.Empty;

            if (mapping != null)
            {
                // Koristimo vrednost iz NazivDokumentaMigracija kao novi naziv
                newName = mapping.NazivDokumentaMigracija ?? docDesc;
                newCode = mapping.SifraDokumentaMigracija ?? originalCode ?? string.Empty;
                tipDosiea = mapping.TipDosijea ?? string.Empty;
            }
            else
            {
                // Ako nema mapiranja, ostavi originalne vrednosti
                newName = docDesc;
                newCode = originalCode ?? string.Empty;
            }

            // Određivanje aktivnosti: provera da li novi naziv ima sufiks "- migracija"
            bool willReceiveSuffix = newName.EndsWith("- migracija", StringComparison.OrdinalIgnoreCase) ||
                                    newName.EndsWith("– migracija", StringComparison.OrdinalIgnoreCase);

            var isActive = ShouldBeActiveByOpis(newName, existingStatus);
            var status = GetAlfrescoStatus(isActive);

            var codeWillChange = mapping != null &&
                                !string.Equals(
                                    mapping.SifraDokumenta?.Trim(),
                                    mapping.SifraDokumentaMigracija?.Trim(),
                                    StringComparison.OrdinalIgnoreCase);

            return new DocumentMigrationInfo
            {
                OriginalName = docDesc,
                NewName = newName,
                OriginalCode = originalCode,
                NewCode = newCode,
                IsActive = isActive,
                Status = status,
                WillReceiveMigrationSuffix = willReceiveSuffix,
                CodeWillChange = codeWillChange,
                TipDosiea = tipDosiea
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
        public string TipDosiea { get; init; } = string.Empty;
    }

    /// <summary>
    /// Contains detailed status information for a document (NEW - Per Analiza_migracije_v2.md)
    /// </summary>
    public record DocumentStatusInfo
    {
        /// <summary>
        /// Indicates if the document is active after migration
        /// </summary>
        public bool IsActive { get; init; }

        /// <summary>
        /// Alfresco status string: "validiran" or "poništen"
        /// </summary>
        public string Status { get; init; } = string.Empty;

        /// <summary>
        /// Indicates if the document description contains "-migracija" suffix
        /// </summary>
        public bool HasMigrationSuffixInOpis { get; init; }

        /// <summary>
        /// Indicates if the document was already inactive in the old Alfresco system
        /// </summary>
        public bool WasInactiveInOldSystem { get; init; }
    }
}
