using Alfresco.Apstraction.Interfaces;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Request;
using Migration.Apstaction.Interfaces;
using Migration.Apstaction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Folder
{
    public class FolderReader : IFolderReader
    {
        private readonly IAlfrescoReadApi _read;

        public FolderReader(IAlfrescoReadApi read)
        {
                _read = read;
        }

        //public sealed record FolderReaderRequest(string RootId, string NameFilter, int Skip, int Take);
        public async Task<IList<ListEntry>> ReadBatchAsync(FolderReaderRequest inRequest, CancellationToken ct)
        {
            

            var cmsLike = string.IsNullOrWhiteSpace(inRequest.NameFilter) ? "" : inRequest.NameFilter;
            var req = new PostSearchRequest()
            {
                Query = new QueryRequest()
                {
                    Language = "cmis",
                    Query = $"SELECT * FROM cmis:folder WHERE cmis:parentId = '{inRequest.RootId}' and cmis:name LIKE '%{cmsLike}%' order by cmis:name" //IN_TREE('<id>') umose parentId = ''
                },
                Paging = new PagingRequest()
                {
                    MaxItems = inRequest.Take,
                    SkipCount = inRequest.Skip
                },
                Sort = null
            };

            var result = (await _read.SearchAsync(req, ct)).List?.Entries ?? new();// List<ListEntry>();

            //var result = new List<FolderSource>(resp?.List?.Entries?.Count ?? 0);

            //if (result != null && result.Count > 0)
            //{
            //    foreach (var item in resp.List.Entries)
            //    {
            //        if (item?.Entry != null)
            //        {
            //            result.Add(new FolderSource(item.Entry.Id, item.Entry.Name, item.Entry.ParentId ?? inRequest.rootId, ""));
            //        }
            //    }
            //}


            return result;
        }
    }
}
