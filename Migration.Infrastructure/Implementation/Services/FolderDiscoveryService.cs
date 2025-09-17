using Alfresco.Contracts.Options;
using Mapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Migration.Apstaction.Interfaces;
using Migration.Apstaction.Interfaces.Services;
using Migration.Apstaction.Models;
using Oracle.Apstaction.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
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
        private readonly IServiceProvider _sp;
        private readonly IUnitOfWork _unitOfWork;


        public FolderDiscoveryService(IFolderIngestor ingestor, IFolderReader reader, IOptions<MigrationOptions> options, IServiceProvider sp, IUnitOfWork unitOfWork)
        {
            _ingestor = ingestor;
            _reader = reader;
            _options = options;
            _sp = sp;
            _unitOfWork = unitOfWork;
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
            //using var scope = _sp.CreateScope();

            //var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await _unitOfWork.BeginAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                var toInsert = page.Items.ToList().ToFolderStagingList();

                if (toInsert.Count > 0)
                {
                    cnt = await _ingestor.InserManyAsync(toInsert, ct);
                }
                _cursor = page.Next;
                await _unitOfWork.CommitAsync();
                return new FolderBatchResult(cnt);
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackAsync();
                throw;
            }
                 



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
                catch (Exception ex)
                {
                    //_logger.Error("No more documents to process, exiting loop."); TODO
                    if (delay > 0)
                        await Task.Delay(delay, ct);
                }
            }
        }
    }
}
