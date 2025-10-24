using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Abstraction.Models;
//using Migration.Extensions.Oracle;
using Migration.Extensions.SqlServer;
//using Oracle.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        //private readonly ILogger<MoveService> _fileLogger;

        private readonly ILogger _dbLogger;
        private readonly ILogger _fileLogger;

        private long _totalMoved = 0;
        private long _totalFailed = 0;
        private int _batchCounter = 0;

        private const string ServiceName = "Move";

        public MoveService(IMoveReader moveService, IMoveExecutor moveExecutor, IDocStagingRepository docRepo, IAlfrescoWriteApi write, IOptions<MigrationOptions> options, IServiceProvider sp, ILoggerFactory logger)
        {
            _moveReader = moveService;
            _moveExecutor = moveExecutor;
            _docRepo = docRepo;
            _write = write;
            _options = options;
            _sp = sp;
            _dbLogger = logger.CreateLogger("DbLogger");
            _fileLogger = logger.CreateLogger("FileLogger");
            //_fileLogger = logger;
        }

        public async Task<MoveBatchResult> RunBatchAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var acquireTimer = Stopwatch.StartNew();

            _fileLogger.LogInformation("Move batch started");

            var batch = _options.Value.MoveService.BatchSize ?? _options.Value.BatchSize;
            var dop = _options.Value.MoveService.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;

            // 1. Atomic acquire - preuzmi i zaključaj dokumente u jednoj transakciji
            var documents = await AcquireDocumentsForMoveAsync(batch, ct).ConfigureAwait(false);
            acquireTimer.Stop();
            _fileLogger.LogInformation("Acquired {Count} documents in {Ms}ms", documents.Count, acquireTimer.ElapsedMilliseconds);

            if (documents.Count == 0)
            {
                _fileLogger.LogInformation("No documents ready for move");
                return new MoveBatchResult(0, 0);
            }

            // 2. Parallel move dokumenata
            _fileLogger.LogInformation("Starting parallel move with DOP={Dop}", dop);
            var moveTimer = Stopwatch.StartNew();
            var doneCount = 0;
            var errors = new ConcurrentBag<(long DocId, Exception Error)>();
            var successfulDocs = new ConcurrentBag<long>();

            await Parallel.ForEachAsync(
                documents,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = dop,
                    CancellationToken = ct
                },
                async (doc, token) =>
                {
                    //var threadId = Environment.CurrentManagedThreadId;
                    //activeThreads.TryAdd(threadId, 0);
                    //activeThreads[threadId]++;

                    //// ✅ LOG START SA THREAD INFO
                    //_fileLogger.LogInformation(
                    //    "[Thread {ThreadId}] START processing document {DocId}",
                    //    threadId, doc.Id);
                    try
                    {
                        var res = await MoveSingleDocumentAsync(doc.Id, doc.NodeId, doc.ToPath, token).ConfigureAwait(false);
                        if (res)
                        {
                            successfulDocs.Add(doc.Id);
                            Interlocked.Increment(ref doneCount);
                            Interlocked.Increment(ref _totalMoved);

                        }
                        else
                        {
                            errors.Add((doc.Id, new InvalidOperationException("Move returned false")));

                        }
                    }
                    catch (Exception ex)
                    {
                        _dbLogger.LogError(ex,
                            "Failed to move document {DocId} ({NodeId})",
                            doc.Id, doc.NodeId);
                        errors.Add((doc.Id, ex));
                    }
                });

            moveTimer.Stop();
            var avgMoveTime = documents.Count > 0 ? moveTimer.ElapsedMilliseconds / (double)documents.Count : 0;
            _fileLogger.LogInformation(
                "Parallel move completed: {Done} succeeded, {Failed} failed in {Ms}ms (avg {Avg:F1}ms/doc)",
                doneCount, errors.Count, moveTimer.ElapsedMilliseconds, avgMoveTime);

            // 3. Batch update za uspešne - sve u jednoj transakciji
            // Skip DB commit if cancellation requested to reduce freeze time
            var updateTimer = Stopwatch.StartNew();
            if (!ct.IsCancellationRequested && !successfulDocs.IsEmpty)
            {
                await MarkDocumentsAsDoneAsync(successfulDocs, ct).ConfigureAwait(false);
            }
            // 4. Batch update za greške - sve u jednoj transakciji
            if (!ct.IsCancellationRequested && !errors.IsEmpty)
            {
                await MarkDocumentsAsFailedAsync(errors, ct).ConfigureAwait(false);
                Interlocked.Add(ref _totalFailed, errors.Count);
            }
            updateTimer.Stop();

            // Save checkpoint after successful batch (skip on cancellation)
            if (!ct.IsCancellationRequested)
            {
                Interlocked.Increment(ref _batchCounter);
                await SaveCheckpointAsync(ct).ConfigureAwait(false);
            }

            sw.Stop();
            _fileLogger.LogInformation(
                "Move batch TOTAL: acquire={AcqMs}ms, move={MoveMs}ms, update={UpdMs}ms, total={TotalMs}ms | " +
                "Success={Done}, Failed={Failed} | Overall: {TotalMoved} moved, {TotalFailed} failed",
                acquireTimer.ElapsedMilliseconds, moveTimer.ElapsedMilliseconds, updateTimer.ElapsedMilliseconds, sw.ElapsedMilliseconds,
                doneCount, errors.Count, _totalMoved, _totalFailed);

            return new MoveBatchResult(doneCount, errors.Count);
        }

        public async Task RunLoopAsync(CancellationToken ct)
        {
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;

            _fileLogger.LogInformation("Move worker started");

            // Reset stuck documents from previous crashed run
            await ResetStuckItemsAsync(ct).ConfigureAwait(false);

            // Load checkpoint to resume from last position
            await LoadCheckpointAsync(ct).ConfigureAwait(false);

            // Start from next batch after checkpoint
            var batchCounter = _batchCounter + 1;

            while (!ct.IsCancellationRequested)
            {
                using var batchScope = _fileLogger.BeginScope(new Dictionary<string, object>
                {
                    ["BatchCounter"] = batchCounter
                });

                try
                {
                    _fileLogger.LogDebug("Starting batch {BatchCounter}", batchCounter);

                    var result = await RunBatchAsync(ct).ConfigureAwait(false);

                    if (result.Done == 0 && result.Failed == 0)
                    {
                        emptyResultCounter++;
                        _fileLogger.LogDebug(
                            "Empty result ({Counter}/{Max})",
                            emptyResultCounter, maxEmptyResults);

                        if (emptyResultCounter >= maxEmptyResults)
                        {
                            _fileLogger.LogInformation(
                                "Breaking after {Count} consecutive empty results",
                                emptyResultCounter);
                            break;
                        }

                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        emptyResultCounter = 0; // Reset counter on success

                        var betweenDelay = _options.Value.MoveService.DelayBetweenBatchesInMs
                            ?? _options.Value.DelayBetweenBatchesInMs;

                        if (betweenDelay > 0)
                        {
                            await Task.Delay(betweenDelay, ct).ConfigureAwait(false);
                        }
                    }

                    batchCounter++;
                }
                catch (OperationCanceledException)
                {
                    _fileLogger.LogInformation("Move worker cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    _dbLogger.LogError(ex, "Error in batch {BatchCounter}", batchCounter);

                    // Exponential backoff on error
                    await Task.Delay(delay * 2, ct).ConfigureAwait(false);
                    batchCounter++;
                }
            }

            _fileLogger.LogInformation(
                "Move worker completed after {Count} batches. Total: {Moved} moved, {Failed} failed",
                batchCounter - 1, _totalMoved, _totalFailed);
        }

        #region privates

        private async Task ResetStuckItemsAsync(CancellationToken ct)
        {
            try
            {
                var timeout = TimeSpan.FromMinutes(_options.Value.StuckItemsTimeoutMinutes);

                await using var scope = _sp.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var resetCount = await docRepo.ResetStuckDocumentsAsync(
                        uow.Connection,
                        uow.Transaction,
                        timeout,
                        ct).ConfigureAwait(false);

                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    if (resetCount > 0)
                    {
                        _fileLogger.LogWarning(
                            "Reset {Count} stuck documents that were IN PROGRESS for more than {Minutes} minutes",
                            resetCount, _options.Value.StuckItemsTimeoutMinutes);
                    }
                    else
                    {
                        _fileLogger.LogInformation("No stuck documents found");
                    }
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning(ex, "Failed to reset stuck documents");
            }
        }

        private async Task LoadCheckpointAsync(CancellationToken ct)
        {
            try
            {
                await using var scope = _sp.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var checkpointRepo = scope.ServiceProvider.GetRequiredService<IMigrationCheckpointRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var checkpoint = await checkpointRepo.GetByServiceNameAsync(ServiceName, ct).ConfigureAwait(false);
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    if (checkpoint != null)
                    {
                        _totalMoved = checkpoint.TotalProcessed;
                        _totalFailed = checkpoint.TotalFailed;
                        _batchCounter = checkpoint.BatchCounter;

                        _fileLogger.LogInformation(
                            "Checkpoint loaded: {TotalMoved} moved, {TotalFailed} failed, batch {BatchCounter}",
                            _totalMoved, _totalFailed, _batchCounter);
                    }
                    else
                    {
                        _fileLogger.LogInformation("No checkpoint found, starting fresh");
                        _totalMoved = 0;
                        _totalFailed = 0;
                        _batchCounter = 0;
                    }
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning(ex, "Failed to load checkpoint, starting fresh");
                _totalMoved = 0;
                _totalFailed = 0;
                _batchCounter = 0;
            }
        }

        private async Task SaveCheckpointAsync(CancellationToken ct)
        {
            try
            {
                await using var scope = _sp.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var checkpointRepo = scope.ServiceProvider.GetRequiredService<IMigrationCheckpointRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var checkpoint = new MigrationCheckpoint
                    {
                        ServiceName = ServiceName,
                        TotalProcessed = _totalMoved,
                        TotalFailed = _totalFailed,
                        BatchCounter = _batchCounter
                    };

                    await checkpointRepo.UpsertAsync(checkpoint, ct).ConfigureAwait(false);
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    _fileLogger.LogDebug("Checkpoint saved: {TotalMoved} moved, {TotalFailed} failed, batch {BatchCounter}",
                        _totalMoved, _totalFailed, _batchCounter);
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning(ex, "Failed to save checkpoint");
            }
        }

        private async Task<IReadOnlyList<DocStaging>> AcquireDocumentsForMoveAsync(int batch, CancellationToken ct)
        {

            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                var documents = await docRepo.TakeReadyForProcessingAsync(batch, ct).ConfigureAwait(false);

                // Mark documents as IN PROGRESS (not DONE!)
                var updates = documents.Select(d => (
                    d.Id,
                    MigrationStatus.InProgress.ToDbString(),
                    (string?)null
                ));

                await docRepo.BatchSetDocumentStatusAsync_v1(
                    uow.Connection,
                    uow.Transaction,
                    updates,
                    ct).ConfigureAwait(false);

                //foreach (var doc in documents)
                //{
                //    await docRepo.SetStatusAsync(doc.Id, MigrationStatus.InProgress.ToDbString(), null, ct).ConfigureAwait(false);
                //}

                await uow.CommitAsync().ConfigureAwait(false);
                return documents;
            }
            catch (Exception ex)
            {
                await uow.RollbackAsync().ConfigureAwait(false);
                throw;
            }

        }

        private async Task<bool> MoveSingleDocumentAsync(long docId, string nodeId, string destNodeId,CancellationToken ct)
        {
            using var scope = _fileLogger.BeginScope(new Dictionary<string, object>
            {
                ["DocumentId"] = docId,
                ["NodeId"] = nodeId,
                ["DestFolderId"] = destNodeId
            });

            _fileLogger.LogDebug("Moving document {DocId}", docId);

            var moved = await _moveExecutor.MoveAsync(nodeId, destNodeId, ct).ConfigureAwait(false);

            if (!moved)
            {
                throw new InvalidOperationException(
                    $"Move operation returned false for document {docId}");
            }

            //await UpdateDocumentStatusAsync(
            //    docId,
            //    MigrationStatus.Done.ToDbString(),
            //    null,
            //    ct);

            return moved;
        }

        private async Task UpdateDocumentStatusAsync(long docId, string status, string? errorMsg, CancellationToken ct)
        {
            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                await docRepo.SetStatusAsync(docId, status, errorMsg, ct).ConfigureAwait(false);
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);
            }
            catch
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
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

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                // Koristi batch extension method umesto pojedinačnih update-a
                var updates = errors.Select(e => (
                    e.DocId,
                    MigrationStatus.Error.ToDbString(),
                    e.Error.Message.Length > 4000
                        ? e.Error.Message[..4000]
                        : e.Error.Message
                ));

                await docRepo.BatchSetDocumentStatusAsync_v1(
                    uow.Connection,
                    uow.Transaction,
                    updates,
                    ct).ConfigureAwait(false);

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                _fileLogger.LogWarning("Marked {Count} documents as failed", errors.Count);
            }
            catch (Exception ex)
            {
                _dbLogger.LogError(ex, "Failed to mark documents as failed");
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
            }
        }

        private async Task MarkDocumentsAsDoneAsync(
            ConcurrentBag<long> docsIds,
            CancellationToken ct)
        {
            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                // Koristi batch extension method umesto pojedinačnih update-a
                var updates = docsIds.Select(id => (
                    id,
                    MigrationStatus.Done.ToDbString(),
                    (string?)null
                ));

                await docRepo.BatchSetDocumentStatusAsync_v1(
                    uow.Connection,
                    uow.Transaction,
                    updates,
                    ct).ConfigureAwait(false);

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                _fileLogger.LogWarning("Marked {Count} documents as succesed", docsIds.Count);
            }
            catch (Exception ex)
            {
                _dbLogger.LogError(ex, "Failed to mark documents as succesed");
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
            }
        }

        public async Task RunLoopAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback)
        {
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;
            var batchSize = _options.Value.MoveService.BatchSize ?? _options.Value.BatchSize;

            _fileLogger.LogInformation("Move worker started");

            // Reset stuck documents from previous crashed run
            await ResetStuckItemsAsync(ct).ConfigureAwait(false);

            // Load checkpoint to resume from last position
            await LoadCheckpointAsync(ct).ConfigureAwait(false);

            // Start from next batch after checkpoint
            var batchCounter = _batchCounter + 1;

            // Try to get total count of documents to move
            long totalCount = 0;
            try
            {
                _fileLogger.LogInformation("Attempting to count total documents to move...");

                await using var scope = _sp.CreateAsyncScope();
                var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

                totalCount = await docRepo.CountReadyForProcessingAsync(ct).ConfigureAwait(false);

                if (totalCount >= 0)
                {
                    _fileLogger.LogInformation("Total documents to move: {TotalCount}", totalCount);
                }
                else
                {
                    _fileLogger.LogWarning("Count not available, progress will show processed items only");
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning(ex, "Failed to count total documents, continuing without total count");
                totalCount = 0;
            }

            // Initial progress report
            var progress = new WorkerProgress
            {
                TotalItems = totalCount, // Will be 0 if count failed
                ProcessedItems = _totalMoved,
                BatchSize = batchSize,
                CurrentBatch = 0,
                Message = totalCount > 0
                    ? $"Starting move operation... (Total documents: {totalCount})"
                    : "Starting move operation..."
            };
            progressCallback?.Invoke(progress);

            while (!ct.IsCancellationRequested)
            {
                using var batchScope = _fileLogger.BeginScope(new Dictionary<string, object>
                {
                    ["BatchCounter"] = batchCounter
                });

                try
                {
                    _fileLogger.LogDebug("Starting batch {BatchCounter}", batchCounter);

                    var result = await RunBatchAsync(ct).ConfigureAwait(false);

                    // Update progress after each batch
                    progress.ProcessedItems = _totalMoved;
                    progress.CurrentBatch = batchCounter;
                    progress.CurrentBatchCount = result.Done + result.Failed;
                    progress.SuccessCount = result.Done;
                    progress.FailedCount = result.Failed;
                    progress.Timestamp = DateTimeOffset.UtcNow;
                    progress.Message = (result.Done + result.Failed) > 0
                        ? $"Moved {result.Done} documents in batch {batchCounter} ({result.Failed} failed)"
                        : "No more documents to move";

                    progressCallback?.Invoke(progress);

                    if (result.Done == 0 && result.Failed == 0)
                    {
                        emptyResultCounter++;
                        _fileLogger.LogDebug(
                            "Empty result ({Counter}/{Max})",
                            emptyResultCounter, maxEmptyResults);

                        if (emptyResultCounter >= maxEmptyResults)
                        {
                            _fileLogger.LogInformation(
                                "Breaking after {Count} consecutive empty results",
                                emptyResultCounter);

                            progress.Message = $"Completed: {_totalMoved} documents moved, {_totalFailed} failed";
                            progressCallback?.Invoke(progress);
                            break;
                        }

                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        emptyResultCounter = 0; // Reset counter on success

                        var betweenDelay = _options.Value.MoveService.DelayBetweenBatchesInMs
                            ?? _options.Value.DelayBetweenBatchesInMs;

                        if (betweenDelay > 0)
                        {
                            await Task.Delay(betweenDelay, ct).ConfigureAwait(false);
                        }
                    }

                    batchCounter++;
                }
                catch (OperationCanceledException)
                {
                    _fileLogger.LogInformation("Move worker cancelled");
                    progress.Message = $"Cancelled after moving {_totalMoved} documents ({_totalFailed} failed)";
                    progressCallback?.Invoke(progress);
                    throw;
                }
                catch (Exception ex)
                {
                    _dbLogger.LogError(ex, "Error in batch {BatchCounter}", batchCounter);

                    progress.Message = $"Error in batch {batchCounter}: {ex.Message}";
                    progressCallback?.Invoke(progress);

                    // Exponential backoff on error
                    await Task.Delay(delay * 2, ct).ConfigureAwait(false);
                    batchCounter++;
                }
            }

            _fileLogger.LogInformation(
                "Move worker completed after {Count} batches. Total: {Moved} moved, {Failed} failed",
                batchCounter - 1, _totalMoved, _totalFailed);
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
        //            _fileLogger.LogInformation($"TakeReadyForProcessingAsync.");
        //            documents = await dr.TakeReadyForProcessingAsync(batch, ct);
        //            _fileLogger.LogInformation($"TakeReadyForProcessingAsync returned {documents.Count}. Setitng up to status in prog!");

        //            foreach (var d in documents)
        //                await dr.SetStatusAsync(d.Id, "IN PROG", null, ct);

        //            await uow.CommitAsync();
        //            _fileLogger.LogInformation($"Statuses changed. Commit DOne!");

        //        }
        //        catch (Exception ex)
        //        {
        //            __dbLogger.LogError($"Exception: {ex.Message}!");

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
        //            using (_fileLogger.BeginScope(new Dictionary<string, object> { ["DocumentId"] = doc.Id }))
        //            {
        //                _fileLogger.LogInformation($"Prepare document {doc.Id} for move.");
        //                _fileLogger.LogInformation($"DocId: {doc.NodeId} Destination: {doc.ToPath}.");

        //                await uow.BeginAsync();
        //                try
        //                {

        //                    if (await _moveExecutor.MoveAsync(doc.NodeId, doc.ToPath, token))
        //                    {
        //                        _fileLogger.LogInformation($"Document {doc.Id} moved. Changing status.");

        //                        await dr.SetStatusAsync(doc.Id, "DONE", null, token);
        //                    }

        //                    Interlocked.Increment(ref ctnDone);
        //                    await uow.CommitAsync(ct: token);
        //                    _fileLogger.LogInformation($"Document {doc.Id} commited.");

        //                }
        //                catch (Exception ex)
        //                {
        //                    _fileLogger.LogInformation($"Exception: {ex.Message}.");

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
        //    //_fileLogger.LogInformation("No more documents to process, exiting loop."); TODO
        //    if (delay > 0)
        //        await Task.Delay(delay, ct);
        //    return toRet;
        //}

        //public async Task RunLoopAsync(CancellationToken ct)
        //{
        //    int BatchCounter = 1, counter = 0;
        //    var delay = _options.Value.IdleDelayInMs;
        //    _fileLogger.LogInformation("Worker Started");
        //    while (!ct.IsCancellationRequested)
        //    {
        //        using (_fileLogger.BeginScope(new Dictionary<string, object> { ["BatchCnt"] = BatchCounter }))
        //        {
        //            try
        //            {
        //                _fileLogger.LogInformation($"Batch Started");

        //                var resRun = await RunBatchAsync(ct);
        //                if (resRun.Done == 0 && resRun.Failed == 0)
        //                {
        //                    _fileLogger.LogInformation($"No more documents to process, exiting loop.");
        //                    counter++;
        //                    if (counter == _options.Value.BreakEmptyResults)
        //                    {
        //                        _fileLogger.LogInformation($" Break after {counter} empty results");
        //                        break;
        //                    }
        //                    //var delay = _options.Value.IdleDelayInMs;
        //                    //_fileLogger.LogInformation("No more documents to process, exiting loop."); TODO
        //                    if (delay > 0)
        //                        await Task.Delay(delay, ct);
        //                }
        //                BatchCounter++;
        //            }
        //            catch (Exception ex)
        //            {
        //                __dbLogger.LogError($"RunLoopAsync Exception: {ex.Message}.");
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
