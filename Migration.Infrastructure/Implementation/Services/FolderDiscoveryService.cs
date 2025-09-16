using Alfresco.Contracts.Options;
using Mapper;
using Microsoft.Extensions.Options;
using Migration.Apstaction.Interfaces;
using Migration.Apstaction.Interfaces.Services;
using Migration.Apstaction.Models;
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
        private readonly IOptions<MigrationOptions> _options;
        private FolderSeekCursor? _cursor = null;

        public FolderDiscoveryService(IFolderIngestor ingestor, IFolderReader reader, IOptions<MigrationOptions> options)
        {
            _ingestor = ingestor;
            _reader = reader;
            _options = options;
        }

        public async Task<FolderBatchResult> RunBatchAsync(CancellationToken ct)
        {
            var cnt = 0;
            var batch = _options.Value.DocumentDiscovery.BatchSize ?? _options.Value.BatchSize;
            var dop = _options.Value.DocumentDiscovery.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;            


            
            var folderRequest = new FolderReaderRequest(
                _options.Value.RootDiscoveryFolderId, _options.Value.FolderDiscovery.NameFilter ?? "-", 0, batch, _cursor
                );
            var page = await _reader.ReadBatchAsync(folderRequest, ct);            
            
            if(!page.HasMore) return new FolderBatchResult(cnt);

            var toInsert = page.Items.ToList().ToFolderStagingList();

            if (toInsert.Count > 0)
            {
                cnt = await _ingestor.InserManyAsync(toInsert, ct);
            }          

            _cursor = page.Next;

            return new FolderBatchResult(cnt);

        }

        public async Task RunLoopAsync(CancellationToken ct)
        {
            var delay = _options.Value.IdleDelayInMs;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var resRun = await RunBatchAsync(ct);
                    if (resRun.InsertedCount == 0)
                    {
                       
                        //_logger.LogInformation("No more documents to process, exiting loop."); TODO
                        if (delay > 0)
                            await Task.Delay(delay, ct);
                    }
                    var between = _options.Value.DelayBetweenBatchesInMs;
                    if (between > 0) 
                        await Task.Delay(between, ct);
                }
                catch (Exception)
                {
                    //_logger.Error("No more documents to process, exiting loop."); TODO
                    if (delay > 0)
                        await Task.Delay(delay, ct);
                }
            }
        }
    }
}
