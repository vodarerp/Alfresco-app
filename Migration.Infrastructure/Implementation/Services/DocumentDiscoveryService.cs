using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Mapper;
using Microsoft.Extensions.Options;
using Migration.Apstaction.Interfaces;
using Migration.Apstaction.Interfaces.Wrappers;
using Migration.Apstaction.Models;
using Migration.Infrastructure.Implementation.Helpers;
using Oracle.Apstaction.Interfaces;
using System.Collections.Concurrent;

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
        public DocumentDiscoveryService(IDocumentIngestor ingestor, IDocumentReader reader, IDocumentResolver resolver, IDocStagingRepository docRepo, IFolderStagingRepository folderRepo, IOptions<MigrationOptions> options)
        {
            _ingestor = ingestor;
            _reader = reader;
            _resolver = resolver;
            _docRepo = docRepo;
            _folderRepo = folderRepo;
            _options = options;
        }
        public async Task<DocumentBatchResult> RunBatchAsync(CancellationToken ct)
        {            
            int procesed = 0;
            var batch = _options.Value.DocumentDiscovery.BatchSize ?? _options.Value.BatchSize;
            var dop = _options.Value.DocumentDiscovery.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;
            var folders = await _folderRepo.TakeReadyForProcessingAsync(batch, ct);

            if (folders != null && folders.Count > 0)
            {
                await Parallel.ForEachAsync(folders, new ParallelOptions
                {
                    MaxDegreeOfParallelism = dop,
                    CancellationToken = ct
                },
                async (folder,token) =>
                {

                    try
                    {
                        var documents = await _reader.ReadBatchAsync(folder.NodeId, ct);                      

                        if (documents == null || !documents.Any())
                        {
                            await _folderRepo.SetStatusAsync(folder.Id, "PROCESSED", null, ct);
                            Interlocked.Increment(ref procesed); //thread safe folderProcesed++
                            return;
                        }

                        var docBag = new ConcurrentBag<DocStaging>();

                        await Parallel.ForEachAsync(documents, new ParallelOptions
                        {
                            MaxDegreeOfParallelism = dop,
                            CancellationToken = ct
                        },
                        async (document, token) =>
                        {
                            var folderName = document.Entry.Name.NormalizeName();
                            var newFolderPath = await _resolver.ResolveAsync(_options.Value.RootDestinationFolderId, folderName, ct);
                            var toInser = document.Entry.ToDocStaging();
                            toInser.ToPath = newFolderPath;

                            docBag.Add(toInser);
                        });

                        var listToInsert = docBag.ToList();

                        if (listToInsert != null && listToInsert.Count > 0)
                        {
                            _ = await _ingestor.InserManyAsync(listToInsert, ct);
                        }
                        await _folderRepo.SetStatusAsync(folder.Id, "PROCESSED", null, ct);
                       
                        Interlocked.Increment(ref procesed); //thread safe folderProcesed++

                    }
                    catch (Exception ex)
                    {
                        await _folderRepo.FailAsync(folder.Id, ex.Message, ct);
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
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var resRun = await RunBatchAsync(ct);
                    if (resRun.PlannedCount == 0)
                    {
                        var delay = _options.Value.IdleDelayInMs;
                        //_logger.LogInformation("No more documents to process, exiting loop."); TODO
                        if (delay > 0)                            
                            await Task.Delay(delay, ct);
                    }
                }                
                catch (Exception ex)
                {
                    
                }
            }
        }       
    }
}
