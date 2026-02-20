using Alfresco.Contracts.Extensions;
using Alfresco.Contracts.Models;
using System;
using System.Linq;

namespace Alfresco.Contracts.Mapper
{   
    public static class DossierIdFormatter
    {        
        public static string ConvertToNewFormat(string oldDossierId)
        {
            if (string.IsNullOrWhiteSpace(oldDossierId))
                return string.Empty;

            // If already has hyphen, return as-is
            if (oldDossierId.Contains("-"))
                return oldDossierId.ToUpperInvariant();

            // Extract prefix and CoreId, then recombine with hyphen
            var prefix = ExtractPrefix(oldDossierId);
            var coreId = ExtractCoreId(oldDossierId);

            if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(coreId))
                return oldDossierId.ToUpperInvariant(); // Fallback to original

            return CreateNewDossierId(prefix, coreId);
        }
        
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
       
        public static string ExtractPrefix(string dossierId)
        {
            if (string.IsNullOrWhiteSpace(dossierId))
                return string.Empty;

            // Extract all non-digit, non-hyphen characters from the beginning
            var prefix = new string(dossierId.TakeWhile(c => !char.IsDigit(c) && c != '-').ToArray());

            return prefix.ToUpperInvariant();
        }
        
        public static string CreateNewDossierId(string prefix, string coreId)
        {
            if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(coreId))
                return string.Empty;

            return $"{prefix.ToUpperInvariant()}-{coreId}";
        }
       
        public static bool IsOldFormat(string dossierId)
        {
            if (string.IsNullOrWhiteSpace(dossierId))
                return false;

            return !dossierId.Contains("-");
        }
       
        public static bool IsNewFormat(string dossierId)
        {
            if (string.IsNullOrWhiteSpace(dossierId))
                return false;

            return dossierId.Contains("-");
        }

        /// <summary>
        /// Maps client segment (PI/LE) to product type code (00008/00010)
        /// Used for deposit dossiers
        /// </summary>
        /// <param name="clientSegment">Client segment from ecm:docClientType ("PI", "LE", "FL", "PL")</param>
        /// <returns>Product type code: "00008" for PI/FL, "00010" for LE/PL</returns>
        public static string MapClientSegmentToProductType(string? clientSegment)
        {
            if (string.IsNullOrWhiteSpace(clientSegment))
                return "00008"; // Default to PI

            return clientSegment.ToUpperInvariant() switch
            {
                "PI" => "00008",  // Fizička lica - Depozitni proizvodi
                "FL" => "00008",  // Fizička lica - alternative
                "LE" => "00010",  // SB - Depozitni proizvodi (Pravna lica)
                "PL" => "00010",  // Pravna lica - alternative
                _ => "00008"      // Default to PI
            };
        }
       
        public static string CreateDepositDossierId(string coreId, string? productType, string? contractNumber, string? folderName = null, DateTime? docCreated = null)
        {
            // Determine productType from folderName if not provided
            // PI/FL folders → 00008 (Fizička lica - Depozitni proizvodi)
            // LE/PL folders → 00010 (SB - Depozitni proizvodi / Pravna lica)
            if (string.IsNullOrWhiteSpace(productType) && !string.IsNullOrWhiteSpace(folderName))
            {
                var prefix = ExtractPrefix(folderName);
                productType = prefix.ToUpperInvariant() switch
                {
                    "PI" => "00008",  // Fizička lica - Depozitni proizvodi
                    "FL" => "00008",  // Fizička lica - alternative prefix
                    "LE" => "00010",  // SB - Depozitni proizvodi (Pravna lica)
                    "PL" => "00010",  // Pravna lica - alternative prefix
                    _ => "00008"      // Default to PI if unknown (was 000001 before)
                };
            }

            //if (string.IsNullOrWhiteSpace(productType)) productType = "00008"; // Default to PI
            //if (string.IsNullOrWhiteSpace(contractNumber)) contractNumber = "20250102";
                if (string.IsNullOrWhiteSpace(contractNumber))
                {
                    // Generate contract number based on docCreated date or use a default
                    contractNumber = docCreated.HasValue ? docCreated.Value.ToString("yyyyMMdd") : "";
            }

            return $"DE-{coreId}-{productType}-{contractNumber}";
        }
       
        public static bool IsDepositDossier(string? dossierId)
        {
            if (string.IsNullOrWhiteSpace(dossierId))
                return false;

            return dossierId.StartsWith("DE", StringComparison.OrdinalIgnoreCase);
        }
       
        public static (string CoreId, string ProductType, string ContractNumber)? ParseDepositDossierId(string depositDossierId)
        {
            if (string.IsNullOrWhiteSpace(depositDossierId) || !IsDepositDossier(depositDossierId))
                return null;

            try
            {
                // Expected format: DE-{CoreId}-{ProductType}_{ContractNumber} (NEW)
                //              or: DE{CoreId}-{ProductType}_{ContractNumber} (OLD)
                // Example: DE-500342-00008_12345 or DE500342-00008_12345

                // Remove "DE" or "DE-" prefix
                var withoutPrefix = depositDossierId.StartsWith("DE-", StringComparison.OrdinalIgnoreCase)
                    ? depositDossierId.Substring(3)  // "DE-500342-00008_12345" → "500342-00008_12345"
                    : depositDossierId.Substring(2);  // "DE500342-00008_12345" → "500342-00008_12345"

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

       
        public static string ExtractCoreIdFromDeposit(string depositDossierId)
        {
            var parsed = ParseDepositDossierId(depositDossierId);
            return parsed?.CoreId ?? string.Empty;
        }

       
        public static string ExtractProductTypeFromDeposit(string depositDossierId)
        {
            var parsed = ParseDepositDossierId(depositDossierId);
            return parsed?.ProductType ?? string.Empty;
        }

        public static string ExtractContractNumberFromDeposit(string depositDossierId)
        {
            var parsed = ParseDepositDossierId(depositDossierId);
            return parsed?.ContractNumber ?? string.Empty;
        }

       
        public static string ConvertForTargetType(string folderName, int targetDossierType, string? contracNumber, string? productType, string? coreId, DateTime? docCreated)
        {
            if (string.IsNullOrWhiteSpace(folderName))
                return string.Empty;

            // Extract CoreId from the old dossier ID
            //var coreId = ExtractCoreId(oldDossierId);
            if (string.IsNullOrWhiteSpace(coreId)) coreId = ClientPropertiesExtensions.TryExtractCoreIdFromName(folderName);

            if (string.IsNullOrWhiteSpace(coreId))
                return ConvertToNewFormat(folderName); // Fallback to simple conversion

            // Determine the new prefix based on target dossier type
            string newPrefix = targetDossierType switch
            {
                300 => "ACC",  // Account Package dossier
                400 => "LE",   // Legal Entity dossier
                500 => "PI",   // Physical Individual dossier
                700 => "DE",   // Deposit dossier
                _ =>   "O"//ExtractPrefix(oldDossierId) // Keep original prefix for unknown types
            };

            string toRet = targetDossierType switch
            {
                700 => CreateDepositDossierId(coreId, productType, contracNumber, folderName, docCreated),   // Deposit dossier - pass folderName to determine productType
                _ => CreateNewDossierId(newPrefix, coreId)//ExtractPrefix(oldDossierId) // Keep original prefix for unknown types
            };


            return toRet;
            //return CreateNewDossierId(newPrefix, coreId);
        }

        
        
    }
}
