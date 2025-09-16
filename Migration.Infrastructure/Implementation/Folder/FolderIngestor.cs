using Alfresco.Contracts.Models;
using Alfresco.Contracts.Oracle.Models;
using Mapper;
using Migration.Apstaction.Interfaces;
using Migration.Apstaction.Models;
using Oracle.Apstaction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Folder
{
    public class FolderIngestor : IFolderIngestor
    {
        private readonly IFolderStagingRepository _folderRepo;


        public FolderIngestor(IFolderStagingRepository folderRepo)
        {
            _folderRepo = folderRepo;
        }

        public async Task<int> InserManyAsync(IReadOnlyList<FolderStaging> items, CancellationToken ct)
        {
            int added = 0;

            if (items != null && items.Count > 0)
            {
                //var toInsert = items.ToFolderStagingList();
                //toInsert = items.ToFolderStagingList();
                added = await _folderRepo.InsertManyAsync(items, ct);
            }

            return added;
            //throw new NotImplementedException();
        }

        public async Task UpsertAsync(FolderIngestorItem item, CancellationToken ct)
        {
            //_alfrescoWriteService.CreateFolderAsync("bc5b358a-ec9d-49d2-9b35-8aec9d19d27b", $"TestFolder-{x}");

            var row = new FolderStaging()
            {
                NodeId = item.srcFolderId,
                ParentId = item.srcRootId,
                Name = item.srcFolderName,
                Status = "READY",
                DestFolderId = item.destFolderId


            };

            await _folderRepo.AddAsync(row, ct);
        }
    }
}


