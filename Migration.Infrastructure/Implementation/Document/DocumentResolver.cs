using Alfresco.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces;
using Oracle.Abstraction.Interfaces;
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

        public DocumentResolver(
            IDocStagingRepository doc,
            IAlfrescoReadApi read,
            IAlfrescoWriteApi write,
            IFolderManager folderManager)
        {
            _doc = doc;
            _read = read;
            _write = write;
            _folderManager = folderManager ?? throw new ArgumentNullException(nameof(folderManager));
        }
        public async Task<string> ResolveAsync(string destinationRootId, string newFolderName, CancellationToken ct)
        {

            var folderID = await _read.GetFolderByRelative(destinationRootId, newFolderName, ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(folderID))
            {
                folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, ct).ConfigureAwait(false);
            }

            return folderID;

            //throw new NotImplementedException();
        }
    }
}
