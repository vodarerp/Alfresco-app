using Alfresco.Contracts.Request;
using Alfresco.Contracts.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Abstraction.Interfaces
{
    public interface IAlfrescoReadApi
    {
        Task<bool> PingAsync(CancellationToken ct = default);

        Task<NodeChildrenResponse> GetNodeChildrenAsync(string nodeId, CancellationToken ct = default);

        /// <summary>
        /// Gets children of a node with pagination support.
        /// Prevents OutOfMemory exceptions by limiting number of items loaded at once.
        /// </summary>
        /// <param name="nodeId">The node ID</param>
        /// <param name="skipCount">Number of items to skip (pagination offset)</param>
        /// <param name="maxItems">Maximum number of items to return per page</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Node children response with pagination info</returns>
        Task<NodeChildrenResponse> GetNodeChildrenAsync(string nodeId, int skipCount, int maxItems, CancellationToken ct = default);

        Task<NodeChildrenResponse> SearchAsync(PostSearchRequest request, CancellationToken ct = default);

        Task<string> GetFolderByRelative(string inNodeId, string inRelativePath, CancellationToken ct = default);

        /// <summary>
        /// Gets a single node by its ID
        /// </summary>
        /// <param name="nodeId">The node ID (e.g., "workspace://SpacesStore/node-id" or just "node-id")</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Node information</returns>
        Task<NodeResponse> GetNodeByIdAsync(string nodeId, CancellationToken ct = default);

        /// <summary>
        /// Checks if a folder exists by name within a parent folder
        /// </summary>
        /// <param name="parentFolderId">The parent folder ID to search in</param>
        /// <param name="folderName">The name of the folder to find</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>True if folder exists, false otherwise</returns>
        Task<bool> FolderExistsAsync(string parentFolderId, string folderName, CancellationToken ct = default);

        /// <summary>
        /// Gets a folder by name within a parent folder, including its properties
        /// </summary>
        /// <param name="parentFolderId">The parent folder ID to search in</param>
        /// <param name="folderName">The name of the folder to find</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>NodeResponse with folder information and properties, or null if not found</returns>
        Task<NodeResponse?> GetFolderByNameAsync(string parentFolderId, string folderName, CancellationToken ct = default);
    }
}
