using Migration.Abstraction.Interfaces;
using System;
using System.Text.RegularExpressions;

namespace Migration.Infrastructure.Implementation
{
    /// <summary>
    /// Service for generating and parsing unique folder identifiers for deposit folders.
    /// Per documentation: Specific format required for deposit folder structure.
    /// </summary>
    public class UniqueFolderIdentifierService : IUniqueFolderIdentifierService
    {
        // Regex pattern for validating unique identifier format
        // Format: DE-{CoreId}-{ProductType}-{ContractNumber}_{Timestamp}
        // Example: DE-10194302-00008-10104302_20241105154459
        private static readonly Regex IdentifierPattern = new Regex(
            @"^DE-(?<coreId>\d+)-(?<productType>\d{5})-(?<contractNumber>[^_]+)_(?<timestamp>\d{14})$",
            RegexOptions.Compiled);

        // Regex pattern for folder reference format
        // Format: DE-{CoreId}{ProductType}-{ContractNumber}
        // Example: DE-1019430200008-10104302
        private static readonly Regex FolderReferencePattern = new Regex(
            @"^DE-(?<coreId>\d+)(?<productType>\d{5})-(?<contractNumber>.+)$",
            RegexOptions.Compiled);

        public string GenerateDepositIdentifier(
            string coreId,
            string productType,
            string contractNumber,
            DateTime processDate)
        {
            ValidateParameters(coreId, productType, contractNumber);

            // Per documentation line 161-163:
            // Format: DE-{CoreId}-{ProductType}-{ContractNumber}_{Timestamp}
            // Timestamp format: yyyyMMddHHmmss (14 digits)
            var timestamp = processDate.ToString("yyyyMMddHHmmss");

            return $"DE-{coreId}-{productType}-{contractNumber}_{timestamp}";
        }

        public string GenerateFolderReference(
            string coreId,
            string productType,
            string contractNumber)
        {
            ValidateParameters(coreId, productType, contractNumber);

            // Per documentation line 156:
            // Format: DE-{CoreId}{ProductType}-{ContractNumber}
            // Note: No separator between CoreId and ProductType!
            return $"DE-{coreId}{productType}-{contractNumber}";
        }

        public (string CoreId, string ProductType, string ContractNumber, DateTime? ProcessDate) ParseIdentifier(
            string uniqueIdentifier)
        {
            if (string.IsNullOrWhiteSpace(uniqueIdentifier))
            {
                throw new ArgumentException("Unique identifier cannot be null or empty", nameof(uniqueIdentifier));
            }

            var match = IdentifierPattern.Match(uniqueIdentifier);

            if (!match.Success)
            {
                throw new FormatException(
                    $"Invalid unique identifier format: {uniqueIdentifier}. " +
                    "Expected format: DE-{CoreId}-{ProductType}-{ContractNumber}_{Timestamp}");
            }

            var coreId = match.Groups["coreId"].Value;
            var productType = match.Groups["productType"].Value;
            var contractNumber = match.Groups["contractNumber"].Value;
            var timestampStr = match.Groups["timestamp"].Value;

            DateTime? processDate = null;
            if (DateTime.TryParseExact(
                timestampStr,
                "yyyyMMddHHmmss",
                null,
                System.Globalization.DateTimeStyles.None,
                out var parsedDate))
            {
                processDate = parsedDate;
            }

            return (coreId, productType, contractNumber, processDate);
        }

        public bool IsValidIdentifier(string uniqueIdentifier)
        {
            if (string.IsNullOrWhiteSpace(uniqueIdentifier))
            {
                return false;
            }

            return IdentifierPattern.IsMatch(uniqueIdentifier);
        }

        /// <summary>
        /// Validates if folder reference follows correct format.
        /// </summary>
        public bool IsValidFolderReference(string folderReference)
        {
            if (string.IsNullOrWhiteSpace(folderReference))
            {
                return false;
            }

            return FolderReferencePattern.IsMatch(folderReference);
        }

        /// <summary>
        /// Parses folder reference back to components.
        /// </summary>
        public (string CoreId, string ProductType, string ContractNumber) ParseFolderReference(
            string folderReference)
        {
            if (string.IsNullOrWhiteSpace(folderReference))
            {
                throw new ArgumentException("Folder reference cannot be null or empty", nameof(folderReference));
            }

            var match = FolderReferencePattern.Match(folderReference);

            if (!match.Success)
            {
                throw new FormatException(
                    $"Invalid folder reference format: {folderReference}. " +
                    "Expected format: DE-{CoreId}{ProductType}-{ContractNumber}");
            }

            return (
                match.Groups["coreId"].Value,
                match.Groups["productType"].Value,
                match.Groups["contractNumber"].Value
            );
        }

        private void ValidateParameters(string coreId, string productType, string contractNumber)
        {
            if (string.IsNullOrWhiteSpace(coreId))
            {
                throw new ArgumentException("CoreId cannot be null or empty", nameof(coreId));
            }

            if (string.IsNullOrWhiteSpace(productType))
            {
                throw new ArgumentException("ProductType cannot be null or empty", nameof(productType));
            }

            if (string.IsNullOrWhiteSpace(contractNumber))
            {
                throw new ArgumentException("ContractNumber cannot be null or empty", nameof(contractNumber));
            }

            // Validate product type format (should be 5 digits)
            if (!Regex.IsMatch(productType, @"^\d{5}$"))
            {
                throw new ArgumentException(
                    $"ProductType must be 5 digits (e.g., '00008' or '00010'), got: {productType}",
                    nameof(productType));
            }

            // Validate CoreId is numeric
            if (!Regex.IsMatch(coreId, @"^\d+$"))
            {
                throw new ArgumentException(
                    $"CoreId must be numeric, got: {coreId}",
                    nameof(coreId));
            }
        }
    }
}
