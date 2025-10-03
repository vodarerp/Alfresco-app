using Alfresco.Apstraction.Interfaces;
using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Apstraction.Interfaces;
using Migration.Apstraction.Interfaces.Wrappers;
using Migration.Apstraction.Models;
using Migration.Extensions.Oracle;
using Oracle.Apstraction.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    public class MoveService : IMoveService
    {
        private readonly IMoveReader _moveReader;
        private readonly IMoveExecutor _moveExecutor;
        private readonly IDocStagingRepository _docRepo;
        private readonly IAlfrescoWriteApi _write;
        private readonly IOptions<MigrationOptions> _options;
        private readonly IServiceProvider _sp;
        private readonly ILogger<MoveService> _logger;

        private long _totalMoved = 0;
        private long _totalFailed = 0;

        public MoveService(IMoveReader moveService, IMoveExecutor moveExecutor, IDocStagingRepository docRepo, IAlfrescoWriteApi write, IOptions<MigrationOptions> options, IServiceProvider sp, ILogger<MoveService> logger)
        {
            _moveReader = moveService;
            _moveExecutor = moveExecutor;
            _docRepo = docRepo;
            _write = write;
            _options = options;
            _sp = sp;
            _logger = logger;
        }

        public async Task<MoveBatchResult> RunBatchAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            using var batchScope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Service"] = nameof(MoveService),
                ["Operation"] = "RunBatch"
            });

            _logger.LogInformation("Move batch started");

            var batch = _options.Value.MoveService.BatchSize ?? _options.Value.BatchSize;
            var dop = _options.Value.MoveService.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;

            // 1. Atomic acquire - preuzmi i zaključaj dokumente u jednoj transakciji
            var documents = await AcquireDocumentsForMoveAsync(batch, ct);

            if (documents.Count == 0)
            {
                _logger.LogInformation("No documents ready for move");
                return new MoveBatchResult(0, 0);
            }

            _logger.LogInformation("Acquired {Count} documents for move", documents.Count);

            // 2. Parallel move dokumenata
            var doneCount = 0;
            var errors = new ConcurrentBag<(long DocId, Exception Error)>();

            await Parallel.ForEachAsync(
                documents,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = dop,
                    CancellationToken = ct
                },
                async (doc, token) =>
                {
                    try
                    {
                        await MoveSingleDocumentAsync(doc.Id, doc.NodeId, doc.ToPath, token);
                        Interlocked.Increment(ref doneCount);
                        Interlocked.Increment(ref _totalMoved);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to move document {DocId} ({NodeId})",
                            doc.Id, doc.NodeId);
                        errors.Add((doc.Id, ex));
                    }
                });

            // 3. Batch update za greške - sve u jednoj transakciji
            if (!errors.IsEmpty)
            {
                await MarkDocumentsAsFailedAsync(errors, ct);
                Interlocked.Add(ref _totalFailed, errors.Count);
            }

            sw.Stop();
            _logger.LogInformation(
                "Move batch completed: {Done} moved, {Failed} failed in {Elapsed}ms " +
                "(Total: {TotalMoved} moved, {TotalFailed} failed)",
                doneCount, errors.Count, sw.ElapsedMilliseconds, _totalMoved, _totalFailed);

            return new MoveBatchResult(doneCount, errors.Count);
        }

        public async Task RunLoopAsync(CancellationToken ct)
        {
            var batchCounter = 1;
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;

            _logger.LogInformation("Move worker started");

            // Reset metrics
            _totalMoved = 0;
            _totalFailed = 0;

            while (!ct.IsCancellationRequested)
            {
                using var batchScope = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["BatchCounter"] = batchCounter
                });

                try
                {
                    _logger.LogDebug("Starting batch {BatchCounter}", batchCounter);

                    var result = await RunBatchAsync(ct);

                    if (result.Done == 0 && result.Failed == 0)
                    {
                        emptyResultCounter++;
                        _logger.LogDebug(
                            "Empty result ({Counter}/{Max})",
                            emptyResultCounter, maxEmptyResults);

                        if (emptyResultCounter >= maxEmptyResults)
                        {
                            _logger.LogInformation(
                                "Breaking after {Count} consecutive empty results",
                                emptyResultCounter);
                            break;
                        }

                        await Task.Delay(delay, ct);
                    }
                    else
                    {
                        emptyResultCounter = 0; // Reset counter on success

                        var betweenDelay = _options.Value.MoveService.DelayBetweenBatchesInMs
                            ?? _options.Value.DelayBetweenBatchesInMs;

                        if (betweenDelay > 0)
                        {
                            await Task.Delay(betweenDelay, ct);
                        }
                    }

                    batchCounter++;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Move worker cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in batch {BatchCounter}", batchCounter);

                    // Exponential backoff on error
                    await Task.Delay(delay * 2, ct);
                    batchCounter++;
                }
            }

            _logger.LogInformation(
                "Move worker completed after {Count} batches. Total: {Moved} moved, {Failed} failed",
                batchCounter - 1, _totalMoved, _totalFailed);
        }

        #region privates

        private async Task<IReadOnlyList<DocStaging>> AcquireDocumentsForMoveAsync(int batch, CancellationToken ct)
        {

            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

            await uow.BeginAsync(ct: ct);
            try
            {
                var documents = await docRepo.TakeReadyForProcessingAsync(batch, ct);

                foreach(var doc in documents)
                {
                    await docRepo.SetStatusAsync(doc.Id, MigrationStatus.InProgress.ToDbString(), null, ct);
                }

                await uow.CommitAsync();
                return documents;
            }
            catch (Exception ex)
            {
                await uow.RollbackAsync();
                throw;
            }

        }

        private async Task MoveSingleDocumentAsync(long docId, string nodeId, string destNodeId,CancellationToken ct)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["DocumentId"] = docId,
                ["NodeId"] = nodeId,
                ["DestFolderId"] = destNodeId
            });

            _logger.LogDebug("Moving document {DocId}", docId);

            var moved = await _moveExecutor.MoveAsync(nodeId, destNodeId, ct);

            if (!moved)
            {
                throw new InvalidOperationException(
                    $"Move operation returned false for document {docId}");
            }

            await UpdateDocumentStatusAsync(
                docId,
                MigrationStatus.Done.ToDbString(),
                null,
                ct);
        }

        private async Task UpdateDocumentStatusAsync(long docId, string status, string? errorMsg, CancellationToken ct)
        {
            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

            await uow.BeginAsync(ct: ct);
            try
            {
                await docRepo.SetStatusAsync(docId, status, errorMsg, ct);
                await uow.CommitAsync(ct: ct);
            }
            catch
            {
                await uow.RollbackAsync(ct: ct);
                throw;
            }
        }

        private async Task MarkDocumentsAsFailedAsync(
            ConcurrentBag<(long DocId, Exception Error)> errors,
            CancellationToken ct)
        {
            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

            await uow.BeginAsync(ct: ct);
            try
            {
                // Koristi batch extension method umesto pojedinačnih update-a
                var updates = errors.Select(e => (
                    e.DocId,
                    MigrationStatus.Error.ToDbString(),
                    e.Error.Message.Length > 4000
                        ? e.Error.Message.Substring(0, 4000)
                        : e.Error.Message
                ));

                await docRepo.BatchSetDocumentStatusAsync(
                    uow.Connection,
                    uow.Transaction,
                    updates,
                    ct);

                await uow.CommitAsync(ct: ct);

                _logger.LogWarning("Marked {Count} documents as failed", errors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark documents as failed");
                await uow.RollbackAsync(ct: ct);
            }
        }

        #endregion


        #region Old version - working (commented)
        //public async Task<MoveBatchResult> RunBatchAsync(CancellationToken ct)
        //{
        //    var toRet = new MoveBatchResult(0, 0);
        //    int ctnDone = 0, ctnFailed = 0;
        //    //ct.ThrowIfCancellationRequested();
        //    var batch = _options.Value.MoveService.BatchSize ?? _options.Value.BatchSize;
        //    var dop = _options.Value.MoveService.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;

        //    IReadOnlyList<DocStaging> documents = null;

        //    await using (var scope = _sp.CreateAsyncScope())
        //    {
        //        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        //        var dr = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

        //        await uow.BeginAsync(ct: ct);

        //        try
        //        {
        //            _logger.LogInformation($"TakeReadyForProcessingAsync.");
        //            documents = await dr.TakeReadyForProcessingAsync(batch, ct);
        //            _logger.LogInformation($"TakeReadyForProcessingAsync returned {documents.Count}. Setitng up to status in prog!");

        //            foreach (var d in documents)
        //                await dr.SetStatusAsync(d.Id, "IN PROG", null, ct);

        //            await uow.CommitAsync();
        //            _logger.LogInformation($"Statuses changed. Commit DOne!");

        //        }
        //        catch (Exception ex)
        //        {
        //            _logger.LogError($"Exception: {ex.Message}!");

        //            await uow.RollbackAsync();
        //            throw;
        //        }
        //    }


        //    if (documents != null && documents.Count > 0)
        //    {

        //        await Parallel.ForEachAsync(documents, new ParallelOptions
        //        {
        //            CancellationToken = ct,
        //            MaxDegreeOfParallelism = dop
        //        },
        //        async (doc, token) =>
        //        {
        //            await using var scope = _sp.CreateAsyncScope();
        //            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        //            var dr = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();
        //            using (_logger.BeginScope(new Dictionary<string, object> { ["DocumentId"] = doc.Id }))
        //            {
        //                _logger.LogInformation($"Prepare document {doc.Id} for move.");
        //                _logger.LogInformation($"DocId: {doc.NodeId} Destination: {doc.ToPath}.");

        //                await uow.BeginAsync();
        //                try
        //                {

        //                    if (await _moveExecutor.MoveAsync(doc.NodeId, doc.ToPath, token))
        //                    {
        //                        _logger.LogInformation($"Document {doc.Id} moved. Changing status.");

        //                        await dr.SetStatusAsync(doc.Id, "DONE", null, token);
        //                    }

        //                    Interlocked.Increment(ref ctnDone);
        //                    await uow.CommitAsync(ct: token);
        //                    _logger.LogInformation($"Document {doc.Id} commited.");

        //                }
        //                catch (Exception ex)
        //                {
        //                    _logger.LogInformation($"Exception: {ex.Message}.");

        //                    await uow.RollbackAsync(ct: token);
        //                    await using var failScope = _sp.CreateAsyncScope();
        //                    var failUow = failScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        //                    var failFr = failScope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

        //                    await failUow.BeginAsync(ct: token);
        //                    await failFr.FailAsync(doc.Id, ex.Message, token);
        //                    await failUow.CommitAsync(ct: token);

        //                }
        //            }
        //        });



        //    }

        //    #region Commented 

        //    //    var readyDocuments = await _moveReader.ReadBatchAsync(batch, ct);

        //    //if (readyDocuments == null || readyDocuments.Count == 0)
        //    //{
        //    //    //iLogger.LogInformation("No documents ready for move."); Todo
        //    //    return toRet;
        //    //}

        //    //foreach (var item in readyDocuments)
        //    //{

        //    //    try
        //    //    {
        //    //        if (await _moveExecutor.MoveAsync(item.DocumentNodeId, item.FolderDestId, ct))
        //    //        {
        //    //            //loger
        //    //            await _docRepo.SetStatusAsync(item.DocStagingId, "DONE", null, ct);

        //    //            //Interlocked.Increment(ref ctnDone);
        //    //            ctnDone++;

        //    //        }
        //    //        else
        //    //        {
        //    //            //logert
        //    //        }

        //    //    }
        //    //    catch (Exception)
        //    //    {
        //    //        await _docRepo.SetStatusAsync(item.DocStagingId, "FAILED", "Docuemt FAILD to execute MVOE", ct);

        //    //        //Interlocked.Increment(ref ctnFailed);
        //    //        ctnFailed++;

        //    //    }

        //    //}

        //    #endregion




        //    var delay = _options.Value.DocumentDiscovery.DelayBetweenBatchesInMs ?? _options.Value.DelayBetweenBatchesInMs;
        //    //_logger.LogInformation("No more documents to process, exiting loop."); TODO
        //    if (delay > 0)
        //        await Task.Delay(delay, ct);
        //    return toRet;
        //}

        //public async Task RunLoopAsync(CancellationToken ct)
        //{
        //    int BatchCounter = 1, counter = 0;
        //    var delay = _options.Value.IdleDelayInMs;
        //    _logger.LogInformation("Worker Started");
        //    while (!ct.IsCancellationRequested)
        //    {
        //        using (_logger.BeginScope(new Dictionary<string, object> { ["BatchCnt"] = BatchCounter }))
        //        {
        //            try
        //            {
        //                _logger.LogInformation($"Batch Started");

        //                var resRun = await RunBatchAsync(ct);
        //                if (resRun.Done == 0 && resRun.Failed == 0)
        //                {
        //                    _logger.LogInformation($"No more documents to process, exiting loop.");
        //                    counter++;
        //                    if (counter == _options.Value.BreakEmptyResults)
        //                    {
        //                        _logger.LogInformation($" Break after {counter} empty results");
        //                        break;
        //                    }
        //                    //var delay = _options.Value.IdleDelayInMs;
        //                    //_logger.LogInformation("No more documents to process, exiting loop."); TODO
        //                    if (delay > 0)
        //                        await Task.Delay(delay, ct);
        //                }
        //                BatchCounter++;
        //            }
        //            catch (Exception ex)
        //            {
        //                _logger.LogError($"RunLoopAsync Exception: {ex.Message}.");
        //                if (delay > 0)
        //                    await Task.Delay(delay, ct); ;
        //            }
        //        }
        //        BatchCounter++;
        //        counter = 0;
        //    }
        //} 
        #endregion
    }
}
