using Alfresco.Abstraction.Interfaces;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
//using Oracle.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Document
{
    public class DocumentResolver : IDocumentResolver
    {
        private readonly IDocStagingRepository _doc;
        private readonly IAlfrescoReadApi _read;
        private readonly IAlfrescoWriteApi _write;
        private readonly IFolderManager _folderManager;
        private readonly ILogger<DocumentResolver> _logger;

        public DocumentResolver(
            IDocStagingRepository doc,
            IAlfrescoReadApi read,
            IAlfrescoWriteApi write,
            IFolderManager folderManager,
            ILogger<DocumentResolver> logger)
        {
            _doc = doc;
            _read = read;
            _write = write;
            _folderManager = folderManager ?? throw new ArgumentNullException(nameof(folderManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        public async Task<string> ResolveAsync(string destinationRootId, string newFolderName, CancellationToken ct)
        {
            return await ResolveAsync(destinationRootId, newFolderName, null, ct).ConfigureAwait(false);
        }

        public async Task<string> ResolveAsync(string destinationRootId, string newFolderName, Dictionary<string, object>? properties, CancellationToken ct)
        {
            var folderID = await _read.GetFolderByRelative(destinationRootId, newFolderName, ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(folderID))
            {
                // If properties are provided, try to create folder with properties first
                if (properties != null && properties.Count > 0)
                {
                    try
                    {
                        _logger.LogDebug(
                            "Attempting to create folder '{FolderName}' with {PropertyCount} properties under parent '{ParentId}'",
                            newFolderName, properties.Count, destinationRootId);

                        folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, properties, ct).ConfigureAwait(false);

                        _logger.LogInformation(
                            "Successfully created folder '{FolderName}' with properties. FolderId: {FolderId}",
                            newFolderName, folderID);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to create folder '{FolderName}' with properties under parent '{ParentId}'. " +
                            "Error: {ErrorType} - {ErrorMessage}. Attempting to create without properties.",
                            newFolderName, destinationRootId, ex.GetType().Name, ex.Message);

                        // Try to create without properties as fallback
                        try
                        {
                            folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, null, ct).ConfigureAwait(false);

                            _logger.LogWarning(
                                "Successfully created folder '{FolderName}' WITHOUT properties as fallback. FolderId: {FolderId}",
                                newFolderName, folderID);
                        }
                        catch (Exception fallbackEx)
                        {
                            _logger.LogError(fallbackEx,
                                "Failed to create folder '{FolderName}' even without properties. " +
                                "Error: {ErrorType} - {ErrorMessage}",
                                newFolderName, fallbackEx.GetType().Name, fallbackEx.Message);
                            throw; // Re-throw if both attempts fail
                        }
                    }
                }
                else
                {
                    // No properties provided, create normally
                    folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, null, ct).ConfigureAwait(false);

                    _logger.LogDebug(
                        "Successfully created folder '{FolderName}' without properties. FolderId: {FolderId}",
                        newFolderName, folderID);
                }
            }
            else
            {
                _logger.LogDebug(
                    "Folder '{FolderName}' already exists under parent '{ParentId}'. FolderId: {FolderId}",
                    newFolderName, destinationRootId, folderID);
            }

            return folderID;

            //throw new NotImplementedException();
        }
    }
}
