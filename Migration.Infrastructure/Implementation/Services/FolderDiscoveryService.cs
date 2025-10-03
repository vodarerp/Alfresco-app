using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Mapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Apstraction.Interfaces;
using Migration.Apstraction.Interfaces.Services;
using Migration.Apstraction.Models;
using Oracle.Apstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
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

        // private FolderSeekCursor? _cursor = null;
        private readonly object _cursorLock = new();

        // Metrics tracking
        private long _totalInserted = 0;

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
            var sw = Stopwatch.StartNew();

            using var batchScope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Service"] = nameof(FolderDiscoveryService),
                ["Operation"] = "RunBatch"
            });
            var batch = _options.Value.FolderDiscovery.BatchSize ?? _options.Value.BatchSize;
            var nameFilter = _options.Value.FolderDiscovery.NameFilter ?? "-";
            var rootDiscoveryId = _options.Value.RootDiscoveryFolderId;

            // Thread-safe cursor read
            FolderSeekCursor? currentCursor;
            lock (_cursorLock)
            {
                currentCursor = _cursor;
            }

            var folderRequest = new FolderReaderRequest(
                RootId: rootDiscoveryId,
                NameFilter: nameFilter,
                Skip: 0,
                Take: batch,
                Cursor: currentCursor);

            var page = await _reader.ReadBatchAsync(folderRequest, ct);

            if (!page.HasMore || page.Items.Count == 0)
            {
                _logger.LogInformation("No more folders to process");
                return new FolderBatchResult(0);
            }

            _logger.LogInformation("Read {Count} folders from Alfresco", page.Items.Count);

            var foldersToInsert = page.Items.ToList().ToFolderStagingListInsert();

            var inserted = await InsertFoldersAsync(foldersToInsert, ct);

            lock (_cursorLock) 
            {
                _cursor = page.Next;
            }

            Interlocked.Add(ref _totalInserted, inserted);

            sw.Stop();
            _logger.LogInformation(
                "FolderDiscovery batch completed: {Count} folders inserted in {Elapsed}ms (Total: {Total} inserted)",
                inserted, sw.ElapsedMilliseconds, _totalInserted);

            return new FolderBatchResult(inserted);
        }

        public async Task RunLoopAsync(CancellationToken ct)
        {
            var batchCounter = 1;
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;

            _logger.LogInformation("FolderDiscovery worker started");

            // Reset metrics
            _totalInserted = 0;

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

                    if (result.InsertedCount == 0)
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

                        var betweenDelay = _options.Value.FolderDiscovery.DelayBetweenBatchesInMs
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
                    _logger.LogInformation("FolderDiscovery worker cancelled");
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
                "FolderDiscovery worker completed after {Count} batches. Total: {Total} folders inserted",
                batchCounter - 1, _totalInserted);
        }



        #region private methods

        private async Task<int> InsertFoldersAsync(
            List<FolderStaging> folders,
            CancellationToken ct)
        {
            if (folders.Count == 0)
            {
                return 0;
            }

            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

            await uow.BeginAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                var inserted = await folderRepo.InsertManyAsync(folders, ct);
                await uow.CommitAsync(ct: ct);

                _logger.LogDebug("Successfully inserted {Count} folders", inserted);
                return inserted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to insert {Count} folders", folders.Count);
                await uow.RollbackAsync(ct: ct);
                throw;
            }
        }


        #endregion

        #region Older version - wroking (commented)
        //public async Task<FolderBatchResult> RunBatchAsync(CancellationToken ct)
        //{
        //    _logger.LogInformation("RunBatchAsync Started");

        //    var cnt = 0;
        //    var batch = _options.Value.DocumentDiscovery.BatchSize ?? _options.Value.BatchSize;
        //    var dop = _options.Value.DocumentDiscovery.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;


        //    var folderRequest = new FolderReaderRequest(
        //        _options.Value.RootDiscoveryFolderId, _options.Value.FolderDiscovery.NameFilter ?? "-", 0, batch, _cursor
        //        );

        //    _logger.LogInformation("_reader.ReadBatchAsync called");
        //    var page = await _reader.ReadBatchAsync(folderRequest, ct);

        //    if (!page.HasMore) return new FolderBatchResult(cnt);
        //    //using var scope = _sp.CreateScope();

        //    //var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        //    await _unitOfWork.BeginAsync(IsolationLevel.ReadCommitted, ct);
        //    try
        //    {
        //        var toInsert = page.Items.ToList().ToFolderStagingListInsert();

        //        if (toInsert.Count > 0)
        //        {
        //            _logger.LogInformation("_ingestor.InserManyAsync called");

        //            cnt = await _ingestor.InserManyAsync(toInsert, ct);
        //        }
        //        _cursor = page.Next;
        //        await _unitOfWork.CommitAsync();
        //        _logger.LogInformation("RunBatchAsync Commited");
        //        return new FolderBatchResult(cnt);
        //    }
        //    catch (Exception ex)
        //    {
        //        await _unitOfWork.RollbackAsync();
        //        _logger.LogInformation("RunBatchAsync Rollback");
        //        _logger.LogError("RunBatchAsync crashed!! {errMsg}!", ex.Message);
        //        return new FolderBatchResult(0);

        //    }




        //}

        //public async Task RunLoopAsync(CancellationToken ct)
        //{
        //    int BatchCounter = 1, couter = 0;
        //    var delay = _options.Value.IdleDelayInMs;
        //    _logger.LogInformation("Worker Started");
        //    while (!ct.IsCancellationRequested)
        //    {
        //        using (_logger.BeginScope(new Dictionary<string, object> { ["BatchCounter"] = BatchCounter }))
        //        {
        //            try
        //            {
        //                _logger.LogInformation($"Batch Started");

        //                var resRun = await RunBatchAsync(ct);

        //                if (resRun.InsertedCount == 0)
        //                {
        //                    _logger.LogInformation($"No more documents to process, exiting loop.");
        //                    couter++;
        //                    if (couter == _options.Value.BreakEmptyResults)
        //                    {
        //                        _logger.LogInformation($" Break after {couter} empty results");
        //                        break;
        //                    }
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
