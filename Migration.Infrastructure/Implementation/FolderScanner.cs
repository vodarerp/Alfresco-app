using Alfresco.Apstraction.Interfaces;
using Alfresco.Contracts.Request;
using Migration.Apstaction.Interfaces;
using Migration.Apstaction.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation
{
    public class FolderScanner
    {
        private readonly IAlfrescoReadApi _read;


        public FolderScanner(IAlfrescoReadApi read)
        {
             _read = read;
        }
        public async Task<IReadOnlyList<FolderSource>> ScanAsync(ScanRequest inRequest, CancellationToken ct)
        {
            var cmsLike = string.IsNullOrWhiteSpace(inRequest.nameFilter) ? "" : inRequest.nameFilter;
            var req = new PostSearchRequest()
            {
                Query = new QueryRequest()
                {
                    Language = "cmis",
                    Query = $"SELECT * FROM cmis:folder WHERE cmis:name LIKE '%{cmsLike}%' order by cmis:name"
                },
                Paging = new PagingRequest()
                {
                    MaxItems = inRequest.take,
                    SkipCount = inRequest.skip
                },
                Sort = null
            };

            var resp = await _read.SearchAsync(req, ct);

            var result = new List<FolderSource>(resp?.List?.Entries?.Count ?? 0);

            if (resp?.List?.Entries != null)
            {
                foreach (var item in resp.List.Entries)
                {
                    if (item?.Entry != null)
                    {
                        result.Add(new FolderSource(item.Entry.Id, item.Entry.Name, item.Entry.ParentId ?? inRequest.rootId, ""));
                    }
                }
            }


            return result;
        }
    }
}
