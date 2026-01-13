using Alfresco.Contracts.Models;
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
        
        Task<NodeChildrenResponse> GetNodeChildrenAsync(string nodeId, int skipCount, int maxItems, CancellationToken ct = default);

        Task<NodeChildrenResponse> SearchAsync(PostSearchRequest request, CancellationToken ct = default);

        Task<string> GetFolderByRelative(string inNodeId, string inRelativePath, CancellationToken ct = default);
        Task<Entry> GetFolderByRelative_v1(string inNodeId, string inRelativePath, CancellationToken ct = default);       
        Task<NodeResponse> GetNodeByIdAsync(string nodeId, CancellationToken ct = default);
       
        Task<bool> FolderExistsAsync(string parentFolderId, string folderName, CancellationToken ct = default);
       
        Task<NodeResponse?> GetFolderByNameAsync(string parentFolderId, string folderName, CancellationToken ct = default);
    }
}
