using Alfresco.Apstraction.Interfaces;
using Alfresco.Contracts.Oracle.Models;
using Migration.Apstaction.Interfaces;
using Migration.Apstaction.Models;
using Oracle.Apstaction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation
{
    public class FolderIngestor : IFolderIngestor
    {
        private readonly IFolderStagingRepository _folderRepo;

        public FolderIngestor(IFolderStagingRepository folderRepo)
        {
            _folderRepo = folderRepo;
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

            await _folderRepo.AddAsync(row,ct);
        }
    }
}
