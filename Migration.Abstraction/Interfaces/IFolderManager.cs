using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    /// <summary>
    /// Service for managing physical folders on filesystem.
    /// Handles creation of folder structure: ROOT -> dosie-{ClientType} -> {ClientType}{CoreId}
    /// </summary>
    public interface IFolderManager
    {
        /// <summary>
        /// Ensures that the complete folder structure exists for a client.
        /// Creates folders if they don't exist.
        /// </summary>
        /// <param name="clientType">Client type: "PL", "FL", or "ACC"</param>
        /// <param name="coreId">Client's Core ID</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Full physical path to the client's folder</returns>
        /// <example>
        /// For clientType="PL", coreId="10101010", and root="C:\Docs":
        /// Creates: C:\Docs\dosie-PL\PL10101010
        /// Returns: "C:\Docs\dosie-PL\PL10101010"
        /// </example>
        Task<string> EnsureFolderStructureAsync(string clientType, string coreId, CancellationToken ct = default);

        /// <summary>
        /// Checks if folder structure exists for a client.
        /// </summary>
        /// <param name="clientType">Client type</param>
        /// <param name="coreId">Client's Core ID</param>
        /// <returns>True if folder structure exists</returns>
        bool FolderStructureExists(string clientType, string coreId);

        /// <summary>
        /// Gets the full physical path for a client's folder (without creating it).
        /// </summary>
        /// <param name="clientType">Client type</param>
        /// <param name="coreId">Client's Core ID</param>
        /// <returns>Full physical path</returns>
        string GetClientFolderPath(string clientType, string coreId);
    }
}
