using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Apstraction.Interfaces
{
    public interface IAlfrescoWriteApi
    {
        Task<bool> MoveDocumentAsync(string nodeId, string targetFolderId, string? newName, CancellationToken ct = default);
        Task<bool> DeleteNodeAsync(string nodeId, CancellationToken ct = default);
        Task<bool> CreateFolderAsync(string parentFolderId, string newFolderName, CancellationToken ct = default);
    }
}
