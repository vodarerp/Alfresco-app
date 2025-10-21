using System;

namespace Migration.Abstraction.Interfaces
{
    /// <summary>
    /// Service for generating folder paths based on client type and CoreId.
    /// Structure: ROOT -> dosie-{ClientType} -> {ClientType}{CoreId} -> documents
    /// Example: ROOT -> dosie-PL -> PL10101010 -> documents
    /// </summary>
    public interface IFolderPathService
    {
        /// <summary>
        /// Generates the full folder path for a client's documents.
        /// </summary>
        /// <param name="clientType">Client type: "PL" (Pravno Lice) or "FL" (Fizičko Lice)</param>
        /// <param name="coreId">Client's Core ID (without prefix)</param>
        /// <returns>Full folder path relative to root</returns>
        /// <example>
        /// For clientType="PL" and coreId="10101010":
        /// Returns: "dosie-PL/PL10101010"
        /// </example>
        string GenerateFolderPath(string clientType, string coreId);

        /// <summary>
        /// Generates the parent dosie folder name based on client type.
        /// </summary>
        /// <param name="clientType">Client type: "PL" or "FL"</param>
        /// <returns>Dosie folder name (e.g., "dosie-PL")</returns>
        string GenerateDosieFolder(string clientType);

        /// <summary>
        /// Generates the client-specific folder name.
        /// </summary>
        /// <param name="clientType">Client type: "PL" or "FL"</param>
        /// <param name="coreId">Client's Core ID (without prefix)</param>
        /// <returns>Client folder name (e.g., "PL10101010")</returns>
        string GenerateClientFolder(string clientType, string coreId);

        /// <summary>
        /// Validates client type.
        /// </summary>
        /// <param name="clientType">Client type to validate</param>
        /// <returns>True if valid (PL or FL)</returns>
        bool IsValidClientType(string clientType);

        /// <summary>
        /// Parses folder path back to components.
        /// </summary>
        /// <param name="folderPath">Folder path to parse</param>
        /// <returns>Tuple with ClientType and CoreId</returns>
        (string ClientType, string CoreId) ParseFolderPath(string folderPath);
    }
}
