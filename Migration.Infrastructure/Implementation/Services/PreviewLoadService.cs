using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Mapper;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.Request;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Abstraction.Models;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    public class PreviewLoadService : IPreviewLoadService
    {
        private readonly IAlfrescoReadApi _alfrescoReadApi;
        private readonly IOptions<MigrationOptions> _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _dbLogger;
        private readonly ILogger _fileLogger;
        private readonly ILogger _uiLogger;
        private readonly IOpisToTipMapper _opisToTipMapper;

        // State tracking
        private Dictionary<string, string>? _dossierFolders;
        private int _currentFolderTypeIndex = 0;
        private long _totalDocumentsProcessed = 0;
        private long _totalFailed = 0;
        private int _batchCounter = 0;

        // Per-folder skip sets — ConcurrentDictionary<int, byte> used as thread-safe HashSet<int>
        private ConcurrentDictionary<string, ConcurrentDictionary<int, byte>> _processedSkipsPerFolder = new();
        private ConcurrentDictionary<string, ConcurrentDictionary<int, byte>> _failedSkipsPerFolder = new();

        // 5 total attempts (1 initial + 4 retries), delays 2/4/8/16 s
        private static readonly int[] RetryDelaysSeconds = [2, 4, 8, 16];
        private const int MaxRetryAttempts = 4;

        public PreviewLoadService(
            IAlfrescoReadApi alfrescoReadApi,
            IOptions<MigrationOptions> options,
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory,
            IOpisToTipMapper opisToTipMapper)
        {
            _alfrescoReadApi = alfrescoReadApi ?? throw new ArgumentNullException(nameof(alfrescoReadApi));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _opisToTipMapper = opisToTipMapper ?? throw new ArgumentNullException(nameof(opisToTipMapper));
            _dbLogger = loggerFactory.CreateLogger("DbLogger");
            _fileLogger = loggerFactory.CreateLogger("FileLogger");
            _uiLogger = loggerFactory.CreateLogger("UiLogger");
        }

        public Task<bool> RunLoopAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback)
            => RunLoopAsync(ct, progressCallback, folderFilter: null);

        public async Task<bool> RunLoopAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback, string? folderFilter)
        {
            var sw = Stopwatch.StartNew();
            var batchSize = _options.Value.DocumentTypeDiscovery.BatchSize;
            var maxDocs = _options.Value.MaxDocumentsToProcess;

            // Reset per-run state
            _totalDocumentsProcessed = 0;
            _totalFailed = 0;
            _batchCounter = 0;
            _currentFolderTypeIndex = 0;
            _processedSkipsPerFolder = new ConcurrentDictionary<string, ConcurrentDictionary<int, byte>>();
            _failedSkipsPerFolder = new ConcurrentDictionary<string, ConcurrentDictionary<int, byte>>();

            _fileLogger.LogInformation("PreviewLoadService started (folderFilter={FolderFilter})", folderFilter ?? "sve");
            _uiLogger.LogInformation("PreviewLoadService started (folderFilter={FolderFilter})", folderFilter ?? "sve");

            await LoadCheckpointAsync(ct).ConfigureAwait(false);

            progressCallback?.Invoke(new WorkerProgress
            {
                TotalItems = maxDocs,
                ProcessedItems = _totalDocumentsProcessed,
                CurrentBatch = 0,
                BatchSize = batchSize,
                CurrentBatchCount = 0,
                SuccessCount = 0,
                FailedCount = 0,
                Message = "Starting preview load...",
                Timestamp = DateTimeOffset.UtcNow
            });

            try
            {
                _dossierFolders = GetSubDossiersFolders();

                if (!string.IsNullOrWhiteSpace(folderFilter))
                {
                    var key = folderFilter.Trim().ToUpperInvariant();
                    _dossierFolders = _dossierFolders
                        .Where(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(kv => kv.Key, kv => kv.Value);
                }

                if (_dossierFolders == null || !_dossierFolders.Any())
                {
                    _uiLogger.LogWarning("PreviewLoadService: No dossier folders configured (filter={Filter}). Check RootPIFolderId/RootLEFolderId in appsettings.", folderFilter ?? "sve");
                    return false;
                }

                var folderTypes = _dossierFolders.Keys.OrderBy(k => k).ToList();

                for (int i = _currentFolderTypeIndex; i < folderTypes.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    if (maxDocs > 0 && Interlocked.Read(ref _totalDocumentsProcessed) >= maxDocs)
                    {
                        _fileLogger.LogInformation(
                            "PreviewLoadService: Reached MaxDocumentsToProcess={MaxDocs}. Total processed: {TotalProcessed}",
                            maxDocs, _totalDocumentsProcessed);
                        break;
                    }

                    var currFolderType = folderTypes[i];
                    _currentFolderTypeIndex = i;
                    var folderPath = _dossierFolders[currFolderType];

                    await ParralelProccesDocumentsAsync(currFolderType, folderPath, ct).ConfigureAwait(false);

                    progressCallback?.Invoke(new WorkerProgress
                    {
                        TotalItems = maxDocs,
                        ProcessedItems = Interlocked.Read(ref _totalDocumentsProcessed),
                        CurrentBatch = _batchCounter,
                        BatchSize = batchSize,
                        SuccessCount = (int)Interlocked.Read(ref _totalDocumentsProcessed),
                        FailedCount = (int)Interlocked.Read(ref _totalFailed),
                        Message = $"Folder {currFolderType} done",
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }

                _fileLogger.LogInformation(
                    "PreviewLoadService: Completed. Total inserted: {Total}, failed: {Failed}, elapsed: {Elapsed}s",
                    _totalDocumentsProcessed, _totalFailed, sw.Elapsed.TotalSeconds);
                _uiLogger.LogInformation(
                    "PreviewLoadService: Done. Inserted={Total}, Failed={Failed}",
                    _totalDocumentsProcessed, _totalFailed);
            }
            catch (OperationCanceledException)
            {
                _fileLogger.LogWarning("PreviewLoadService: Cancelled after {Elapsed}s", sw.Elapsed.TotalSeconds);
                _uiLogger.LogWarning("PreviewLoadService: Cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex, "PreviewLoadService: Fatal error after {Elapsed}s", sw.Elapsed.TotalSeconds);
                _uiLogger.LogError("PreviewLoadService: Fatal error - {Message}", ex.Message);
                throw;
            }

            return true;
        }

        private async Task ParralelProccesDocumentsAsync(string currFolderType, string folderPath, CancellationToken ct)
        {
            var batchSize = _options.Value.DocumentTypeDiscovery.BatchSize;
            var maxParallelism = _options.Value.DocumentTypeDiscovery.MaxDegreeOfParallelism > 0
                ? _options.Value.DocumentTypeDiscovery.MaxDegreeOfParallelism
                : 5;
            var maxDocs = _options.Value.MaxDocumentsToProcess;
            var regex = new Regex($"^{Regex.Escape(currFolderType)}[0-9]", RegexOptions.IgnoreCase);
            var query = BuildPreviewSearchQuery(folderPath);
            var totalCount = await GetTotalDocumentCountAsync(query, ct).ConfigureAwait(false);

            _fileLogger.LogInformation(
                "PreviewLoadService DOSSIER-{Type}: totalCount={TotalCount}, batchSize={BatchSize}, parallelism={Parallelism}",
                currFolderType, totalCount, batchSize, maxParallelism);

            if (totalCount == 0)
            {
                _fileLogger.LogInformation("PreviewLoadService DOSSIER-{Type}: No documents found, skipping", currFolderType);
                return;
            }

            var processedSkips = _processedSkipsPerFolder.GetOrAdd(currFolderType, _ => new ConcurrentDictionary<int, byte>());
            var failedSkips = _failedSkipsPerFolder.GetOrAdd(currFolderType, _ => new ConcurrentDictionary<int, byte>());

            // Full skip range for this folder
            var allSkips = Enumerable
                .Range(0, (totalCount + batchSize - 1) / batchSize)
                .Select(i => i * batchSize)
                .ToList();

            // Pending = full range minus already-processed
            var pendingSkips = allSkips
                .Where(s => !processedSkips.ContainsKey(s))
                .ToList();

            if (pendingSkips.Count == 0)
            {
                _fileLogger.LogInformation(
                    "PreviewLoadService DOSSIER-{Type}: All {Count} batches already processed, skipping",
                    currFolderType, allSkips.Count);
                await LogReconciliationAsync(currFolderType, totalCount, ct).ConfigureAwait(false);
                return;
            }

            // Prioritize previously failed skips
            var failedFirst = new HashSet<int>(failedSkips.Keys);
            pendingSkips = pendingSkips
                .OrderBy(s => failedFirst.Contains(s) ? 0 : 1)
                .ThenBy(s => s)
                .ToList();

            _fileLogger.LogInformation(
                "PreviewLoadService DOSSIER-{Type}: {Pending} batches pending ({Failed} priority-failed, {Fresh} fresh) of {Total} total",
                currFolderType,
                pendingSkips.Count,
                failedFirst.Count(pendingSkips.Contains),
                pendingSkips.Count - failedFirst.Count(pendingSkips.Contains),
                allSkips.Count);

            var dbWriteLock = new SemaphoreSlim(1, 1);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = ct
            };

            await Parallel.ForEachAsync(pendingSkips, parallelOptions, async (skipCount, token) =>
            {
                if (maxDocs > 0 && Interlocked.Read(ref _totalDocumentsProcessed) >= maxDocs)
                    return;

                Exception? lastException = null;

                for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++)
                {
                    try
                    {
                        if (attempt > 0)
                        {
                            var delaySec = RetryDelaysSeconds[Math.Min(attempt - 1, RetryDelaysSeconds.Length - 1)];
                            _fileLogger.LogWarning(
                                "PreviewLoadService DOSSIER-{Type} skip={Skip}: Retry {Attempt}/{Max}, delay={Delay}s",
                                currFolderType, skipCount, attempt, MaxRetryAttempts, delaySec);
                            await Task.Delay(delaySec * 1000, token).ConfigureAwait(false);
                        }

                        var searchResult = await SearchDocumentsAsync(folderPath, skipCount, batchSize, token).ConfigureAwait(false);
                        var totalFetchedFromAlfresco = searchResult.Documents.Count;

                        searchResult.Documents.RemoveAll(o =>
                        {
                            var lastParentName = o.Entry.Path?.Elements?.LastOrDefault()?.Name;
                            return lastParentName == null || !regex.IsMatch(lastParentName);
                        });

                        var afterRegexFilter = searchResult.Documents.Count;
                        var newDossierCount = totalFetchedFromAlfresco - afterRegexFilter;

                        if (newDossierCount > 0)
                        {
                            _fileLogger.LogInformation(
                                "PreviewLoadService DOSSIER-{Type} skip={Skip}: {NewDossierCount} new-dossier docs regex-filtered",
                                currFolderType, skipCount, newDossierCount);
                        }

                        var docs = searchResult.Documents;
                        if (maxDocs > 0)
                        {
                            var remaining = maxDocs - Interlocked.Read(ref _totalDocumentsProcessed);
                            if (remaining <= 0) return;
                            if (docs.Count > remaining)
                                docs = docs.Take((int)remaining).ToList();
                        }

                        var docsToInsert = new List<PreviewDocStaging>(docs.Count);
                        var mappingFailed = 0;

                        foreach (var doc in docs)
                        {
                            try
                            {
                                var previewDoc = await ApplyPreviewDocumentMappingAsync(doc.Entry, currFolderType, token).ConfigureAwait(false);
                                docsToInsert.Add(previewDoc);
                            }
                            catch (Exception ex)
                            {
                                Interlocked.Increment(ref _totalFailed);
                                mappingFailed++;
                                _fileLogger.LogWarning(
                                    "PreviewLoadService DOSSIER-{Type} skip={Skip}: Mapping failed for {Name}: {Error}",
                                    currFolderType, skipCount, doc.Entry.Name, ex.Message);
                            }
                        }

                        _fileLogger.LogInformation(
                            "PreviewLoadService DOSSIER-{Type} skip={Skip}: fetched={F}, afterRegexFilter={R}, mappingFailed={M}, inserted={I}",
                            currFolderType, skipCount, totalFetchedFromAlfresco, afterRegexFilter, mappingFailed, docsToInsert.Count);

                        // Flush this skip's docs under the write lock, then mark as processed
                        await dbWriteLock.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            await FlushPendingBatchesAsync(docsToInsert, currFolderType, token).ConfigureAwait(false);
                            processedSkips.TryAdd(skipCount, 0);
                            failedSkips.TryRemove(skipCount, out _);
                        }
                        finally
                        {
                            dbWriteLock.Release();
                        }

                        // Periodic persist every ~10 successfully flushed batches
                        if (_batchCounter % 10 == 0)
                            await PersistCheckpointAsync(currFolderType, batchSize, token).ConfigureAwait(false);

                        lastException = null;
                        break;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (TaskCanceledException ex) when (!token.IsCancellationRequested)
                    {
                        // Timeout, not user cancel — eligible for retry
                        lastException = ex;
                        _fileLogger.LogWarning(
                            "PreviewLoadService DOSSIER-{Type} skip={Skip}: Timeout on attempt {Attempt}",
                            currFolderType, skipCount, attempt + 1);
                    }
                    catch (HttpRequestException ex)
                    {
                        lastException = ex;
                        _fileLogger.LogWarning(
                            "PreviewLoadService DOSSIER-{Type} skip={Skip}: HttpRequestException on attempt {Attempt}: {Error}",
                            currFolderType, skipCount, attempt + 1, ex.Message);
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        _fileLogger.LogWarning(
                            "PreviewLoadService DOSSIER-{Type} skip={Skip}: Error on attempt {Attempt}: {Error}",
                            currFolderType, skipCount, attempt + 1, ex.Message);
                    }
                }

                if (lastException != null)
                {
                    failedSkips.TryAdd(skipCount, 0);
                    _fileLogger.LogError(
                        "PreviewLoadService DOSSIER-{Type} skip={Skip}: All {Max} attempts failed. Batch added to FailedSkips.\n{StackTrace}",
                        currFolderType, skipCount, MaxRetryAttempts + 1, lastException.ToString());
                }
            }).ConfigureAwait(false);

            // Phase 2 — serial retry of remaining failed skips
            var stillFailed = failedSkips.Keys.OrderBy(s => s).ToList();
            if (stillFailed.Count > 0)
            {
                _fileLogger.LogWarning(
                    "PreviewLoadService DOSSIER-{Type}: Phase 2 — serial retry of {Count} failed skip(s): [{Skips}]",
                    currFolderType, stillFailed.Count, string.Join(",", stillFailed));

                foreach (var skipCount in stillFailed)
                {
                    if (ct.IsCancellationRequested) break;

                    Exception? lastException = null;

                    for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++)
                    {
                        try
                        {
                            if (attempt > 0)
                            {
                                var delaySec = RetryDelaysSeconds[Math.Min(attempt - 1, RetryDelaysSeconds.Length - 1)];
                                await Task.Delay(delaySec * 1000, ct).ConfigureAwait(false);
                            }

                            var searchResult = await SearchDocumentsAsync(folderPath, skipCount, batchSize, ct).ConfigureAwait(false);
                            var totalFetchedFromAlfresco = searchResult.Documents.Count;

                            searchResult.Documents.RemoveAll(o =>
                            {
                                var lastParentName = o.Entry.Path?.Elements?.LastOrDefault()?.Name;
                                return lastParentName == null || !regex.IsMatch(lastParentName);
                            });

                            var afterRegexFilter = searchResult.Documents.Count;
                            var docsToInsert = new List<PreviewDocStaging>(afterRegexFilter);
                            var mappingFailed = 0;

                            foreach (var doc in searchResult.Documents)
                            {
                                try
                                {
                                    var previewDoc = await ApplyPreviewDocumentMappingAsync(doc.Entry, currFolderType, ct).ConfigureAwait(false);
                                    docsToInsert.Add(previewDoc);
                                }
                                catch (Exception ex)
                                {
                                    Interlocked.Increment(ref _totalFailed);
                                    mappingFailed++;
                                    _fileLogger.LogWarning(
                                        "PreviewLoadService Phase2 DOSSIER-{Type} skip={Skip}: Mapping failed for {Name}: {Error}",
                                        currFolderType, skipCount, doc.Entry.Name, ex.Message);
                                }
                            }

                            _fileLogger.LogInformation(
                                "PreviewLoadService Phase2 DOSSIER-{Type} skip={Skip}: fetched={F}, afterRegexFilter={R}, mappingFailed={M}, inserted={I}",
                                currFolderType, skipCount, totalFetchedFromAlfresco, afterRegexFilter, mappingFailed, docsToInsert.Count);

                            await FlushPendingBatchesAsync(docsToInsert, currFolderType, ct).ConfigureAwait(false);
                            processedSkips.TryAdd(skipCount, 0);
                            failedSkips.TryRemove(skipCount, out _);
                            lastException = null;
                            break;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            _fileLogger.LogWarning(
                                "PreviewLoadService Phase2 DOSSIER-{Type} skip={Skip}: Attempt {Attempt} failed: {Error}",
                                currFolderType, skipCount, attempt + 1, ex.Message);
                        }
                    }

                    if (lastException != null)
                    {
                        _fileLogger.LogError(
                            "PreviewLoadService Phase2 DOSSIER-{Type} skip={Skip}: All retries exhausted, batch permanently failed.\n{StackTrace}",
                            currFolderType, skipCount, lastException.ToString());
                    }
                }
            }

            await PersistCheckpointAsync(currFolderType, batchSize, ct).ConfigureAwait(false);
            await LogReconciliationAsync(currFolderType, totalCount, ct).ConfigureAwait(false);

            _fileLogger.LogInformation(
                "PreviewLoadService DOSSIER-{Type}: Parallel processing done. Running total: {TotalDocs} docs",
                currFolderType, _totalDocumentsProcessed);
        }

        /// <summary>
        /// Inserts <paramref name="docs"/> into the DB under a single transaction.
        /// Does NOT update the checkpoint — that is PersistCheckpointAsync's responsibility.
        /// </summary>
        private async Task FlushPendingBatchesAsync(
            IList<PreviewDocStaging> docs,
            string folderType,
            CancellationToken ct)
        {
            if (docs.Count == 0) return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                var inserted = await repo.InsertManyMergeAsync(docs, ct).ConfigureAwait(false);
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                if (inserted > 0)
                    Interlocked.Add(ref _totalDocumentsProcessed, inserted);
                Interlocked.Increment(ref _batchCounter);

                _fileLogger.LogInformation(
                    "PreviewLoadService DOSSIER-{Type}: Flushed {DocCount} docs",
                    folderType, inserted);
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("PreviewLoadService: Failed to flush batch: {Error}", ex.Message);
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        private async Task PersistCheckpointAsync(string folderType, int batchSize, CancellationToken ct)
        {
            try
            {
                var processedSkips = _processedSkipsPerFolder.GetOrAdd(folderType, _ => new ConcurrentDictionary<int, byte>());
                var failedSkips = _failedSkipsPerFolder.GetOrAdd(folderType, _ => new ConcurrentDictionary<int, byte>());

                // High-water mark: largest consecutively-processed skip + batchSize
                var sortedProcessed = processedSkips.Keys.OrderBy(s => s).ToList();
                long hwm = 0;
                for (int i = 0; i < sortedProcessed.Count; i++)
                {
                    if (sortedProcessed[i] == i * batchSize)
                        hwm = sortedProcessed[i] + batchSize;
                    else
                        break;
                }

                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repo = scope.ServiceProvider.GetRequiredService<IPreviewLoadCheckpointRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    await repo.UpsertCheckpointStateAsync(folderType, hwm, processedSkips.Keys, failedSkips.Keys, ct).ConfigureAwait(false);
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                    _fileLogger.LogInformation(
                        "PreviewLoadService checkpoint persisted: {FolderType}, hwm={Hwm}, processed={P}, failed={F}",
                        folderType, hwm, processedSkips.Count, failedSkips.Count);
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning(
                    "PreviewLoadService: Could not persist checkpoint for {FolderType}: {Error}",
                    folderType, ex.Message);
            }
        }

        private async Task LogReconciliationAsync(string folderType, int alfrescoTotal, CancellationToken ct)
        {
            try
            {
                var processedSkips = _processedSkipsPerFolder.GetOrAdd(folderType, _ => new ConcurrentDictionary<int, byte>());
                var failedSkips = _failedSkipsPerFolder.GetOrAdd(folderType, _ => new ConcurrentDictionary<int, byte>());
                var batchSize = _options.Value.DocumentTypeDiscovery.BatchSize;

                var expectedBatches = (alfrescoTotal + batchSize - 1) / batchSize;
                var failedSkipsList = string.Join(",", failedSkips.Keys.OrderBy(s => s));

                long inDb = 0;
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();
                    await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                    inDb = await repo.GetCountByDossierTypeAsync(folderType, ct).ConfigureAwait(false);
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _fileLogger.LogWarning("LogReconciliation: Could not get DB count: {Error}", ex.Message);
                }

                _fileLogger.LogInformation(
                    "Reconciliation DOSSIER-{Type}: alfresco={Alfresco}, batches_expected={E}, " +
                    "batches_processed={P}, batches_failed={F} (skips: [{FailedSkips}]), inDb={InDb}",
                    folderType, alfrescoTotal, expectedBatches,
                    processedSkips.Count, failedSkips.Count, failedSkipsList, inDb);
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning(
                    "LogReconciliation DOSSIER-{Type}: Error: {Error}",
                    folderType, ex.Message);
            }
        }

        private async Task<PreviewDocStaging> ApplyPreviewDocumentMappingAsync(Entry alfrescoEntry, string folderType, CancellationToken ct)
        {
            var doc = new PreviewDocStaging
            {
                NodeId = alfrescoEntry.Id,
                Name = alfrescoEntry.Name,
                NodeType = alfrescoEntry.NodeType,
                Status = "PENDING",
                DossierType = folderType,
                DossierDestinationFolderIsCreated = 0,
                RecordInserted = DateTime.UtcNow
            };

            string? docDesc = null;
            string? existingDocType = null;
            string? existingStatus = null;
            string? coreIdFromDoc = null;
            string? docDossierType = null;
            string? docClientType = null;
            string? sourceFromDoc = null;
            DateTime? docCreationDate = null;
            string? contractNumber = null;
            string? productType = null;
            string? accountNumbers = null;
            string? jsonProperties = null;

            if (alfrescoEntry.Properties != null)
            {
                string GetStr(string key) => alfrescoEntry!.Properties!.TryGetValue(key, out var value) ? value?.ToString() ?? "" : "";

                docDesc = GetStr("ecm:docDesc");
                existingDocType = GetStr("ecm:docType");
                existingStatus = GetStr("ecm:docStatus");
                coreIdFromDoc = GetStr("ecm:coreId");
                docDossierType = GetStr("ecm:docDossierType");
                docClientType = GetStr("ecm:docClientType");
                sourceFromDoc = GetStr("ecm:source");
                contractNumber = GetStr("ecm:bnkNumberOfContract");
                productType = GetStr("ecm:bnkTypeOfProduct");
                accountNumbers = GetStr("ecm:bnkAccountNumber");
                jsonProperties = JsonSerializer.Serialize(alfrescoEntry.Properties);

                if (alfrescoEntry.Properties.TryGetValue("ecm:datumKreiranja", out var v11))
                {
                    if (v11 is DateTime dt) docCreationDate = dt;
                    else if (DateTime.TryParse(v11?.ToString(), out var parsed)) docCreationDate = parsed;
                }
            }

            var parentFolderName = alfrescoEntry.Path?.Elements?.LastOrDefault()?.Name;
            var parentFolderId = alfrescoEntry.Path?.Elements?.LastOrDefault()?.Id ?? alfrescoEntry.ParentId;

            doc.Properties = jsonProperties;
            doc.ParentFolderName = parentFolderName;
            doc.ParentId = parentFolderId;
            doc.DocDescription = docDesc;
            doc.OriginalDocumentCode = existingDocType;
            doc.OldAlfrescoStatus = existingStatus;
            doc.ContractNumber = contractNumber;
            doc.AccountNumbers = accountNumbers;
            doc.OriginalCreatedAt = docCreationDate ?? alfrescoEntry.CreatedAt.DateTime;
            doc.OriginalDocumentName = alfrescoEntry.Name;
            doc.ClientSegment = docClientType;

            doc.CoreId = coreIdFromDoc;
            if (string.IsNullOrWhiteSpace(doc.CoreId) && !string.IsNullOrWhiteSpace(parentFolderName))
                doc.CoreId = DossierIdFormatter.ExtractCoreId(parentFolderName);

            DocumentMapping? fullMapping = null;
            if (!string.IsNullOrWhiteSpace(docDesc))
                fullMapping = await _opisToTipMapper.GetFullMappingAsync(docDesc, existingDocType, ct).ConfigureAwait(false);

            var tipDosijea = fullMapping?.TipDosijea ?? docDossierType ?? "";
            doc.DocumentType = fullMapping?.SifraDokumentaMigracija ?? existingDocType;
            doc.DocumentTypeMigration = fullMapping?.SifraDokumentaMigracija;
            doc.NewDocumentName = fullMapping?.NazivDokumentaMigracija ?? string.Empty;
            doc.ProductType = fullMapping?.TipProizvoda ?? productType;
            doc.CategoryCode = fullMapping?.OznakaKategorije;
            doc.CategoryName = fullMapping?.NazivKategorije;

            var statusInfo = DocumentStatusDetectorV3.DetermineStatus(fullMapping, existingStatus);
            doc.IsActive = statusInfo.IsActive ? 1 : 0;
            doc.NewAlfrescoStatus = statusInfo.Status;
            doc.NewDocumentCode = statusInfo.MappingCode;
            if (string.IsNullOrWhiteSpace(doc.OriginalDocumentCode))
                doc.OriginalDocumentCode = statusInfo.OriginalCode;

            var destinationType = DestinationRootFolderDeterminator.DetermineAndResolve(
                doc.DocumentType,
                tipDosijea,
                doc.ClientSegment);

            if (destinationType == DossierType.Unknown && !string.IsNullOrWhiteSpace(parentFolderName))
            {
                var prefix = DossierIdFormatter.ExtractPrefix(parentFolderName);
                destinationType = prefix.ToUpperInvariant() switch
                {
                    "PI" => DossierType.ClientFL,
                    "FL" => DossierType.ClientFL,
                    "LE" => DossierType.ClientPL,
                    "PL" => DossierType.ClientPL,
                    _ => DossierType.Unknown
                };
            }

            doc.TargetDossierType = ((int)destinationType).ToString();
            doc.Source = sourceFromDoc ?? SourceDetector.GetSource(destinationType);

            if (!string.IsNullOrWhiteSpace(parentFolderName))
            {
                string? productTypeToUse = doc.ProductType;
                if (destinationType == DossierType.Deposit)
                    productTypeToUse = DossierIdFormatter.MapClientSegmentToProductType(doc.ClientSegment);

                doc.DossierDestinationFolderName = DossierIdFormatter.ConvertForTargetType(
                    parentFolderName,
                    (int)destinationType,
                    contractNumber,
                    productTypeToUse,
                    doc.CoreId,
                    doc.OriginalCreatedAt);
            }

            return doc;
        }

        private string BuildPreviewSearchQuery(string ancestorId)
        {
            var query = $"ANCESTOR:\"{ancestorId}\" AND TYPE:\"cm:content\"";

            if (_options.Value.DocumentTypeDiscovery.UseDateFilter)
            {
                var dateFrom = _options.Value.DocumentTypeDiscovery.DateFrom;
                var dateTo = _options.Value.DocumentTypeDiscovery.DateTo;

                if (!string.IsNullOrWhiteSpace(dateFrom) && !string.IsNullOrWhiteSpace(dateTo))
                {
                    if (DateTime.TryParse(dateFrom, out var fromDate) && DateTime.TryParse(dateTo, out var toDate))
                        query += $" AND cm\\:created:[{fromDate:yyyy-MM-dd} TO {toDate:yyyy-MM-dd}]";
                }
            }

            return query;
        }

        private async Task<int> GetTotalDocumentCountAsync(string query, CancellationToken ct)
        {
            var request = new PostSearchRequest
            {
                Query = new QueryRequest { Query = query, Language = "afts" },
                Paging = new PagingRequest { MaxItems = 1, SkipCount = 0 }
            };
            var response = await _alfrescoReadApi.SearchAsync(request, ct).ConfigureAwait(false);
            return response.List.Pagination.TotalItems;
        }

        private async Task<(List<ListEntry> Documents, bool HasMore)> SearchDocumentsAsync(
            string ancestorId, int skipCount, int maxItems, CancellationToken ct)
        {
            var query = BuildPreviewSearchQuery(ancestorId);

            _fileLogger.LogInformation("PreviewLoadService AFTS Query: {Query}, Skip: {Skip}, Max: {Max}", query, skipCount, maxItems);

            var req = new PostSearchRequest
            {
                Query = new QueryRequest { Language = "afts", Query = query },
                Paging = new PagingRequest { MaxItems = maxItems, SkipCount = skipCount },
                Sort = new List<SortRequest>
                {
                    new SortRequest { Type = "FIELD", Field = "created", Ascending = true },
                    new SortRequest { Type = "FIELD", Field = "name", Ascending = true },
                    // Third sort for stable skip-pagination; if Alfresco rejects this field,
                    // try "cmis:objectId" or "id" as alternatives.
                    new SortRequest { Type = "FIELD", Field = "sys:node-uuid", Ascending = true }
                },
                Include = new[] { "properties", "path" }
            };

            var response = await _alfrescoReadApi.SearchAsync(req, ct).ConfigureAwait(false);
            var documents = response?.List?.Entries ?? new List<ListEntry>();
            var hasMore = response?.List?.Pagination?.HasMoreItems ?? false;

            return (documents, hasMore);
        }

        private Dictionary<string, string> GetSubDossiersFolders()
        {
            var result = new Dictionary<string, string>();

            if (!string.IsNullOrWhiteSpace(_options.Value.RootPIFolderId))
                result["PI"] = _options.Value.RootPIFolderId!;

            if (!string.IsNullOrWhiteSpace(_options.Value.RootLEFolderId))
                result["LE"] = _options.Value.RootLEFolderId!;

            return result;
        }

        private async Task LoadCheckpointAsync(CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IPreviewLoadCheckpointRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            try
            {
                await uow.BeginAsync(ct: ct).ConfigureAwait(false);

                foreach (var folderType in new[] { "PI", "LE" })
                {
                    var state = await repo.GetCheckpointStateAsync(folderType, ct).ConfigureAwait(false);

                    var processedSet = _processedSkipsPerFolder.GetOrAdd(folderType, _ => new ConcurrentDictionary<int, byte>());
                    var failedSet = _failedSkipsPerFolder.GetOrAdd(folderType, _ => new ConcurrentDictionary<int, byte>());

                    foreach (var s in state.ProcessedSkips)
                        processedSet.TryAdd(s, 0);

                    foreach (var s in state.FailedSkips)
                        failedSet.TryAdd(s, 0);

                    _fileLogger.LogInformation(
                        "PreviewLoadService checkpoint loaded: {FolderType} — hwm={Hwm}, processed={P}, failed={F}",
                        folderType, state.FetchedCount, state.ProcessedSkips.Count, state.FailedSkips.Count);
                }

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                _fileLogger.LogWarning("PreviewLoadService: Could not load checkpoint, starting fresh. Error: {Error}", ex.Message);
            }
        }

        #region Not Implemented
        public Task<DocumentSearchBatchResult> RunBatchAsync(CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task<bool> RunLoopAsync(CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
