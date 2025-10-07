using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Mapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Abstraction.Models;
using Migration.Infrastructure.Implementation.Helpers;
using Oracle.Abstraction.Interfaces;
using System.Collections.Concurrent;
using Migration.Extensions.Oracle;
using System.Diagnostics;


namespace Migration.Infrastructure.Implementation.Services
{
    public class DocumentDiscoveryService : IDocumentDiscoveryService
    {
        private readonly IDocumentIngestor _ingestor;
        private readonly IDocStagingRepository _docRepo;
        private readonly IFolderStagingRepository _folderRepo;
        private readonly IDocumentReader _reader;
        private readonly IDocumentResolver _resolver;
        private readonly IOptions<MigrationOptions> _options;
        private readonly IServiceProvider _sp;
        private readonly ILogger<DocumentDiscoveryService> _logger;
        //private readonly IUnitOfWork _unitOfWork;


        private readonly ConcurrentDictionary<string, string> _resolvedFoldersCache = new();
        private long _totalProcessed = 0;
        private long _totalFailed = 0;

        public DocumentDiscoveryService(IDocumentIngestor ingestor, IDocumentReader reader, IDocumentResolver resolver, IDocStagingRepository docRepo, IFolderStagingRepository folderRepo, IOptions<MigrationOptions> options, IServiceProvider sp, IUnitOfWork unitOfWork, ILogger<DocumentDiscoveryService> logger)
        {
            _ingestor = ingestor;
            _reader = reader;
            _resolver = resolver;
            _docRepo = docRepo;
            _folderRepo = folderRepo;
            _options = options;
            _sp = sp;
            _logger = logger;
            // _unitOfWork = unitOfWork;
        }

        public async Task<DocumentBatchResult> RunBatchAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            using var batchScope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Service"] = nameof(DocumentDiscoveryService),
                ["Operation"] = "RunBatch"
            });

            _logger.LogInformation("DocumentDiscovery batch started");

            var batch = _options.Value.DocumentDiscovery.BatchSize ?? _options.Value.BatchSize;
            var dop = _options.Value.DocumentDiscovery.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;

            var folders = await AcquireFoldersForProcessingAsync(batch, ct).ConfigureAwait(false);

            if (folders.Count == 0)
            {
                _logger.LogInformation("No folders ready for processing");
                return new DocumentBatchResult(0);
            }

            var processedCount = 0;

            var errors = new ConcurrentBag<(long folderId, Exception error)>();

            await Parallel.ForEachAsync(folders, new ParallelOptions
            {
                MaxDegreeOfParallelism = dop,
                CancellationToken = ct
            },
            async (folder, token) =>
            {
                try
                {
                    await ProcessSingleFolderAsync(folder, ct).ConfigureAwait(false);
                    Interlocked.Increment(ref processedCount);
                    Interlocked.Increment(ref _totalProcessed);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process folder {FolderId} ({Name})",
                           folder.Id, folder.Name);
                    errors.Add((folder.Id, ex));
                }
            });

            if (!errors.IsEmpty)
            {
                await MarkFoldersAsFailedAsync(errors, ct).ConfigureAwait(false);
                Interlocked.Add(ref _totalFailed, errors.Count);
            }

            sw.Stop();
            _logger.LogInformation(
                "DocumentDiscovery batch completed: {Processed} processed, {Failed} failed in {Elapsed}ms " +
                "(Total: {TotalProcessed} processed, {TotalFailed} failed)",
                processedCount, errors.Count, sw.ElapsedMilliseconds, _totalProcessed, _totalFailed);

            return new DocumentBatchResult(processedCount);


        }

        public async Task RunLoopAsync(CancellationToken ct)
        {
            var batchCounter = 1;
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;
            _totalProcessed = 0;
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

                    var result = await RunBatchAsync(ct).ConfigureAwait(false);

                    if (result.PlannedCount == 0)
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
                        await Task.Delay(delay,ct).ConfigureAwait(false);
                    }
                    else
                    {
                        emptyResultCounter = 0;
                        var betweenDelay = _options.Value.DocumentDiscovery.DelayBetweenBatchesInMs
                            ?? _options.Value.DelayBetweenBatchesInMs;

                        if (betweenDelay > 0)
                        {
                            await Task.Delay(betweenDelay, ct).ConfigureAwait(false);
                        }
                    }
                    batchCounter++;
                }
                catch (Exception ex)
                {

                    _logger.LogError(ex, "Error in batch {BatchCounter}", batchCounter);

                    // Exponential backoff on error
                    await Task.Delay(delay * 2, ct).ConfigureAwait(false);
                    batchCounter++;
                } 
            }
            _logger.LogInformation(
                "DocumentDiscovery worker completed after {Count} batches. " +
                "Total: {Processed} processed, {Failed} failed",
                batchCounter - 1, _totalProcessed, _totalFailed);
            
        }

        #region Private metods

        private async Task<IReadOnlyList<FolderStaging>> AcquireFoldersForProcessingAsync(int batch, CancellationToken ct)
        {
            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                var folders = await folderRepo.TakeReadyForProcessingAsync(batch, ct).ConfigureAwait(false);

                // Batch update instead of N individual updates
                var updates = folders.Select(f => (
                    f.Id,
                    MigrationStatus.InProgress.ToDbString(),
                    (string?)null
                ));

                await folderRepo.BatchSetFolderStatusAsync_v1(
                    uow.Connection,
                    uow.Transaction,
                    updates,
                    ct).ConfigureAwait(false);

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                return folders;

            }
            catch (Exception ex)
            {

                await uow.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }

        }

        private async Task ProcessSingleFolderAsync(FolderStaging folder, CancellationToken ct)
        {
            using var logScope = _logger.BeginScope(new Dictionary<string, object> 
            { 
                ["FolderId"] = folder.Id ,
                ["FolderName"] = folder.Name ?? "unknown",
                ["NodeId"] = folder.NodeId ?? "unknown"
            });


            _logger.LogDebug("Processing folder {FolderId}", folder.Id);

            var documents = await _reader.ReadBatchAsync(folder.NodeId!, ct).ConfigureAwait(false);

            if (documents == null || documents.Count == 0)
            {
                _logger.LogDebug("No documents found in folder {FolderId}", folder.Id);
                await MarkFolderAsProcessedAsync(folder.Id, ct).ConfigureAwait(false);
                return;
            }
            _logger.LogDebug("Found {Count} documents in folder {FolderId}", documents.Count, folder.Id);

            var desFolderId = await ResolveDestinationFolder(folder, ct).ConfigureAwait(false);

            var docsToInsert = new List<DocStaging>(documents.Count);

            foreach (var d in documents)
            {
                var item = d.Entry.ToDocStagingInsert();
                item.ToPath = desFolderId;
                item.Status = MigrationStatus.Ready.ToDbString();
                docsToInsert.Add(item);
            }

            await InsertDocsAndMarkFolderAsync(docsToInsert, folder.Id, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Successfully processed folder {FolderId}: {Count} documents inserted",
                folder.Id, docsToInsert.Count);


        }

        private async Task InsertDocsAndMarkFolderAsync(List<DocStaging> docsToInsert, long folderId, CancellationToken ct)
        {

            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();
            var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

            await uow.BeginAsync().ConfigureAwait(false);

            try
            {
                if (docsToInsert.Count > 0)
                {
                    var inserted = await docRepo.InsertManyAsync(docsToInsert, ct).ConfigureAwait(false); // TODO: izmeniti InsertManyAsync da vrati broj insertovanih
                    _logger.LogDebug("Inserted {Count} documents for folder {FolderId}",
                        inserted, folderId);
                }

                await folderRepo.SetStatusAsync(folderId, MigrationStatus.Processed.ToString(), null, ct).ConfigureAwait(false);
                await uow.CommitAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await uow.RollbackAsync().ConfigureAwait(false);
                throw;
            }

            //throw new NotImplementedException();
        }

        private async Task<string> ResolveDestinationFolder(FolderStaging folder, CancellationToken ct)
        {
            var normalizedName = folder.Name?.NormalizeName()
                    ?? throw new InvalidOperationException($"Folder {folder.Id} has null name");

            if(_resolvedFoldersCache.TryGetValue(normalizedName, out var cachedId))
            {
                _logger.LogDebug("Using cached destination folder ID for folder {FolderId}", folder.Id);
                return cachedId;
            }


            _logger.LogDebug("Resolving destination folder for '{Name}'", normalizedName);

            var destFolderId = await _resolver.ResolveAsync(_options.Value.RootDestinationFolderId, normalizedName, ct).ConfigureAwait(false);

            _resolvedFoldersCache.TryAdd(normalizedName, destFolderId);

            _logger.LogDebug("Resolved and cached destination folder '{Name}' -> {Id}",
                normalizedName, destFolderId);

            if (_resolvedFoldersCache.Count > 10000)
            {
                _resolvedFoldersCache.Clear();
                _logger.LogWarning("Cache cleared due to size limit (10000 entries)");
            }


            return destFolderId;
        }

        private async Task MarkFolderAsProcessedAsync(long id, CancellationToken ct)
        {
            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                await folderRepo.SetStatusAsync(
                    id,
                    MigrationStatus.Processed.ToDbString(),
                    null,
                    ct).ConfigureAwait(false);

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);
            }
            catch
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        private async Task MarkFoldersAsFailedAsync(
           ConcurrentBag<(long FolderId, Exception Error)> errors,
           CancellationToken ct)
        {
            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                // Koristi batch extension method
                var updates = errors.Select(e => (
                    e.FolderId,
                    MigrationStatus.Error.ToDbString(),
                    e.Error.Message.Length > 4000
                        ? e.Error.Message[..4000]
                        : e.Error.Message
                ));

                await folderRepo.BatchSetFolderStatusAsync_v1(
                    uow.Connection,
                    uow.Transaction,
                    updates,
                    ct).ConfigureAwait(false);

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                _logger.LogWarning("Marked {Count} folders as failed", errors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark folders as failed");
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
            }
        }


        #endregion

        #region Old Version - working (commented)
        //public async Task<DocumentBatchResult> RunBatchAsync(CancellationToken ct)
        //{
        //    _logger.LogInformation("RunBatchAsync started!");
        //    IReadOnlyList<FolderStaging> folders = null;
        //    int procesed = 0;
        //    var batch = _options.Value.DocumentDiscovery.BatchSize ?? _options.Value.BatchSize;
        //    var dop = _options.Value.DocumentDiscovery.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;
        //    List<DocStaging> docsToInser = new();

        //    await using (var scope = _sp.CreateAsyncScope())
        //    {
        //        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        //        var fr = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();
        //        await uow.BeginAsync(ct: ct);
        //        try
        //        {
        //            _logger.LogInformation("TakeReadyForProcessingAsync calling!");

        //            folders = await fr.TakeReadyForProcessingAsync(batch, ct);

        //            _logger.LogInformation($"TakeReadyForProcessingAsync returned {folders.Count}. Setitng up to status in prog!");

        //            foreach (var f in folders)
        //                await fr.SetStatusAsync(f.Id, MigrationStatus.InProgress.ToString(), null, ct);


        //            await uow.CommitAsync(ct: ct);
        //            _logger.LogInformation($"Statuses changed. Commit DOne!");

        //        }
        //        catch (Exception ex)
        //        {
        //            _logger.LogError($"Exception: {ex.Message}!");

        //            await uow.RollbackAsync(ct: ct);
        //            throw;
        //        }
        //    }

        //    if (folders != null && folders.Count > 0)
        //    {
        //        await Parallel.ForEachAsync(folders, new ParallelOptions
        //        {
        //            MaxDegreeOfParallelism = dop,
        //            CancellationToken = ct
        //        },
        //        async (folder, token) =>
        //        {
        //            using (_logger.BeginScope(new Dictionary<string, object> { ["FolderId"] = folder.Id }))
        //            {
        //                try
        //                {

        //                    _logger.LogInformation($"Geting docs for folder.");
        //                    var documents = await _reader.ReadBatchAsync(folder.NodeId, ct);


        //                    //if (documents == null || !documents.Any())
        //                    //{

        //                    //    await _unitOfWork.BeginAsync(ct: ct);
        //                    //    await _folderRepo.SetStatusAsync(folder.Id, "PROCESSED", null, ct);
        //                    //    await _unitOfWork.CommitAsync(ct: ct);
        //                    //    Interlocked.Increment(ref procesed); //thread safe folderProcesed++
        //                    //    return;
        //                    //}

        //                    var docBag = new ConcurrentBag<DocStaging>();

        //                    if (documents != null && documents.Count > 0)
        //                    {
        //                        var folderName = folder?.Name?.NormalizeName();
        //                        var newFolderPath = await _resolver.ResolveAsync(_options.Value.RootDestinationFolderId, folderName, ct);
        //                        _logger.LogInformation($"New folred path: {newFolderPath}");


        //                        docsToInser = new List<DocStaging>(documents.Count);

        //                        foreach (var d in documents)
        //                        {
        //                            var item = d.Entry.ToDocStagingInsert();
        //                            item.ToPath = newFolderPath;
        //                            docsToInser.Add(item);
        //                        }
        //                        //izbaciti Parallel ukoliko bude malo dokumenata po folder
        //                        //await Parallel.ForEachAsync(documents, new ParallelOptions
        //                        //{
        //                        //    MaxDegreeOfParallelism = dop,
        //                        //    CancellationToken = ct
        //                        //},
        //                        //async (document, token) =>
        //                        //{
        //                        //    var toInser = document.Entry.ToDocStagingInsert();
        //                        //    toInser.ToPath = newFolderPath;
        //                        //    docBag.Add(toInser);
        //                        //    await Task.CompletedTask;
        //                        //});                            
        //                    }

        //                    var listToInsert = docBag.ToList();

        //                    await using var scope = _sp.CreateAsyncScope();
        //                    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        //                    var fr = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();
        //                    var dr = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

        //                    await uow.BeginAsync(ct: ct);

        //                    try
        //                    {
        //                        if (docsToInser != null && docsToInser.Count > 0)
        //                        {
        //                            _ = await dr.InsertManyAsync(docsToInser, ct);
        //                            _logger.LogInformation($"Docs inserted int db");

        //                            //_ = await _ingestor.InserManyAsync(listToInsert, ct);
        //                        }

        //                        await fr.SetStatusAsync(folder.Id, MigrationStatus.Processed.ToDbString(), null, ct);

        //                        await uow.CommitAsync(ct: ct);
        //                        _logger.LogInformation($"Folder status to PROCESSED. DB Commited");

        //                    }
        //                    catch (Exception exTx)
        //                    {
        //                        _logger.LogError($"Exception: {exTx.Message}");

        //                        await uow.RollbackAsync(ct: ct);
        //                        await using var failScope = _sp.CreateAsyncScope();
        //                        var failUow = failScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        //                        var failFr = failScope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

        //                        await failUow.BeginAsync(ct: token);
        //                        await failFr.FailAsync(folder.Id, exTx.Message, token);
        //                        await failUow.CommitAsync(ct: token);
        //                        return;
        //                    }

        //                    Interlocked.Increment(ref procesed); //thread safe n++

        //                }
        //                catch (Exception ex)
        //                {
        //                    _logger.LogError($"Exception: {ex.Message}");

        //                    await using var failScope = _sp.CreateAsyncScope();
        //                    var failUow = failScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        //                    var failFr = failScope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

        //                    await failUow.BeginAsync(ct: token);
        //                    await failFr.FailAsync(folder.Id, ex.Message, token);
        //                    await failUow.CommitAsync(ct: token);
        //                    //await _folderRepo.FailAsync(folder.Id, ex.Message, ct);
        //                }
        //            }

        //        });
        //    }
        //    var delay = _options.Value.DocumentDiscovery.DelayBetweenBatchesInMs ?? _options.Value.DelayBetweenBatchesInMs;
        //    //_logger.LogInformation("No more documents to process, exiting loop."); TODO
        //    if (delay > 0)
        //        await Task.Delay(delay, ct);
        //    return new DocumentBatchResult(procesed);
        //}
        //public async Task RunLoopAsync(CancellationToken ct)
        //{
        //    int BatchCounter = 1, couter = 0;
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
        //                if (resRun.PlannedCount == 0)
        //                {
        //                    _logger.LogInformation($"No more documents to process, exiting loop.");
        //                    couter++;
        //                    if (couter == _options.Value.BreakEmptyResults)
        //                    {
        //                        _logger.LogInformation($" Break after {couter} empty results");
        //                        break;
        //                    }
        //                    //_logger.LogInformation("No more documents to process, exiting loop."); TODO
        //                    if (delay > 0)
        //                        await Task.Delay(delay, ct);
        //                }
        //                var between = _options.Value.DelayBetweenBatchesInMs;
        //                if (between > 0)
        //                    await Task.Delay(between, ct);
        //                _logger.LogInformation($"Batch Done");
        //            }
        //            catch (Exception ex)
        //            {
        //                _logger.LogError($"RunLoopAsync Exception: {ex.Message}.");
        //                if (delay > 0)
        //                    await Task.Delay(delay, ct);
        //            }
        //        }
        //        BatchCounter++;
        //        couter = 0;
        //    }
        //    _logger.LogInformation("RunLoopAsync END");

        //} 
        #endregion
    }
}
