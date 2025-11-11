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

            // Helper to safely get property value from Alfresco Properties dictionary
            string? GetStringProperty(string key)
            {
                if (inEntryi.Properties != null && inEntryi.Properties.TryGetValue(key, out var value))
                {
                    return value?.ToString();
                }
                return null;
            }

            // Map properties directly from Alfresco Properties dictionary
            if (inEntryi.Properties != null && inEntryi.Properties.Count > 0)
            {
                // 1. CoreId
                folderStaging.CoreId = GetStringProperty("ecm:coreId");

                // 2. MBR/JMBG (bnkJmbg)
                folderStaging.MbrJmbg = GetStringProperty("ecm:mbrJmbg") ?? GetStringProperty("ecm:jmbg");

                // 3. Client Name (bnkClientName)
                folderStaging.ClientName = GetStringProperty("ecm:clientName");

                // 4. Client Type (mapped from bnkClientType which is "segment" from ClientAPI)
                folderStaging.ClientType = GetStringProperty("ecm:clientType");

                // 5. Client Subtype (bnkClientSubtype)
                folderStaging.ClientSubtype = GetStringProperty("ecm:clientSubtype");

                // 6. Residency (bnkResidence)
                folderStaging.Residency = GetStringProperty("ecm:residency") ?? GetStringProperty("ecm:bnkResidence");

                // 7. Segment (bnkClientType in properties maps to segment)
                folderStaging.Segment = GetStringProperty("ecm:bnkClientType") ?? GetStringProperty("ecm:segment");

                // 8. Staff (bnkStaff)
                folderStaging.Staff = GetStringProperty("ecm:staff") ?? GetStringProperty("ecm:docStaff");

                // 9. OPU User
                folderStaging.OpuUser = GetStringProperty("ecm:opuUser");

                // 10. OPU Realization (bnkRealizationOPUID)
                folderStaging.OpuRealization = GetStringProperty("ecm:opuRealization");

                // 11. Barclex (bnkBarclex - barCLEXGroupCode + barCLEXGroupName)
                folderStaging.Barclex = GetStringProperty("ecm:barclex");

                // 12. Collaborator (bnkContributor - barCLEXCode + barCLEXName)
                folderStaging.Collaborator = GetStringProperty("ecm:collaborator");

                // NEW: BarCLEX properties
                folderStaging.BarCLEXName = GetStringProperty("ecm:barCLEXName");
                folderStaging.BarCLEXOpu = GetStringProperty("ecm:barCLEXOpu") ?? GetStringProperty("ecm:bnkOfficeId");
                folderStaging.BarCLEXGroupName = GetStringProperty("ecm:barCLEXGroupName");
                folderStaging.BarCLEXGroupCode = GetStringProperty("ecm:barCLEXGroupCode");
                folderStaging.BarCLEXCode = GetStringProperty("ecm:barCLEXCode");

                // Additional properties
                folderStaging.ProductType = GetStringProperty("ecm:bnkTypeOfProduct") ?? GetStringProperty("ecm:productType");
                folderStaging.ContractNumber = GetStringProperty("ecm:contractNumber") ?? GetStringProperty("ecm:bnkNumberOfContract");
                folderStaging.Batch = GetStringProperty("ecm:batch");
                folderStaging.Source = GetStringProperty("ecm:source") ?? GetStringProperty("ecm:bnkSource") ?? GetStringProperty("ecm:bnkSourceId");
                folderStaging.UniqueIdentifier = GetStringProperty("ecm:uniqueFolderId") ?? GetStringProperty("ecm:folderId");
                folderStaging.TipDosijea = GetStringProperty("ecm:bnkDossierType");
                folderStaging.Creator = GetStringProperty("ecm:creator") ?? GetStringProperty("ecm:createdByName");

                // NEW: ClientSegment (može biti isti kao Segment ili drugi property)
                folderStaging.ClientSegment = GetStringProperty("ecm:clientSegment") ?? folderStaging.Segment;

                // NEW: TargetDossierType (destination dossier type for migration)
                folderStaging.TargetDossierType = GetStringProperty("ecm:targetDossierType");

                // Process Date
                var processDateStr = GetStringProperty("ecm:depositProcessedDate") ?? GetStringProperty("ecm:datumKreiranja");
                if (!string.IsNullOrWhiteSpace(processDateStr) && DateTime.TryParse(processDateStr, out var processDate))
                {
                    folderStaging.ProcessDate = processDate;
                }

                // Archived At
                var archivedAtStr = GetStringProperty("ecm:archiveDate");
                if (!string.IsNullOrWhiteSpace(archivedAtStr) && DateTime.TryParse(archivedAtStr, out var archivedAt))
                {
                    folderStaging.ArchivedAt = archivedAt;
                }
            }
            // Fallback: Map ClientProperties if available (for backward compatibility)
            else if (inEntryi.ClientProperties != null)
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
