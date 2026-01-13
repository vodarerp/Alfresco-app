using Alfresco.Contracts.Models;
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
        Task<bool> DeleteNodeAsync(string nodeId, CancellationToken ct = default); //TO DELETE
        Task<string> CreateFolderAsync(string parentFolderId, string newFolderName, CancellationToken ct = default);
        Task<string> CreateFolderAsync(string parentFolderId, string newFolderName, Dictionary<string, object>? properties, CancellationToken ct = default); 
        Task<string> CreateFolderAsync(string parentFolderId, string newFolderName, Dictionary<string, object>? properties, string? customNodeType, CancellationToken ct = default);
        Task<Entry> CreateFolderAsync_v1(string parentFolderId, string newFolderName, Dictionary<string, object>? properties, string? customNodeType, CancellationToken ct = default);
        Task<string> CreateFileAsync(string parentFolderId, string newFileName, CancellationToken ct = default); //TO DELETE
        Task<bool> UpdateNodePropertiesAsync(string nodeId, Dictionary<string, object> properties, CancellationToken ct = default);
    }
}
