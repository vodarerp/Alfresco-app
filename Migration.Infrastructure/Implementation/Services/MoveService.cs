using Alfresco.Apstraction.Interfaces;
using Migration.Apstaction.Interfaces;
using Migration.Apstaction.Interfaces.Wrappers;
using Migration.Apstaction.Models;
using Oracle.Apstaction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    internal class MoveService : IMoveService
    {
        private readonly IMoveReader _moveReader;
        private readonly IMoveExecutor _moveExecutor;
        private readonly IDocStagingRepository _docRepo;
        private readonly IAlfrescoWriteApi _write;

        public MoveService(IMoveReader moveService, IMoveExecutor moveExecutor, IDocStagingRepository docRepo, IAlfrescoWriteApi write)
        {
            _moveReader = moveService;
            _moveExecutor = moveExecutor;
            _docRepo = docRepo;
            _write = write;
        }

        public async Task<MoveBatchResult> RunBatchAsync(MoveBatchRequest inRequest, CancellationToken ct)
        {
            var toRet = new MoveBatchResult(0,0);
            int ctnDone = 0, ctnFailed = 0;
            ct.ThrowIfCancellationRequested();

            var readyDocuments = await _moveReader.ReadBatchAsync(inRequest.Take, ct);

            if (readyDocuments == null || readyDocuments.Count == 0)
            {
                //iLogger.LogInformation("No documents ready for move."); Todo
                return toRet;
            }

            foreach (var item in readyDocuments)
            {

                try
                {
                    if (await _moveExecutor.MoveAsync(item.DocumentNodeId, item.FolderDestId, ct))
                    {
                        //loger
                        await _docRepo.SetStatusAsync(item.DocStagingId, "DONE", null, ct);

                        //Interlocked.Increment(ref ctnDone);
                        ctnDone++;

                    }
                    else
                    {
                        //logert
                    }
                     
                }
                catch (Exception)
                {
                    await _docRepo.SetStatusAsync(item.DocStagingId, "FAILED", "Docuemt FAILD to execute MVOE", ct);

                    //Interlocked.Increment(ref ctnFailed);
                    ctnFailed++;

                }

            }


           


            return toRet;
        }

        public async Task RunLoopAsync(MoveLoopOptions inOptions, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var resRun = await RunBatchAsync(inOptions.Batch, ct);
                    if (resRun.Done == 0 && resRun.Failed == 0)
                    {
                        //_logger.LogInformation("No more documents to process, exiting loop."); TODO
                        await Task.Delay(inOptions.IdleDelay, ct);
                    }
                }
                catch (Exception)
                {
                    throw;
                }
            }
        }
    }
}
