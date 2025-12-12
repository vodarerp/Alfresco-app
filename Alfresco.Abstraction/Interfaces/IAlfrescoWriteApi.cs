using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Abstraction.Interfaces
{
    public interface IAlfrescoWriteApi
    {
        Task<bool> MoveDocumentAsync(string nodeId, string targetFolderId, string? newName, CancellationToken ct = default);
        Task<bool> CopyDocumentAsync(string nodeId, string targetFolderId, string? newName, CancellationToken ct = default);
        Task<bool> DeleteNodeAsync(string nodeId, CancellationToken ct = default);
        Task<string> CreateFolderAsync(string parentFolderId, string newFolderName, CancellationToken ct = default);
        Task<string> CreateFolderAsync(string parentFolderId, string newFolderName, Dictionary<string, object>? properties, CancellationToken ct = default);
        Task<string> CreateFolderAsync(string parentFolderId, string newFolderName, Dictionary<string, object>? properties, string? customNodeType, CancellationToken ct = default);
        Task<string> CreateFileAsync(string parentFolderId, string newFileName, CancellationToken ct = default);

        /// <summary>
        /// Updates properties of an existing node in Alfresco
        /// Uses PUT /nodes/{nodeId} endpoint to update node metadata
        /// </summary>
        /// <param name="nodeId">Node ID to update</param>
        /// <param name="properties">Dictionary of properties to set (e.g., "ecm:status", "ecm:docType", "cm:title")</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>True if update succeeded, false otherwise</returns>
        Task<bool> UpdateNodePropertiesAsync(string nodeId, Dictionary<string, object> properties, CancellationToken ct = default);
    }
}
