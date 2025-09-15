using Migration.Apstaction.Interfaces;
using Migration.Apstaction.Interfaces.Services;
using Migration.Apstaction.Models;
using Mapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    public class FolderDiscoveryService : IFolderDiscoveryService
    {
        private readonly IFolderIngestor _ingestor;
        private readonly IFolderReader _reader;

        public FolderDiscoveryService(IFolderIngestor ingestor, IFolderReader reader)
        {
            _ingestor = ingestor;
            _reader = reader;
        }

        public async Task<FolderBatchResult> RunBatchAsync(FolderDiscoveryBatchRequest inRequest, CancellationToken ct)
        {
            var cnt = 0;

            
            var folders = await _reader.ReadBatchAsync(inRequest.FolderRequest, ct);

            var toInsert = folders.ToList().ToFolderStagingList();

            var x = await _ingestor.InserManyAsync(toInsert, ct);

            cnt += x;

            return new FolderBatchResult(cnt);

        }

        public async Task RunLoopAsync(FolderDiscoveryLoopOptions inOptions, CancellationToken ct)
        {

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var resRun = await RunBatchAsync(inOptions.Batch, ct);
                    if (resRun.InsertedCount == 0)
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
