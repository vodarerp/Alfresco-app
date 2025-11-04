using System;
using System.Linq;

namespace Migration.Infrastructure.Implementation.Helpers
{
    /// <summary>
    /// Handles dossier ID format conversion between old and new Alfresco systems
    ///
    /// Standard dossiers:
    /// - OLD format: {Prefix}-{CoreId} (with hyphen) - Example: PI-102206, LE-500342
    /// - NEW format: {Prefix}{CoreId} (without hyphen) - Example: PI102206, LE500342
    ///
    /// Deposit dossiers:
    /// - NEW format: DE{CoreId}-{ProductType}_{ContractNumber}
    /// - Example: DE500342-00008_12345
    /// </summary>
    public static class DossierIdFormatter
    {
        /// <summary>
        /// Converts OLD dossier format to NEW format by removing the hyphen
        /// </summary>
        /// <param name="oldDossierId">Old dossier ID with hyphen (e.g., "PI-102206")</param>
        /// <returns>New dossier ID without hyphen (e.g., "PI102206")</returns>
        /// <example>
        /// ConvertToNewFormat("PI-102206") → "PI102206"
        /// ConvertToNewFormat("LE-500342") → "LE500342"
        /// ConvertToNewFormat("ACC-13001926") → "ACC13001926"
        /// </example>
        public static string ConvertToNewFormat(string oldDossierId)
        {
            if (string.IsNullOrWhiteSpace(oldDossierId))
                return string.Empty;

            return oldDossierId.Replace("-", "");
        }

        /// <summary>
        /// Extracts the CoreId (numeric part) from dossier ID
        /// Works with both OLD and NEW formats
        /// </summary>
        /// <param name="dossierId">Dossier ID in any format</param>
        /// <returns>Core ID (numeric part)</returns>
        /// <example>
        /// ExtractCoreId("PI-102206") → "102206"
        /// ExtractCoreId("PI102206") → "102206"
        /// ExtractCoreId("ACC-13001926") → "13001926"
        /// ExtractCoreId("LE500342") → "500342"
        /// </example>
        public static string ExtractCoreId(string dossierId)
        {
            if (string.IsNullOrWhiteSpace(dossierId))
                return string.Empty;

            // Remove hyphen first to normalize
            var normalized = dossierId.Replace("-", "");

            // Extract all digits starting from the first digit
            var coreId = new string(normalized.SkipWhile(c => !char.IsDigit(c)).ToArray());

            return coreId;
        }

        /// <summary>
        /// Extracts the prefix (letter part) from dossier ID
        /// Works with both OLD and NEW formats
        /// </summary>
        /// <param name="dossierId">Dossier ID in any format</param>
        /// <returns>Prefix in uppercase (e.g., "PI", "LE", "ACC")</returns>
        /// <example>
        /// ExtractPrefix("PI-102206") → "PI"
        /// ExtractPrefix("pi102206") → "PI"
        /// ExtractPrefix("ACC-13001926") → "ACC"
        /// ExtractPrefix("LE500342") → "LE"
        /// </example>
        public static string ExtractPrefix(string dossierId)
        {
            if (string.IsNullOrWhiteSpace(dossierId))
                return string.Empty;

            // Extract all non-digit, non-hyphen characters from the beginning
            var prefix = new string(dossierId.TakeWhile(c => !char.IsDigit(c) && c != '-').ToArray());

            return prefix.ToUpperInvariant();
        }

        /// <summary>
        /// Creates a NEW dossier ID from prefix and CoreId
        /// </summary>
        /// <param name="prefix">Dossier prefix (e.g., "PI", "LE", "ACC")</param>
        /// <param name="coreId">Core ID (numeric part)</param>
        /// <returns>New dossier ID without hyphen</returns>
        /// <example>
        /// CreateNewDossierId("ACC", "500342") → "ACC500342"
        /// CreateNewDossierId("pi", "102206") → "PI102206"
        /// CreateNewDossierId("D", "500342") → "D500342"
        /// </example>
        public static string CreateNewDossierId(string prefix, string coreId)
        {
            if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(coreId))
                return string.Empty;

            return $"{prefix.ToUpperInvariant()}{coreId}";
        }

        /// <summary>
        /// Checks if dossier ID is in OLD format (contains hyphen)
        /// </summary>
        /// <param name="dossierId">Dossier ID to check</param>
        /// <returns>True if OLD format (with hyphen), false otherwise</returns>
        /// <example>
        /// IsOldFormat("PI-102206") → true
        /// IsOldFormat("PI102206") → false
        /// </example>
        public static bool IsOldFormat(string dossierId)
        {
            if (string.IsNullOrWhiteSpace(dossierId))
                return false;

            return dossierId.Contains("-");
        }

        /// <summary>
        /// Checks if dossier ID is in NEW format (no hyphen)
        /// </summary>
        /// <param name="dossierId">Dossier ID to check</param>
        /// <returns>True if NEW format (without hyphen), false otherwise</returns>
        /// <example>
        /// IsNewFormat("PI102206") → true
        /// IsNewFormat("PI-102206") → false
        /// </example>
        public static bool IsNewFormat(string dossierId)
        {
            if (string.IsNullOrWhiteSpace(dossierId))
                return false;

            return !dossierId.Contains("-");
        }

        /// <summary>
        /// Creates deposit dossier ID in the format: DE{CoreId}-{ProductType}_{ContractNumber}
        /// </summary>
        /// <param name="coreId">Client Core ID</param>
        /// <param name="productType">Product type code (e.g., "00008")</param>
        /// <param name="contractNumber">Contract number</param>
        /// <returns>Deposit dossier ID</returns>
        /// <example>
        /// CreateDepositDossierId("500342", "00008", "12345") → "DE500342-00008_12345"
        /// CreateDepositDossierId("102206", "00010", "67890") → "DE102206-00010_67890"
        /// </example>
        public static string CreateDepositDossierId(string coreId, string productType, string contractNumber)
        {
            if (string.IsNullOrWhiteSpace(coreId) ||
                string.IsNullOrWhiteSpace(productType) ||
                string.IsNullOrWhiteSpace(contractNumber))
            {
                return string.Empty;
            }

            return $"DE{coreId}-{productType}_{contractNumber}";
        }

        /// <summary>
        /// Checks if a dossier ID is a deposit dossier (starts with "DE")
        /// </summary>
        /// <param name="dossierId">Dossier ID to check</param>
        /// <returns>True if deposit dossier, false otherwise</returns>
        /// <example>
        /// IsDepositDossier("DE500342-00008_12345") → true
        /// IsDepositDossier("PI102206") → false
        /// IsDepositDossier("LE500342") → false
        /// </example>
        public static bool IsDepositDossier(string? dossierId)
        {
            if (string.IsNullOrWhiteSpace(dossierId))
                return false;

            return dossierId.StartsWith("DE", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Parses deposit dossier ID and extracts its components
        /// </summary>
        /// <param name="depositDossierId">Deposit dossier ID (e.g., "DE500342-00008_12345")</param>
        /// <returns>Tuple with (CoreId, ProductType, ContractNumber) or null if invalid format</returns>
        /// <example>
        /// ParseDepositDossierId("DE500342-00008_12345") → ("500342", "00008", "12345")
        /// ParseDepositDossierId("PI102206") → null
        /// </example>
        public static (string CoreId, string ProductType, string ContractNumber)? ParseDepositDossierId(string depositDossierId)
        {
            if (string.IsNullOrWhiteSpace(depositDossierId) || !IsDepositDossier(depositDossierId))
                return null;

            try
            {
                // Expected format: DE{CoreId}-{ProductType}_{ContractNumber}
                // Example: DE500342-00008_12345

                // Remove "DE" prefix
                var withoutPrefix = depositDossierId.Substring(2);

                // Split by "-" to get CoreId and rest
                var parts = withoutPrefix.Split('-');
                if (parts.Length != 2)
                    return null;

                var coreId = parts[0];

                // Split second part by "_" to get ProductType and ContractNumber
                var secondParts = parts[1].Split('_');
                if (secondParts.Length != 2)
                    return null;

                var productType = secondParts[0];
                var contractNumber = secondParts[1];

                return (coreId, productType, contractNumber);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts CoreId from deposit dossier ID
        /// </summary>
        /// <param name="depositDossierId">Deposit dossier ID</param>
        /// <returns>Core ID or empty string if invalid</returns>
        /// <example>
        /// ExtractCoreIdFromDeposit("DE500342-00008_12345") → "500342"
        /// </example>
        public static string ExtractCoreIdFromDeposit(string depositDossierId)
        {
            var parsed = ParseDepositDossierId(depositDossierId);
            return parsed?.CoreId ?? string.Empty;
        }

        /// <summary>
        /// Extracts ProductType from deposit dossier ID
        /// </summary>
        /// <param name="depositDossierId">Deposit dossier ID</param>
        /// <returns>Product type or empty string if invalid</returns>
        /// <example>
        /// ExtractProductTypeFromDeposit("DE500342-00008_12345") → "00008"
        /// </example>
        public static string ExtractProductTypeFromDeposit(string depositDossierId)
        {
            var parsed = ParseDepositDossierId(depositDossierId);
            return parsed?.ProductType ?? string.Empty;
        }

        /// <summary>
        /// Extracts ContractNumber from deposit dossier ID
        /// </summary>
        /// <param name="depositDossierId">Deposit dossier ID</param>
        /// <returns>Contract number or empty string if invalid</returns>
        /// <example>
        /// ExtractContractNumberFromDeposit("DE500342-00008_12345") → "12345"
        /// </example>
        public static string ExtractContractNumberFromDeposit(string depositDossierId)
        {
            var parsed = ParseDepositDossierId(depositDossierId);
            return parsed?.ContractNumber ?? string.Empty;
        }
    }
}
