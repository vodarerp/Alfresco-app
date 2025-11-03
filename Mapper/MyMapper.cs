using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alfresco.Contracts.Enums;

namespace Mapper
{
    public static class MyMapper
    {


        public static List<FolderStaging> ToFolderStagingList(this List<ListEntry> inEntry)
        {
            return inEntry.Select(e => e.Entry.ToFolderStaging()).ToList();
        }

        public static List<FolderStaging> ToFolderStagingListInsert(this List<ListEntry> inEntry)
        {
            return inEntry.Select(e => e.Entry.ToFolderStagingInsert()).ToList();
        }

        public static FolderStaging ToFolderStagingInsert(this Entry inEntryi)
        {
            var folderStaging = new FolderStaging
            {
                NodeId = inEntryi.Id,
                ParentId = inEntryi.ParentId,
                Name = inEntryi.Name,
                Status = MigrationStatus.Ready.ToDbString(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
                //InsertedAtAlfresco = inEntryi.CreatedAt
            };

            // Map ClientProperties if available
            if (inEntryi.ClientProperties != null)
            {
                folderStaging.CoreId = inEntryi.ClientProperties.CoreId;
                folderStaging.MbrJmbg = inEntryi.ClientProperties.MbrJmbg;
                folderStaging.ClientName = inEntryi.ClientProperties.ClientName;
                folderStaging.ClientType = inEntryi.ClientProperties.ClientType;
                folderStaging.ClientSubtype = inEntryi.ClientProperties.ClientSubtype;
                folderStaging.Residency = inEntryi.ClientProperties.Residency;
                folderStaging.Segment = inEntryi.ClientProperties.Segment;
                folderStaging.Staff = inEntryi.ClientProperties.Staff;
                folderStaging.OpuUser = inEntryi.ClientProperties.OpuUser;
                folderStaging.OpuRealization = inEntryi.ClientProperties.OpuRealization;
                folderStaging.Barclex = inEntryi.ClientProperties.Barclex;
                folderStaging.Collaborator = inEntryi.ClientProperties.Collaborator;
            }

            return folderStaging;
        }

        public static List<FolderStaging> ToFolderStagingListInsert(this IEnumerable<Entry> inEntries)
        {
            return inEntries.Select(e => e.ToFolderStagingInsert()).ToList();
        }


        public static FolderStaging ToFolderStaging(this Entry inEntryi)
        {
            return new FolderStaging
            {
                NodeId = inEntryi.Id,
                ParentId = inEntryi.ParentId,
                Name = inEntryi.Name,                
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        public static List<FolderStaging> ToFolderStagingList(this IEnumerable<Entry> inEntries)
        {
            return inEntries.Select(e => e.ToFolderStaging()).ToList();
        }


        public static DocStaging ToDocStagingInsert(this Entry inEntryi)
        {
            return new DocStaging
            {
                NodeId = inEntryi.Id,
                FromPath = string.Empty,
                ToPath = string.Empty,
               // RetryCount = 0,
                Status = MigrationStatus.Ready.ToDbString(),
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

        public static List<DocStaging> ToDocStagingListInsert(this IEnumerable<Entry> inEntries)
        {
            return inEntries.Select(e => e.ToDocStagingInsert()).ToList();
        }

        public static DocStaging ToDocStaging(this Entry inEntryi)
        {
            return new DocStaging
            {
                NodeId = inEntryi.Id,
                FromPath = string.Empty, 
                ToPath = string.Empty,                
                //RetryCount = 0,
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
