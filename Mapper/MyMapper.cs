using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mapper
{
    public static class MyMapper
    {
       
        public static DocStaging ToDocStaging(this Entry inEntryi)
        {
            return new DocStaging
            {
                NodeId = inEntryi.Id,
                FromPath = string.Empty, 
                ToPath = string.Empty,
                Status = "NEW",
                RetryCount = 0,
                ErrorMsg = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsFile = inEntryi.IsFile,
                IsFolder = inEntryi.IsFolder, 
                Name = inEntryi.Name,
                NodeType = inEntryi.NodeType,
                ParentId = inEntryi.ParentId
            };
        }

        public static List<DocStaging> ToDocStagingList(this IEnumerable<Entry> inEntries)
        {
            return inEntries.Select(e => e.ToDocStaging()).ToList();
        }

        public static Entry ToEntry(this DocStaging inDoc)
        {
            return new Entry
            {
                Id = inDoc.NodeId,
                IsFile = inDoc.IsFile,
                IsFolder = inDoc.IsFolder,
                Name = inDoc.Name,
                NodeType = inDoc.NodeType,
                ParentId = inDoc.ParentId,
                CreatedAt = inDoc.CreatedAt,
                ModifiedAt = inDoc.UpdatedAt,
                CreatedByUser = new UserInfo { Id = "system", DisplayName = "System" },
                ModifiedByUser = new UserInfo { Id = "system", DisplayName = "System" }
            };
        }
        public static List<Entry> ToEntryList(this IEnumerable<DocStaging> inDocs)
        {
            return inDocs.Select(d => d.ToEntry()).ToList();
        }
    }
}
