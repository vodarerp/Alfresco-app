using Alfresco.Apstraction.Interfaces;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        private readonly IOptions<MigrationOptions> _options;
        private readonly IServiceProvider _sp;

        public MoveService(IMoveReader moveService, IMoveExecutor moveExecutor, IDocStagingRepository docRepo, IAlfrescoWriteApi write, IOptions<MigrationOptions> options, IServiceProvider sp)
        {
            _moveReader = moveService;
            _moveExecutor = moveExecutor;
            _docRepo = docRepo;
            _write = write;
            _options = options;
            _sp = sp;
        }

        public async Task<MoveBatchResult> RunBatchAsync(CancellationToken ct)
        {
            var toRet = new MoveBatchResult(0,0);
            int ctnDone = 0, ctnFailed = 0;
            //ct.ThrowIfCancellationRequested();
            var batch = _options.Value.MoveService.BatchSize ?? _options.Value.BatchSize;
            var dop = _options.Value.MoveService.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;

            IReadOnlyList<DocStaging> documents = null;

            await using (var scope = _sp.CreateAsyncScope())
            {
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var dr = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

                await uow.BeginAsync(ct: ct);

                try
                {
                    documents = await dr.TakeReadyForProcessingAsync(batch, ct);

                    foreach (var d in documents)
                        await dr.SetStatusAsync(d.Id, "INPROG", null, ct);

                    await uow.CommitAsync();

                }
                catch (Exception)
                {
                    await uow.RollbackAsync();
                    throw;
                }
            }


            if (documents != null && documents.Count > 0)
            {

                await Parallel.ForEachAsync(documents, new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = dop
                },
                async (doc, token) =>
                {
                    await using var scope = _sp.CreateAsyncScope();
                    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var dr = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

                    await uow.BeginAsync();
                    try
                    {

                        if (await _moveExecutor.MoveAsync(doc.NodeId, doc.ToPath, token))
                        {
                            await dr.SetStatusAsync(doc.Id, "DONE", null, token);
                        }

                        Interlocked.Increment(ref ctnDone);
                        await uow.CommitAsync(ct: token);
                    }
                    catch (Exception ex)
                    {

                        await uow.RollbackAsync(ct: token);
                        await using var failScope = _sp.CreateAsyncScope();
                        var failUow = failScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                        var failFr = failScope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

                        await failUow.BeginAsync(ct: token);
                        await failFr.FailAsync(doc.Id, ex.Message, token);
                        await failUow.CommitAsync(ct: token);
                       
                    }
                });



            }

            #region Commented 

            //    var readyDocuments = await _moveReader.ReadBatchAsync(batch, ct);

            //if (readyDocuments == null || readyDocuments.Count == 0)
            //{
            //    //iLogger.LogInformation("No documents ready for move."); Todo
            //    return toRet;
            //}

            //foreach (var item in readyDocuments)
            //{

            //    try
            //    {
            //        if (await _moveExecutor.MoveAsync(item.DocumentNodeId, item.FolderDestId, ct))
            //        {
            //            //loger
            //            await _docRepo.SetStatusAsync(item.DocStagingId, "DONE", null, ct);

            //            //Interlocked.Increment(ref ctnDone);
            //            ctnDone++;

            //        }
            //        else
            //        {
            //            //logert
            //        }

            //    }
            //    catch (Exception)
            //    {
            //        await _docRepo.SetStatusAsync(item.DocStagingId, "FAILED", "Docuemt FAILD to execute MVOE", ct);

            //        //Interlocked.Increment(ref ctnFailed);
            //        ctnFailed++;

            //    }

            //}

            #endregion




            var delay = _options.Value.DocumentDiscovery.DelayBetweenBatchesInMs ?? _options.Value.DelayBetweenBatchesInMs;
            //_logger.LogInformation("No more documents to process, exiting loop."); TODO
            if (delay > 0)
                await Task.Delay(delay, ct);
            return toRet;
        }

        public async Task RunLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var resRun = await RunBatchAsync(ct);
                    if (resRun.Done == 0 && resRun.Failed == 0)
                    {
                        var delay = _options.Value.IdleDelayInMs;
                        //_logger.LogInformation("No more documents to process, exiting loop."); TODO
                        if (delay > 0)
                            await Task.Delay(delay, ct);
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
