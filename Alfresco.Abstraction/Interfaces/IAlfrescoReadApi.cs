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

        Task<NodeChildrenResponse> SearchAsync(PostSearchRequest request, CancellationToken ct = default);

        Task<string> GetFolderByRelative(string inNodeId, string inRelativePath, CancellationToken ct = default);

        /// <summary>
        /// Gets a single node by its ID
        /// </summary>
        /// <param name="nodeId">The node ID (e.g., "workspace://SpacesStore/node-id" or just "node-id")</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Node information</returns>
        Task<NodeResponse> GetNodeByIdAsync(string nodeId, CancellationToken ct = default);
    }
}
