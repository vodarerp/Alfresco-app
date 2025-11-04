using System;

namespace Migration.Infrastructure.Implementation.Helpers
{
    /// <summary>
    /// Determines the destination root folder for document migration based on:
    /// - ecm:tipDokumenta (document type)
    /// - ecm:tipDosijea (dossier type)
    /// - ecm:clientSegment (client segment)
    ///
    /// Priority logic:
    /// 1. Deposit documents → DOSSIERS-D
    /// 2. Account Package documents → DOSSIERS-ACC
    /// 3. Based on clientSegment → DOSSIERS-PI or DOSSIERS-LE
    /// 4. Based on tipDosijea → DOSSIERS-PI or DOSSIERS-LE
    /// 5. Fallback → DOSSIERS-UNKNOWN
    /// </summary>
    public static class DestinationRootFolderDeterminator
    {
        // Root folder constants
        public const string DOSSIERS_PI = "DOSSIERS-PI";
        public const string DOSSIERS_LE = "DOSSIERS-LE";
        public const string DOSSIERS_ACC = "DOSSIERS-ACC";
        public const string DOSSIERS_D = "DOSSIERS-D";
        public const string DOSSIERS_UNKNOWN = "DOSSIERS-UNKNOWN";

        /// <summary>
        /// Determines the destination root folder for document migration
        /// </summary>
        /// <param name="tipDokumenta">ecm:tipDokumenta - Document type code (e.g., "00834", "00110")</param>
        /// <param name="tipDosijea">ecm:tipDosijea - Dossier type description (e.g., "Dosije depozita", "Dosije paket računa")</param>
        /// <param name="clientSegment">ecm:clientSegment - Client segment (e.g., "PI", "LE", "FL", "PL")</param>
        /// <returns>Destination root folder name</returns>
        public static string DetermineRootFolder(
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
                return DOSSIERS_D;
            }

            // ========================================
            // PRIORITY 2: Account Package documents
            // ========================================
            // 00834 = Account Package
            // 00102 = Account Package related
            if (tipDokumenta == "00834" || tipDokumenta == "00102")
            {
                return DOSSIERS_ACC;
            }

            if (!string.IsNullOrWhiteSpace(tipDosijea) &&
                tipDosijea.Contains("Dosije paket računa", StringComparison.OrdinalIgnoreCase))
            {
                return DOSSIERS_ACC;
            }

            // ========================================
            // PRIORITY 3: Based on clientSegment
            // ========================================
            if (!string.IsNullOrWhiteSpace(clientSegment))
            {
                var segment = clientSegment.ToUpperInvariant();

                // PI (Personal Individual) or FL (Fizička Lica)
                if (segment == "PI" || segment == "FL")
                {
                    return DOSSIERS_PI;
                }

                // LE (Legal Entity) or PL (Pravna Lica)
                if (segment == "LE" || segment == "PL")
                {
                    return DOSSIERS_LE;
                }
            }

            // ========================================
            // PRIORITY 4: Based on tipDosijea
            // ========================================
            if (!string.IsNullOrWhiteSpace(tipDosijea))
            {
                // Check for PI/FL indicators
                if (tipDosijea.Contains("fizičkog lica", StringComparison.OrdinalIgnoreCase) ||
                    tipDosijea.Contains("fizickog lica", StringComparison.OrdinalIgnoreCase) ||
                    tipDosijea.Contains("klijenta FL", StringComparison.OrdinalIgnoreCase) ||
                    tipDosijea.Contains("PI", StringComparison.OrdinalIgnoreCase))
                {
                    return DOSSIERS_PI;
                }

                // Check for LE/PL indicators
                if (tipDosijea.Contains("pravnog lica", StringComparison.OrdinalIgnoreCase) ||
                    tipDosijea.Contains("klijenta PL", StringComparison.OrdinalIgnoreCase) ||
                    tipDosijea.Contains("LE", StringComparison.OrdinalIgnoreCase))
                {
                    return DOSSIERS_LE;
                }
            }

            // ========================================
            // FALLBACK: Unable to determine
            // ========================================
            return DOSSIERS_UNKNOWN;
        }

        /// <summary>
        /// Checks if the destination requires a different root folder than the source
        /// </summary>
        /// <param name="oldRootFolder">Source root folder (e.g., "DOSSIERS-LE")</param>
        /// <param name="newRootFolder">Destination root folder (e.g., "DOSSIERS-ACC")</param>
        /// <returns>True if root folder changes, false otherwise</returns>
        public static bool IsRootFolderChanging(string oldRootFolder, string newRootFolder)
        {
            if (string.IsNullOrWhiteSpace(oldRootFolder) || string.IsNullOrWhiteSpace(newRootFolder))
                return false;

            return !oldRootFolder.Equals(newRootFolder, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extracts the prefix for new dossier ID from root folder name
        /// </summary>
        /// <param name="rootFolder">Root folder name (e.g., "DOSSIERS-ACC", "DOSSIERS-PI")</param>
        /// <returns>Prefix for dossier ID (e.g., "ACC", "PI")</returns>
        /// <example>
        /// GetPrefixFromRootFolder("DOSSIERS-ACC") → "ACC"
        /// GetPrefixFromRootFolder("DOSSIERS-PI") → "PI"
        /// GetPrefixFromRootFolder("DOSSIERS-D") → "D"
        /// </example>
        public static string GetPrefixFromRootFolder(string rootFolder)
        {
            if (string.IsNullOrWhiteSpace(rootFolder))
                return string.Empty;

            // Remove "DOSSIERS-" prefix
            return rootFolder.Replace("DOSSIERS-", "", StringComparison.OrdinalIgnoreCase)
                             .ToUpperInvariant();
        }
    }
}
