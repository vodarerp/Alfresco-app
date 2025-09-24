using Alfresco.Contracts.Options;
using Mapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<FolderDiscoveryService> _logger;


        public FolderDiscoveryService(IFolderIngestor ingestor, IFolderReader reader, IOptions<MigrationOptions> options, IServiceProvider sp, IUnitOfWork unitOfWork, ILogger<FolderDiscoveryService> logger)
        {
            _ingestor = ingestor;
            _reader = reader;
            _options = options;
            _sp = sp;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<FolderBatchResult> RunBatchAsync(CancellationToken ct)
        {
            _logger.LogInformation("RunBatchAsync Started");

            var cnt = 0;
            var batch = _options.Value.DocumentDiscovery.BatchSize ?? _options.Value.BatchSize;
            var dop = _options.Value.DocumentDiscovery.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;


            var folderRequest = new FolderReaderRequest(
                _options.Value.RootDiscoveryFolderId, _options.Value.FolderDiscovery.NameFilter ?? "-", 0, batch, _cursor
                );

            _logger.LogInformation("_reader.ReadBatchAsync called");
            var page = await _reader.ReadBatchAsync(folderRequest, ct);            
            
            if(!page.HasMore) return new FolderBatchResult(cnt);
            //using var scope = _sp.CreateScope();

            //var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await _unitOfWork.BeginAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                var toInsert = page.Items.ToList().ToFolderStagingListInsert();

                if (toInsert.Count > 0)
                {
                    _logger.LogInformation("_ingestor.InserManyAsync called");

                    cnt = await _ingestor.InserManyAsync(toInsert, ct);
                }
                _cursor = page.Next;
                await _unitOfWork.CommitAsync();
                _logger.LogInformation("RunBatchAsync Commited");
                return new FolderBatchResult(cnt);
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackAsync();
                _logger.LogInformation("RunBatchAsync Rollback");
                _logger.LogError("RunBatchAsync crashed!! {errMsg}!", ex.Message);
                return new FolderBatchResult(0);

            }




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
                            
                        if (resRun.InsertedCount == 0)
                        {
                            _logger.LogInformation($"No more documents to process, exiting loop.");
                            couter++;
                            if (couter == _options.Value.BreakEmptyResults)
                            {
                                _logger.LogInformation($" Break after {couter} empty results");
                                break;
                            }
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
