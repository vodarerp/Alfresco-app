using Migration.Abstraction.Interfaces;
using System;
using System.Text.RegularExpressions;

namespace Migration.Infrastructure.Implementation
{
    /// <summary>
    /// Service for generating folder paths based on client type and CoreId.
    /// Structure: ROOT -> dosie-{ClientType} -> {ClientType}{CoreId} -> documents
    /// </summary>
    public class FolderPathService : IFolderPathService
    {
        // Valid client types
        private const string CLIENT_TYPE_PL = "PL"; // Pravno Lice (Legal Entity)
        private const string CLIENT_TYPE_FL = "FL"; // Fizičko Lice (Natural Person)
        private const string CLIENT_TYPE_ACC = "ACC"; // Account type

        // Regex pattern for validating folder path
        // Format: dosie-{ClientType}/{ClientType}{CoreId}
        // Example: dosie-PL/PL10101010
        private static readonly Regex FolderPathPattern = new Regex(
            @"^dosie-(?<clientType>PL|FL|ACC)/(?<clientTypePrefix>PL|FL|ACC)(?<coreId>\d+)$",
            RegexOptions.Compiled);

        public string GenerateFolderPath(string clientType, string coreId)
        {
            ValidateClientType(clientType);
            ValidateCoreId(coreId);

            var dosieFolder = GenerateDosieFolder(clientType);
            var clientFolder = GenerateClientFolder(clientType, coreId);

            return $"{dosieFolder}/{clientFolder}";
        }

        public string GenerateDosieFolder(string clientType)
        {
            ValidateClientType(clientType);
            return $"dosie-{clientType.ToUpperInvariant()}";
        }

        public string GenerateClientFolder(string clientType, string coreId)
        {
            ValidateClientType(clientType);
            ValidateCoreId(coreId);

            // Format: {ClientType}{CoreId}
            // Example: PL10101010
            return $"{clientType.ToUpperInvariant()}{coreId}";
        }

        public bool IsValidClientType(string clientType)
        {
            if (string.IsNullOrWhiteSpace(clientType))
            {
                return false;
            }

            var upperType = clientType.ToUpperInvariant();
            return upperType == CLIENT_TYPE_PL ||
                   upperType == CLIENT_TYPE_FL ||
                   upperType == CLIENT_TYPE_ACC;
        }

        public (string ClientType, string CoreId) ParseFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                throw new ArgumentException("Folder path cannot be null or empty", nameof(folderPath));
            }

            var match = FolderPathPattern.Match(folderPath);

            if (!match.Success)
            {
                throw new FormatException(
                    $"Invalid folder path format: {folderPath}. " +
                    "Expected format: dosie-{{ClientType}}/{{ClientType}}{{CoreId}} " +
                    "(e.g., dosie-PL/PL10101010)");
            }

            var clientType = match.Groups["clientType"].Value;
            var clientTypePrefix = match.Groups["clientTypePrefix"].Value;
            var coreId = match.Groups["coreId"].Value;

            // Validate that both client type parts match
            if (clientType != clientTypePrefix)
            {
                throw new FormatException(
                    $"Client type mismatch in folder path: {folderPath}. " +
                    $"Dosie folder has '{clientType}' but client folder has '{clientTypePrefix}'");
            }

            return (clientType, coreId);
        }

        private void ValidateClientType(string clientType)
        {
            if (string.IsNullOrWhiteSpace(clientType))
            {
                throw new ArgumentException("Client type cannot be null or empty", nameof(clientType));
            }

            if (!IsValidClientType(clientType))
            {
                throw new ArgumentException(
                    $"Invalid client type: {clientType}. Must be 'PL', 'FL', or 'ACC'",
                    nameof(clientType));
            }
        }

        private void ValidateCoreId(string coreId)
        {
            if (string.IsNullOrWhiteSpace(coreId))
            {
                throw new ArgumentException("CoreId cannot be null or empty", nameof(coreId));
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
