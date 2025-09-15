using Alfresco.Contracts.Models;
using Alfresco.Contracts.Oracle.Models;
using Mapper;
using Migration.Apstaction.Interfaces;
using Oracle.Apstaction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Document
{
    public class DocumentIngestor : IDocumentIngestor
    {
        private readonly IDocStagingRepository _documentRepo;

        public DocumentIngestor(IDocStagingRepository doc)
        {
            _documentRepo = doc;
        }
        public async Task<int> InserManyAsync(IReadOnlyList<DocStaging> items, CancellationToken ct)
        {
            int added = 0;

            if (items != null && items.Count > 0)
            {
               // var toInsert = items.ToDocStagingList();
                //toInsert = items.ToFolderStagingList();
                added = await _documentRepo.InsertManyAsync(items, ct);
            }

            return added;
            //throw new NotImplementedException();
        }
    }
}
