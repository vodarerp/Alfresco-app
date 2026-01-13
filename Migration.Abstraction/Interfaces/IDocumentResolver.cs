using Alfresco.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    public interface IDocumentResolver
    {
        
        Task<string> ResolveAsync(string destinationRootId, string newFolderName, Dictionary<string, object>? properties, CancellationToken ct);

        Task<Entry> ResolveAsync_v1(string destinationRootId, string newFolderName, Dictionary<string, object>? properties, string? customNodeType, bool createIfMissing, CancellationToken ct);


        Task<(Entry Folder, bool IsCreated)> ResolveWithStatusAsync(
            string destinationRootId,
            string newFolderName,
            Dictionary<string, object>? properties,
            Alfresco.Contracts.Models.UniqueFolderInfo? folderInfo,
            CancellationToken ct);


        //Task<(string FolderId, bool IsCreated)> ResolveWithStatusAsync(
        //    string destinationRootId,
        //    string newFolderName,
        //    Dictionary<string, object>? properties,
        //    Alfresco.Contracts.Models.UniqueFolderInfo? folderInfo,
        //    CancellationToken ct);
    }
}
