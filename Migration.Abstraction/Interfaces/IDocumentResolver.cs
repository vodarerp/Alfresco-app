using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    public interface IDocumentResolver
    {
        /// <summary>
        /// Resolves folder by name (creates if missing - backward compatibility)
        /// </summary>
        Task<string> ResolveAsync(string destinationRootId, string newFolderName, CancellationToken ct);

        /// <summary>
        /// Resolves folder by name with properties (creates if missing - backward compatibility)
        /// </summary>
        Task<string> ResolveAsync(string destinationRootId, string newFolderName, Dictionary<string, object>? properties, CancellationToken ct);

        /// <summary>
        /// Resolves folder with explicit control over creation behavior
        /// </summary>
        /// <param name="destinationRootId">Parent folder ID</param>
        /// <param name="newFolderName">Folder name to resolve</param>
        /// <param name="properties">Optional properties for folder creation</param>
        /// <param name="createIfMissing">If true, creates folder if not found. If false, throws exception.</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Folder ID (existing or newly created)</returns>
        Task<string> ResolveAsync(
            string destinationRootId,
            string newFolderName,
            Dictionary<string, object>? properties,
            bool createIfMissing,
            CancellationToken ct);

        /// <summary>
        /// Resolves folder with UniqueFolderInfo context for property enrichment
        /// </summary>
        /// <param name="destinationRootId">Parent folder ID</param>
        /// <param name="newFolderName">Folder name to resolve</param>
        /// <param name="properties">Optional properties for folder creation</param>
        /// <param name="folderInfo">Folder info with additional data for property building (TipProizvoda, CoreId, etc.)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Folder ID (existing or newly created)</returns>
        Task<string> ResolveAsync(
            string destinationRootId,
            string newFolderName,
            Dictionary<string, object>? properties,
            Alfresco.Contracts.Models.UniqueFolderInfo? folderInfo,
            CancellationToken ct);

        /// <summary>
        /// Resolves folder and returns both FolderId and IsCreated status
        /// </summary>
        /// <param name="destinationRootId">Parent folder ID</param>
        /// <param name="newFolderName">Folder name to resolve</param>
        /// <param name="properties">Optional properties for folder creation</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Tuple with FolderId and IsCreated flag (true = created during migration, false = already existed)</returns>
        Task<(string FolderId, bool IsCreated)> ResolveWithStatusAsync(
            string destinationRootId,
            string newFolderName,
            Dictionary<string, object>? properties,
            CancellationToken ct);
    }
}
