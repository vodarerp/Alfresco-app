using Alfresco.Contracts.Enums;
using System;

namespace Alfresco.Contracts.Mapper
{
    /// <summary>
    /// Determines the destination dossier type for document migration based on:
    /// - ecm:tipDokumenta (document type code)
    /// - ecm:tipDosijea (dossier type description)
    /// - ecm:clientSegment (client segment)
    ///
    /// Priority logic:
    /// 1. Deposit documents → Deposit (700)
    /// 2. Account Package documents → AccountPackage (300)
    /// 3. Based on clientSegment → ClientFL (500) or ClientPL (400)
    /// 4. Based on tipDosijea → ClientFL (500) or ClientPL (400)
    /// 5. Fallback → Unknown (999)
    /// </summary>
    public static class DestinationRootFolderDeterminator
    {
        /// <summary>
        /// Determines the destination dossier type for document migration
        /// </summary>
        /// <param name="tipDokumenta">ecm:tipDokumenta - Document type code (e.g., "00834", "00110")</param>
        /// <param name="tipDosijea">ecm:tipDosijea - Dossier type description (e.g., "Dosije depozita", "Dosije paket računa")</param>
        /// <param name="clientSegment">ecm:clientSegment - Client segment (e.g., "PI", "LE", "FL", "PL")</param>
        /// <returns>DossierType enum value</returns>
        public static DossierType DetermineDestinationDossierType(
            string? tipDokumenta,
            string? tipDosijea,
            string? clientSegment)
        {
            // ========================================
            // PRIORITY 1: Deposit documents
            // ========================================
            if (!string.IsNullOrWhiteSpace(tipDosijea) &&
                tipDosijea.Contains("Dosije depozita", StringComparison.OrdinalIgnoreCase))
            {
                return DossierType.Deposit; // 700
            }

            // ========================================
            // PRIORITY 2: Account Package documents
            // ========================================
            // 00834 = Account Package
            // 00102 = Account Package related
            if (tipDokumenta == "00834" || tipDokumenta == "00102")
            {
                return DossierType.AccountPackage; // 300
            }

            if (!string.IsNullOrWhiteSpace(tipDosijea) &&
                (tipDosijea.Contains("Dosije paket računa", StringComparison.OrdinalIgnoreCase) ||
                 tipDosijea.Contains("Dosije paket racuna", StringComparison.OrdinalIgnoreCase)))
            {
                return DossierType.AccountPackage; // 300
            }

            // ========================================
            // PRIORITY 3: Based on clientSegment
            // ========================================
            if (!string.IsNullOrWhiteSpace(clientSegment))
            {
                var segment = clientSegment.ToUpperInvariant();

                // PI (Personal Individual) or FL (Fizička Lica) or RETAIL
                if (segment == "PI" || segment == "FL" || segment == "RETAIL")
                {
                    return DossierType.ClientFL; // 500
                }

                // LE (Legal Entity) or PL (Pravna Lica) or SME or CORPORATE
                if (segment == "LE" || segment == "PL" || segment == "SME" || segment == "CORPORATE")
                {
                    return DossierType.ClientPL; // 400
                }
            }

            // ========================================
            // PRIORITY 4: Based on tipDosijea
            // ========================================
            if (!string.IsNullOrWhiteSpace(tipDosijea))
            {
                var normalizedTipDosijea = tipDosijea.ToLowerInvariant();

                // Check for FL/PI indicators
                if (normalizedTipDosijea.Contains("fizičkog lica") ||
                    normalizedTipDosijea.Contains("fizickog lica") ||
                    normalizedTipDosijea.Contains("klijenta fl") ||
                    normalizedTipDosijea.Contains("dosije klijenta fl / pl"))
                {
                    // If it's "FL / PL", return unresolved type to be resolved later
                    if (normalizedTipDosijea.Contains("fl / pl") || normalizedTipDosijea.Contains("fl/pl"))
                    {
                        return DossierType.ClientFLorPL; // -1 (needs clientSegment)
                    }
                    return DossierType.ClientFL; // 500
                }

                // Check for PL/LE indicators
                if (normalizedTipDosijea.Contains("pravnog lica") ||
                    normalizedTipDosijea.Contains("klijenta pl") ||
                    normalizedTipDosijea.Contains("klijenta le"))
                {
                    return DossierType.ClientPL; // 400
                }
            }

            // ========================================
            // FALLBACK: Unable to determine
            // ========================================
            return DossierType.Unknown; // 999
        }

        /// <summary>
        /// Determines destination dossier type and resolves FL/PL if needed
        /// </summary>
        /// <param name="tipDokumenta">Document type code</param>
        /// <param name="tipDosijea">Dossier type description</param>
        /// <param name="clientSegment">Client segment</param>
        /// <returns>Resolved DossierType (never returns ClientFLorPL)</returns>
        public static DossierType DetermineAndResolve(
            string? tipDokumenta,
            string? tipDosijea,
            string? clientSegment)
        {
            var dossierType = DetermineDestinationDossierType(tipDokumenta, tipDosijea, clientSegment);

            // If unresolved FL/PL, try to resolve using clientSegment
            if (dossierType == DossierType.ClientFLorPL && !string.IsNullOrWhiteSpace(clientSegment))
            {
                return DossierTypeDetector.ResolveFLorPL(clientSegment);
            }

            // If still unresolved or Other, return Unknown
            if (dossierType == DossierType.ClientFLorPL || dossierType == DossierType.Other)
            {
                return DossierType.Unknown;
            }

            return dossierType;
        }

        /// <summary>
        /// Checks if the destination dossier type is different from the source
        /// </summary>
        /// <param name="sourceDossierType">Source dossier type</param>
        /// <param name="destinationDossierType">Destination dossier type</param>
        /// <returns>True if dossier type changes, false otherwise</returns>
        public static bool IsDossierTypeChanging(DossierType sourceDossierType, DossierType destinationDossierType)
        {
            return sourceDossierType != destinationDossierType;
        }
    }
}
