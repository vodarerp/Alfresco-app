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
    public class MoveExecutor : IMoveExecutor
    {

        private readonly IDocStagingRepository _docRepo;
        private readonly IAlfrescoWriteApi _write;


        public MoveExecutor(IDocStagingRepository doc, IAlfrescoWriteApi write)
        {
            _docRepo = doc;
            _write = write;
        }


        public async Task<int> ExecuteMoveAsync(int take, CancellationToken ct)
        {
            //throw new NotImplementedException();

            int added = 0;
            var list = await _docRepo.TakeReadyForProcessingAsync(take, ct);
            foreach(var item in list)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    await _docRepo.SetStatusAsync(item.Id, "PROCESSING", null, ct);
                    if (await _write.MoveDocumentAsync(item.NodeId, item.ToPath,null, ct: ct))
                    {
                        await _docRepo.SetStatusAsync(item.Id, "DONE", null, ct);
                        added++;
                    }
                    else
                    {
                        await _docRepo.FailAsync(item.Id, "Move operation returned false", ct);
                    }

                    //await _write.MoveNodeAsync(item.SourceNodeId, item.DestFolderId, ct);
                }
                catch (Exception ex)
                {
                    await _docRepo.FailAsync(item.Id, ex.Message, ct);
                }
            }


            return added;

        }
    }
}
