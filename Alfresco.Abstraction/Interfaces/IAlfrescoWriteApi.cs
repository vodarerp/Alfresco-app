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
        Task<bool> DeleteNodeAsync(string nodeId, CancellationToken ct = default);
        Task<string> CreateFolderAsync(string parentFolderId, string newFolderName, CancellationToken ct = default);
        Task<string> CreateFileAsync(string parentFolderId, string newFileName, CancellationToken ct = default);

    }
}
