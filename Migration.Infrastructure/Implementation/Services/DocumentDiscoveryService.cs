using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Mapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Apstaction.Interfaces;
using Migration.Apstaction.Interfaces.Wrappers;
using Migration.Apstaction.Models;
using Migration.Infrastructure.Implementation.Helpers;
using Oracle.Apstaction.Interfaces;
using System.Collections.Concurrent;
using System.Numerics;

namespace Migration.Infrastructure.Implementation.Services
{
    public class DocumentDiscoveryService : IDocumentDiscoveryService
    {
        private readonly IDocumentIngestor _ingestor;
        private readonly IDocumentReader _reader;
        private readonly IDocumentResolver _resolver;
        private readonly IDocStagingRepository _docRepo;
        private readonly IFolderStagingRepository _folderRepo;
        private readonly IOptions<MigrationOptions> _options;
        private readonly IServiceProvider _sp;
        private readonly ILogger<DocumentDiscoveryService> _logger;
        //private readonly IUnitOfWork _unitOfWork;
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
            _logger.LogInformation("RunBatchAsync started!");
            IReadOnlyList<FolderStaging> folders = null;
            int procesed = 0;
            var batch = _options.Value.DocumentDiscovery.BatchSize ?? _options.Value.BatchSize;
            var dop = _options.Value.DocumentDiscovery.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;
            List<DocStaging> docsToInser = new();

            await using( var scope =  _sp.CreateAsyncScope())
            {
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var fr = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();
                await uow.BeginAsync(ct: ct);
                try
                {
                    _logger.LogInformation("TakeReadyForProcessingAsync calling!");

                    folders = await fr.TakeReadyForProcessingAsync(batch, ct);

                    _logger.LogInformation($"TakeReadyForProcessingAsync returned {folders.Count}. Setitng up to status in prog!");

                    foreach (var f in folders)
                        await fr.SetStatusAsync(f.Id, "IN PROG", null, ct);


                    await uow.CommitAsync(ct: ct);
                    _logger.LogInformation($"Statuses changed. Commit DOne!");

                }
                catch (Exception ex)
                {
                    _logger.LogError($"Exception: {ex.Message}!");

                    await uow.RollbackAsync(ct: ct);
                    throw;
                }
            }

            if (folders != null && folders.Count > 0)
            {
                await Parallel.ForEachAsync(folders, new ParallelOptions
                {
                    MaxDegreeOfParallelism = dop,
                    CancellationToken = ct
                },
                async (folder,token) =>
                {
                    using (_logger.BeginScope(new Dictionary<string, object> { ["FolderId"] = folder.Id }))
                    {
                        try
                        {

                            _logger.LogInformation($"Geting docs for folder.");
                            var documents = await _reader.ReadBatchAsync(folder.NodeId, ct);


                            //if (documents == null || !documents.Any())
                            //{

                            //    await _unitOfWork.BeginAsync(ct: ct);
                            //    await _folderRepo.SetStatusAsync(folder.Id, "PROCESSED", null, ct);
                            //    await _unitOfWork.CommitAsync(ct: ct);
                            //    Interlocked.Increment(ref procesed); //thread safe folderProcesed++
                            //    return;
                            //}

                            var docBag = new ConcurrentBag<DocStaging>();

                            if (documents != null && documents.Count > 0)
                            {
                                var folderName = folder?.Name?.NormalizeName();
                                var newFolderPath = await _resolver.ResolveAsync(_options.Value.RootDestinationFolderId, folderName, ct);
                                _logger.LogInformation($"New folred path: {newFolderPath}");


                                docsToInser = new List<DocStaging>(documents.Count);

                                foreach (var d in documents)
                                {
                                    var item = d.Entry.ToDocStagingInsert();
                                    item.ToPath = newFolderPath;
                                    docsToInser.Add(item);
                                }
                                //izbaciti Parallel ukoliko bude malo dokumenata po folder
                                //await Parallel.ForEachAsync(documents, new ParallelOptions
                                //{
                                //    MaxDegreeOfParallelism = dop,
                                //    CancellationToken = ct
                                //},
                                //async (document, token) =>
                                //{
                                //    var toInser = document.Entry.ToDocStagingInsert();
                                //    toInser.ToPath = newFolderPath;
                                //    docBag.Add(toInser);
                                //    await Task.CompletedTask;
                                //});                            
                            }

                            var listToInsert = docBag.ToList();

                            await using var scope = _sp.CreateAsyncScope();
                            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                            var fr = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();
                            var dr = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

                            await uow.BeginAsync(ct: ct);

                            try
                            {
                                if (docsToInser != null && docsToInser.Count > 0)
                                {
                                    _ = await dr.InsertManyAsync(docsToInser, ct);
                                    _logger.LogInformation($"Docs inserted int db");

                                    //_ = await _ingestor.InserManyAsync(listToInsert, ct);
                                }

                                await fr.SetStatusAsync(folder.Id, "PROCESSED", null, ct);

                                await uow.CommitAsync(ct: ct);
                                _logger.LogInformation($"Folder status to PROCESSED. DB Commited");

                            }
                            catch (Exception exTx)
                            {
                                _logger.LogError($"Exception: {exTx.Message}");

                                await uow.RollbackAsync(ct: ct);
                                await using var failScope = _sp.CreateAsyncScope();
                                var failUow = failScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                                var failFr = failScope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

                                await failUow.BeginAsync(ct: token);
                                await failFr.FailAsync(folder.Id, exTx.Message, token);
                                await failUow.CommitAsync(ct: token);
                                return;
                            }

                            Interlocked.Increment(ref procesed); //thread safe n++

                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Exception: {ex.Message}");

                            await using var failScope = _sp.CreateAsyncScope();
                            var failUow = failScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                            var failFr = failScope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

                            await failUow.BeginAsync(ct: token);
                            await failFr.FailAsync(folder.Id, ex.Message, token);
                            await failUow.CommitAsync(ct: token);
                            //await _folderRepo.FailAsync(folder.Id, ex.Message, ct);
                        }
                    }
                   
                });
            }
            var delay = _options.Value.DocumentDiscovery.DelayBetweenBatchesInMs ?? _options.Value.DelayBetweenBatchesInMs;
            //_logger.LogInformation("No more documents to process, exiting loop."); TODO
            if (delay > 0)
                await Task.Delay(delay, ct);
            return new DocumentBatchResult(procesed);
        }
        public async Task RunLoopAsync(CancellationToken ct)
        {
            int BatchCounter = 1, couter = 0;
            var delay = _options.Value.IdleDelayInMs;

            _logger.LogInformation("Worker Started");

            while (!ct.IsCancellationRequested)
            {
                using (_logger.BeginScope(new Dictionary<string, object> { ["BatchCounter"] = BatchCounter }))
                {
                    try
                    {
                        _logger.LogInformation($"Batch Started");

                        var resRun = await RunBatchAsync(ct);
                        if (resRun.PlannedCount == 0)
                        {
                            _logger.LogInformation($"No more documents to process, exiting loop.");
                            couter++;
                            if (couter == _options.Value.BreakEmptyResults)
                            {
                                _logger.LogInformation($" Break after {couter} empty results");
                                break;
                            }
                            //_logger.LogInformation("No more documents to process, exiting loop."); TODO
                            if (delay > 0)                            
                                await Task.Delay(delay, ct);
                        }
                        var between = _options.Value.DelayBetweenBatchesInMs;
                        if (between > 0)
                            await Task.Delay(between, ct);
                        _logger.LogInformation($"Batch Done");
                    }                
                    catch (Exception ex)
                    {
                        _logger.LogError($"RunLoopAsync Exception: {ex.Message}.");
                        if (delay > 0)
                            await Task.Delay(delay, ct);
                    }
                }
                BatchCounter++;
                couter = 0;
            }
            _logger.LogInformation("RunLoopAsync END");

        }
    }
}
