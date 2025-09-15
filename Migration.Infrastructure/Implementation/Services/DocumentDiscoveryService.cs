using Alfresco.Contracts.Oracle.Models;
using Mapper;
using Migration.Apstaction.Interfaces;
using Migration.Apstaction.Interfaces.Wrappers;
using Migration.Apstaction.Models;
using Migration.Infrastructure.Implementation.Helpers;
using Oracle.Apstaction.Interfaces;

namespace Migration.Infrastructure.Implementation.Services
{
    public class DocumentDiscoveryService : IDocumentDiscoveryService
    {
        private readonly IDocumentIngestor _ingestor;
        private readonly IDocumentReader _reader;
        private readonly IDocumentResolver _resolver;
        private readonly IDocStagingRepository _docRepo;
        private readonly IFolderStagingRepository _folderRepo;        
        public DocumentDiscoveryService(IDocumentIngestor ingestor, IDocumentReader reader, IDocumentResolver resolver, IDocStagingRepository docRepo, IFolderStagingRepository folderRepo)
        {
            _ingestor = ingestor;
            _reader = reader;
            _resolver = resolver;
            _docRepo = docRepo;
            _folderRepo = folderRepo;
        }
        public async Task<DocumentBatchResult> RunBatchAsync(DocumentDiscoveryBatchRequest inRequest, CancellationToken ct)
        {
            var cnt = 0;
            var folders = await _folderRepo.TakeReadyForProcessingAsync(inRequest.Take, ct);

            if (folders != null && folders.Count > 0)
            {
                               
                    foreach (var folder in folders)
                    {
                        try
                        {

                            var documents = await _reader.ReadBatchAsync(folder.NodeId, ct);
                            var listToInsert = new List<DocStaging>(documents.Count());
                            await _folderRepo.SetStatusAsync(folder.Id, "PREPARED", null, ct);

                            foreach (var doc in documents)
                            {
                                var folderName = doc.Entry.Name.NormalizeName();
                                var newFolderPath = await _resolver.ResolveAsync(inRequest.RootDestinationFolder, folderName, ct);
                                var toInser = doc.Entry.ToDocStaging();
                                toInser.ToPath = newFolderPath;
                                listToInsert.Add(toInser);
                            }

                            var x = await _ingestor.InserManyAsync(listToInsert, ct);
                            //folder.Status = "PROCESSED";
                            await _folderRepo.SetStatusAsync(folder.Id, "PROCESSED", null, ct);
                            cnt++;
                        }
                        catch (Exception ex)
                        {
                            await _folderRepo.FailAsync(folder.Id, ex.Message, ct);
                        }
                    }               
                
            }
            return new DocumentBatchResult(cnt);
        }
        public async Task RunLoopAsync(DocumentDiscoveryLoopOptions inOptions, CancellationToken ct)
        {            
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var resRun = await RunBatchAsync(inOptions.Batch, ct);
                    if (resRun.PlannedCount == 0)
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
