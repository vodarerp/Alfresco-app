using Alfresco.Apstraction.Interfaces;
using Migration.Apstaction.Interfaces;
using Oracle.Apstaction.Interfaces;
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


        public DocumentResolver(IDocStagingRepository doc, IAlfrescoReadApi read, IAlfrescoWriteApi write)
        {
            _doc = doc;
            _read = read;
            _write = write;
        }
        public async Task<string> ResolveAsync(string destinationRootId, string newFolderName, CancellationToken ct)
        {

            var folderID = await _read.GetFolderByRelative(destinationRootId, newFolderName, ct);

            if (string.IsNullOrEmpty(folderID))
            {
                folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, ct);
            }

            return folderID;

            //throw new NotImplementedException();
        }
    }
}
