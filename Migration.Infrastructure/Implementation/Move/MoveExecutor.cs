using Alfresco.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Models;
using Oracle.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Move
{
    public class MoveExecutor : IMoveExecutor
    {
        private readonly IDocStagingRepository _docRepo;

        private readonly IAlfrescoWriteApi _write;

        public MoveExecutor(IDocStagingRepository doc, IAlfrescoWriteApi wirte )
        {
            _docRepo = doc;
            _write = wirte;
        }

        public async Task<bool> MoveAsync(string DocumentId, string DestFolderId, CancellationToken ct)
        {

            var toRet = await _write.MoveDocumentAsync(DocumentId, DestFolderId,null, ct).ConfigureAwait(false);

            return toRet;
            //throw new NotImplementedException();
        }
    }
}
