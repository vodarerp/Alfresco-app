using Alfresco.Apstraction.Interfaces;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Request;
using Migration.Apstaction.Interfaces;
using Migration.Apstaction.Models;
using System;
using System.Collections;
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
        public async Task<FolderReaderResult> ReadBatchAsync(FolderReaderRequest inRequest, CancellationToken ct)
        {
            var cmsLike = string.IsNullOrWhiteSpace(inRequest.NameFilter) ? "" : inRequest.NameFilter;
            var sb = new StringBuilder();

            sb.Append("SELECT * from cmis:folder ")              
              .Append($"WHERE cmis:parentId = '{inRequest.RootId}' ")
              .Append($"AND cmis:name LIKE '%{cmsLike}%' ");

            if (inRequest.Cursor is not null && !string.IsNullOrEmpty(inRequest.Cursor.LastObjectId))
            {
                var ld = inRequest.Cursor.LastObjectCreated.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
                sb.Append($"AND cmis:creationDate > TIMESTAMP '{ld}' ");
            }

            sb.Append("ORDER BY cmis:creationDate ASC ");
            //if (inRequest.Cursor is not null && !string.IsNullOrEmpty(inRequest.Cursor.LastObjectId) && inRequest?.Cursor.LastCreatedAt != null)
            //{
            //    var ld = inRequest.Cursor.LastCreatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
            //    sb.Append("AND (cmis:creationDate > TIMESTAMP '").Append(ld).Append("' ")
            //      .Append("OR (cmis:creationDate = TIMESTAMP '").Append(ld).Append("' ")
            //      .Append($"AND cmis:objectId > '{inRequest.Cursor.LastObjectId}' )) ");
            //}

            //sb.Append("ORDER BY cmis:creationDate ASC, cmis:objectId ASC");

            //Query = $"SELECT * FROM cmis:folder WHERE cmis:parentId = '{inRequest.RootId}' and cmis:name LIKE '%{cmsLike}%' order by cmis:name" //IN_TREE('<id>') umose parentId = ''

            var req = new PostSearchRequest()
            {
                Query = new QueryRequest()
                {
                    Language = "cmis",
                    Query = sb.ToString()
                },
                Paging = new PagingRequest()
                {
                    MaxItems = inRequest.Take,
                    SkipCount = 0
                },
                Sort = null
            };

            var result = (await _read.SearchAsync(req, ct)).List?.Entries ?? new List<ListEntry>();

            FolderSeekCursor? next = null;

            if ( result != null && result.Count > 0)
            {
                var last = result[^1].Entry;
                var lastId = $"workspace://SpacesStore/{last.Id}";
                next = new FolderSeekCursor(last.Id,last.CreatedAt);
            }

            return new FolderReaderResult(Items: result, next);
        }
    }
}
