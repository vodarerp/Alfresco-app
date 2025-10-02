using Alfresco.Apstraction.Interfaces;
using Alfresco.Contracts.Models;
using Migration.Apstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Document
{
    public class DocumentReader : IDocumentReader
    {
        private readonly IAlfrescoReadApi _read;

        public DocumentReader(IAlfrescoReadApi read)
        {
            _read = read;
        }

        public async Task<IReadOnlyList<ListEntry>> ReadBatchAsync(string folderNodeId, CancellationToken ct)
        {

            var docs = (await _read.GetNodeChildrenAsync(folderNodeId, ct)).List?.Entries ?? new(); 


            return docs;
            //throw new NotImplementedException();
        }
    }
}
