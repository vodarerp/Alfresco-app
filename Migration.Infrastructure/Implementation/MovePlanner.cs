using Alfresco.Apstraction.Interfaces;
using Migration.Apstaction.Interfaces;
using Oracle.Apstaction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation
{
    public class MovePlanner : IMovePlanner
    {
        private readonly IAlfrescoReadApi _read;
        private readonly IDocStagingRepository _docRepo;

        public MovePlanner(IAlfrescoReadApi read, IDocStagingRepository doc)
        {
            _read = read;
            _docRepo = doc;
        }
        public async Task<int> PlanAsync(string srcFolderId, string destFodlerId, int pageSize, CancellationToken ct)
        {

            int total = 0; 
            int skipped = 0;

            while (true)
            {
                var nodes = await _docRepo.GetListAsync(
                    filters: new { SourceFolderId = srcFolderId },
                    skip: skipped,
                    take: pageSize,
                    orderBy: new string[] { "Id" },
                    ct: ct);
                var listNodes = nodes.ToList();
                if (listNodes.Count == 0) break;

                _ = await _docRepo.InsertManyAsync(listNodes);


                total += listNodes.Count;
                skipped += listNodes.Count;

                if (listNodes.Count < pageSize) break;
            }

            return total;
        }
    }
}
