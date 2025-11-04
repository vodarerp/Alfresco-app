using System;

namespace Migration.Infrastructure.Implementation.Helpers
{
    /// <summary>
    /// Detects document status (active/inactive) based on:
    /// - Suffix "-migracija" in ecm:opisDokumenta (description)
    /// - Existing ecm:status from old Alfresco system
    ///
    /// Status mapping:
    /// - "validiran" = Active document
    /// - "poništen" = Cancelled/Inactive document
    /// </summary>
    public static class DocumentStatusDetector
    {
        // Alfresco status constants
        public const string STATUS_VALIDIRAN = "validiran";
        public const string STATUS_PONISTEN = "poništen";

        /// <summary>
        /// Determines if a document should be active after migration
        /// </summary>
        /// <param name="opisDokumenta">ecm:opisDokumenta - Document description</param>
        /// <param name="existingStatus">ecm:status - Existing status from old Alfresco (optional)</param>
        /// <returns>True if document should be active, false if it should be inactive</returns>
        public static bool ShouldBeActive(
            string? opisDokumenta,
            string? existingStatus = null)
        {
            // ========================================
            // TC 11: Check existing status from old system
            // ========================================
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

            // ========================================
            // TC 1 & 2: Check for "-migracija" suffix in DESCRIPTION
            // ========================================
            if (!string.IsNullOrWhiteSpace(opisDokumenta))
            {
                // Check both " - migracija" and "-migracija" variants
                if (opisDokumenta.Contains(" - migracija", StringComparison.OrdinalIgnoreCase) ||
                    opisDokumenta.Contains("-migracija", StringComparison.OrdinalIgnoreCase))
                {
                    return false; // Inactive if has migration suffix
                }
            }

            // Default: Active
            return true;
        }

        /// <summary>
        /// Gets the Alfresco status string based on active/inactive flag
        /// </summary>
        /// <param name="isActive">True for active, false for inactive</param>
        /// <returns>"validiran" or "poništen"</returns>
        public static string GetAlfrescoStatus(bool isActive)
        {
            return isActive ? STATUS_VALIDIRAN : STATUS_PONISTEN;
        }

        /// <summary>
        /// Returns complete status information for a document
        /// </summary>
        /// <param name="opisDokumenta">ecm:opisDokumenta - Document description</param>
        /// <param name="existingStatus">ecm:status - Existing status from old Alfresco (optional)</param>
        /// <returns>DocumentStatusInfo with all status details</returns>
        public static DocumentStatusInfo GetStatusInfo(
            string? opisDokumenta,
            string? existingStatus = null)
        {
            var isActive = ShouldBeActive(opisDokumenta, existingStatus);
            var status = GetAlfrescoStatus(isActive);

            var hasMigrationSuffix = !string.IsNullOrWhiteSpace(opisDokumenta) &&
                (opisDokumenta.Contains("- migracija", StringComparison.OrdinalIgnoreCase) ||
                 opisDokumenta.Contains("-migracija", StringComparison.OrdinalIgnoreCase));

            var wasInactiveInOldSystem = !string.IsNullOrWhiteSpace(existingStatus) &&
                (existingStatus.Contains("poništen", StringComparison.OrdinalIgnoreCase) ||
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
        /// Checks if a document description has the "-migracija" suffix
        /// </summary>
        /// <param name="opisDokumenta">Document description</param>
        /// <returns>True if has migration suffix, false otherwise</returns>
        public static bool HasMigrationSuffix(string? opisDokumenta)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return false;

            return opisDokumenta.Contains("- migracija", StringComparison.OrdinalIgnoreCase) ||
                   opisDokumenta.Contains("-migracija", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Contains detailed status information for a document
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
