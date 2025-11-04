using System;

namespace Migration.Infrastructure.Implementation.Helpers
{
    /// <summary>
    /// Determines the document source (ecm:source) based on destination root folder
    ///
    /// Source mapping rules:
    /// - TC 6: DOSSIERS-PI/LE/ACC → "Heimdall"
    /// - TC 7: DOSSIERS-D → "DUT"
    ///
    /// Source indicates the origin system of the document:
    /// - "Heimdall" - Main client onboarding/account system
    /// - "DUT" - Deposit system (Depo kartoni)
    /// </summary>
    public static class SourceDetector
    {
        // Source constants
        public const string SOURCE_HEIMDALL = "Heimdall";
        public const string SOURCE_DUT = "DUT";

        /// <summary>
        /// Determines the ecm:source value based on destination root folder
        /// </summary>
        /// <param name="destinationRootFolder">Destination root folder (e.g., "DOSSIERS-PI", "DOSSIERS-D")</param>
        /// <returns>Source system identifier: "Heimdall" or "DUT"</returns>
        /// <example>
        /// GetSource("DOSSIERS-PI") → "Heimdall"
        /// GetSource("DOSSIERS-LE") → "Heimdall"
        /// GetSource("DOSSIERS-ACC") → "Heimdall"
        /// GetSource("DOSSIERS-D") → "DUT"
        /// GetSource("DOSSIERS-UNKNOWN") → "Heimdall" (default)
        /// </example>
        public static string GetSource(string destinationRootFolder)
        {
            if (string.IsNullOrWhiteSpace(destinationRootFolder))
                return SOURCE_HEIMDALL; // Default to Heimdall

            // TC 7: Deposit documents go to DUT
            if (destinationRootFolder.Equals("DOSSIERS-D", StringComparison.OrdinalIgnoreCase))
            {
                return SOURCE_DUT;
            }

            // TC 6: All other folders (PI, LE, ACC, etc.) go to Heimdall
            return SOURCE_HEIMDALL;
        }

        /// <summary>
        /// Checks if the source is Heimdall
        /// </summary>
        /// <param name="source">Source value to check</param>
        /// <returns>True if source is Heimdall, false otherwise</returns>
        public static bool IsHeimdall(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return false;

            return source.Equals(SOURCE_HEIMDALL, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if the source is DUT
        /// </summary>
        /// <param name="source">Source value to check</param>
        /// <returns>True if source is DUT, false otherwise</returns>
        public static bool IsDUT(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return false;

            return source.Equals(SOURCE_DUT, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines if a destination folder should have Heimdall as source
        /// </summary>
        /// <param name="destinationRootFolder">Destination root folder</param>
        /// <returns>True if destination uses Heimdall, false otherwise</returns>
        public static bool IsHeimdallDestination(string destinationRootFolder)
        {
            return GetSource(destinationRootFolder) == SOURCE_HEIMDALL;
        }

        /// <summary>
        /// Determines if a destination folder should have DUT as source
        /// </summary>
        /// <param name="destinationRootFolder">Destination root folder</param>
        /// <returns>True if destination uses DUT, false otherwise</returns>
        public static bool IsDUTDestination(string destinationRootFolder)
        {
            return GetSource(destinationRootFolder) == SOURCE_DUT;
        }
    }
}
