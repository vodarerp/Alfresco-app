using Alfresco.Abstraction.Models;
using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Migration.Infrastructure.Implementation.Move;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Abstraction.Models;
using Migration.Extensions.SqlServer;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    public class MoveService : IMoveService
    {
        private readonly IMoveExecutor _moveExecutor;
        private readonly IOptions<MigrationOptions> _options;
        private readonly IServiceScopeFactory _scopeFactory;

        private readonly ILogger _dbLogger;
        private readonly ILogger _fileLogger;
        private readonly ILogger _uiLogger;

        private long _totalMoved = 0;
        private long _totalFailed = 0;
        private int _batchCounter = 0;

        private const string ServiceName = "Move";

        public MoveService(IMoveExecutor moveExecutor,
                           IOptions<MigrationOptions> options,
                           IServiceScopeFactory scopeFactory,
                           ILoggerFactory logger)
        {
            _moveExecutor = moveExecutor;
            _options = options;
            _scopeFactory = scopeFactory;
            _dbLogger = logger.CreateLogger("DbLogger");
            _fileLogger = logger.CreateLogger("FileLogger");
            _uiLogger = logger.CreateLogger("UiLogger");
        }

        public async Task<MoveBatchResult> RunBatchAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var acquireTimer = Stopwatch.StartNew();

            _fileLogger.LogInformation("Move batch started - BatchSize: {BatchSize}, DOP: {DOP}",
                _options.Value.MoveService.BatchSize ?? _options.Value.BatchSize,
                _options.Value.MoveService.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism);
            _dbLogger.LogInformation("Move batch started");
            _uiLogger.LogInformation("Processing documents batch...");

            var batch = _options.Value.MoveService.BatchSize ?? _options.Value.BatchSize;
            var dop = _options.Value.MoveService.MaxDegreeOfParallelism ?? _options.Value.MaxDegreeOfParallelism;

            // 1. Atomic acquire - preuzmi i zaključaj dokumente u jednoj transakciji
            _fileLogger.LogDebug("Acquiring documents for move - Batch size: {BatchSize}", batch);
            var documents = await AcquireDocumentsForMoveAsync(batch, ct).ConfigureAwait(false);
            acquireTimer.Stop();
            _fileLogger.LogInformation("Acquired {Count} documents in {Ms}ms", documents.Count, acquireTimer.ElapsedMilliseconds);
            _dbLogger.LogInformation("Acquired {Count} documents in {Ms}ms", documents.Count, acquireTimer.ElapsedMilliseconds);

            if (documents.Count == 0)
            {
                _fileLogger.LogDebug("No documents ready for move - batch is empty");
                return new MoveBatchResult(0, 0);
            }

            // 2. Parallel move dokumenata
            _fileLogger.LogInformation("Starting parallel move with DOP={Dop}", dop);
            var moveTimer = Stopwatch.StartNew();
            var doneCount = 0;
            var errors = new ConcurrentBag<(long DocId, Exception Error)>();
            var successfulDocs = new ConcurrentBag<long>();

            await Parallel.ForEachAsync(
                documents,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = dop,
                    CancellationToken = ct
                },
                async (doc, token) =>
                {
                    try
                    {
                        _fileLogger.LogDebug("Processing document {DocId} (NodeId: {NodeId})", doc.Id, doc.NodeId);

                        // Pass entire DocStaging object to MoveSingleDocumentAsync
                        var res = await MoveSingleDocumentAsync(doc, token).ConfigureAwait(false);

                        if (res)
                        {
                            successfulDocs.Add(doc.Id);
                            Interlocked.Increment(ref doneCount);
                            Interlocked.Increment(ref _totalMoved);
                            _fileLogger.LogDebug("Successfully migrated document {DocId}", doc.Id);
                        }
                        else
                        {
                            _fileLogger.LogWarning("Migration returned false for document {DocId} ({NodeId})", doc.Id, doc.NodeId);
                            _dbLogger.LogWarning("Migration returned false for document {DocId} ({NodeId})", doc.Id, doc.NodeId);
                            _uiLogger.LogWarning("Document {DocId} migration failed", doc.Id);
                            errors.Add((doc.Id, new InvalidOperationException("Migration returned false")));
                        }
                    }
                    catch (Exception ex)
                    {
                        _fileLogger.LogError("Failed to migrate document {DocId} - Error: {Error}", doc.Id, ex.Message);
                        _dbLogger.LogError(ex,
                            "Failed to migrate document {DocId} ({NodeId})",
                            doc.Id, doc.NodeId);
                        _uiLogger.LogError("Document {DocId} error: {Error}", doc.Id, ex.Message);
                        errors.Add((doc.Id, ex));
                    }
                });

            moveTimer.Stop();
            var avgMoveTime = documents.Count > 0 ? moveTimer.ElapsedMilliseconds / (double)documents.Count : 0;
            _fileLogger.LogInformation(
                "Parallel move completed: {Done} succeeded, {Failed} failed in {Ms}ms (avg {Avg:F1}ms/doc)",
                doneCount, errors.Count, moveTimer.ElapsedMilliseconds, avgMoveTime);

            // 3. Batch update za uspešne - sve u jednoj transakciji
            // Skip DB commit if cancellation requested to reduce freeze time
            var updateTimer = Stopwatch.StartNew();
            if (!ct.IsCancellationRequested && !successfulDocs.IsEmpty)
            {
                _fileLogger.LogDebug("Marking {Count} documents as DONE", successfulDocs.Count);
                await MarkDocumentsAsDoneAsync(successfulDocs, ct).ConfigureAwait(false);
            }
            // 4. Batch update za greške - sve u jednoj transakciji
            if (!ct.IsCancellationRequested && !errors.IsEmpty)
            {
                _fileLogger.LogWarning("Marking {Count} documents as FAILED", errors.Count);
                await MarkDocumentsAsFailedAsync(errors, ct).ConfigureAwait(false);
                Interlocked.Add(ref _totalFailed, errors.Count);
            }
            updateTimer.Stop();

            // Save checkpoint after successful batch (skip on cancellation)
            if (!ct.IsCancellationRequested)
            {
                Interlocked.Increment(ref _batchCounter);
                await SaveCheckpointAsync(ct).ConfigureAwait(false);
            }

            sw.Stop();
            _fileLogger.LogInformation(
                "Move batch TOTAL: acquire={AcqMs}ms, move={MoveMs}ms, update={UpdMs}ms, total={TotalMs}ms | " +
                "Success={Done}, Failed={Failed} | Overall: {TotalMoved} moved, {TotalFailed} failed",
                acquireTimer.ElapsedMilliseconds, moveTimer.ElapsedMilliseconds, updateTimer.ElapsedMilliseconds, sw.ElapsedMilliseconds,
                doneCount, errors.Count, _totalMoved, _totalFailed);

            return new MoveBatchResult(doneCount, errors.Count);
        }

        public async Task<bool> RunLoopAsync(CancellationToken ct)
        {
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;
            var completedSuccessfully = false;

            _fileLogger.LogInformation("Move service started - IdleDelay: {IdleDelay}ms, MaxEmptyResults: {MaxEmptyResults}",
                delay, maxEmptyResults);
            _dbLogger.LogInformation("Move service started");
            _uiLogger.LogInformation("Move service started");

            // Reset stuck documents from previous crashed run
            _fileLogger.LogInformation("Resetting stuck documents...");
            await ResetStuckItemsAsync(ct).ConfigureAwait(false);

            // Load checkpoint to resume from last position
            await LoadCheckpointAsync(ct).ConfigureAwait(false);

            // Start from next batch after checkpoint
            var batchCounter = _batchCounter + 1;

            while (!ct.IsCancellationRequested)
            {
                using var batchScope = _fileLogger.BeginScope(new Dictionary<string, object>
                {
                    ["BatchCounter"] = batchCounter
                });

                try
                {
                    _fileLogger.LogDebug("Starting batch {BatchCounter}", batchCounter);

                    var result = await RunBatchAsync(ct).ConfigureAwait(false);

                    if (result.Done == 0 && result.Failed == 0)
                    {
                        emptyResultCounter++;
                        _fileLogger.LogDebug(
                            "Empty result ({Counter}/{Max})",
                            emptyResultCounter, maxEmptyResults);

                        if (emptyResultCounter >= maxEmptyResults)
                        {
                            _fileLogger.LogInformation(
                                "Breaking after {Count} consecutive empty results",
                                emptyResultCounter);
                            completedSuccessfully = true;
                            break;
                        }

                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        emptyResultCounter = 0; // Reset counter on success

                        var betweenDelay = _options.Value.MoveService.DelayBetweenBatchesInMs
                            ?? _options.Value.DelayBetweenBatchesInMs;

                        if (betweenDelay > 0)
                        {
                            await Task.Delay(betweenDelay, ct).ConfigureAwait(false);
                        }
                    }

                    batchCounter++;
                }
                catch (OperationCanceledException)
                {
                    _fileLogger.LogInformation("Move service cancelled by user");
                    _dbLogger.LogInformation("Move service cancelled");
                    _uiLogger.LogInformation("Move cancelled");
                    throw;
                }
                catch (AlfrescoTimeoutException timeoutEx)
                {
                    _fileLogger.LogError("Move service stopped - Alfresco Timeout: {Message}", timeoutEx.Message);
                    _dbLogger.LogError(timeoutEx, "Move service stopped - Timeout");
                    _uiLogger.LogError("Move service stopped - Timeout: {Operation}", timeoutEx.Operation);
                    throw; // Re-throw to stop migration
                }
                catch (AlfrescoRetryExhaustedException retryEx)
                {
                    _fileLogger.LogError("Move service stopped - Alfresco Retry Exhausted: {Message}", retryEx.Message);
                    _dbLogger.LogError(retryEx, "Move service stopped - Retry Exhausted");
                    _uiLogger.LogError("Move service stopped - Retry Exhausted: {Operation}", retryEx.Operation);
                    throw; // Re-throw to stop migration
                }
                catch (AlfrescoException alfrescoEx)
                {
                    _fileLogger.LogError("Move service stopped - Alfresco Error: {Message}", alfrescoEx.Message);
                    _dbLogger.LogError(alfrescoEx, "Move service stopped - Alfresco Error");
                    _uiLogger.LogError("Move service stopped - Alfresco Error (Status: {StatusCode})", alfrescoEx.StatusCode);
                    throw; // Re-throw to stop migration
                }
                catch (Exception ex)
                {
                    _fileLogger.LogError("Critical error in batch {BatchCounter}: {Error}", batchCounter, ex.Message);
                    _dbLogger.LogError(ex, "Error in batch {BatchCounter}", batchCounter);
                    _uiLogger.LogError("Error in batch {BatchCounter}", batchCounter);

                    // Exponential backoff on error
                    await Task.Delay(delay * 2, ct).ConfigureAwait(false);
                    batchCounter++;
                }
            }

            _fileLogger.LogInformation(
                "Move service completed after {Count} batches. Total: {Moved} moved, {Failed} failed",
                batchCounter - 1, _totalMoved, _totalFailed);
            _dbLogger.LogInformation(
                "Move service completed - Total: {Moved} moved, {Failed} failed",
                _totalMoved, _totalFailed);
            _uiLogger.LogInformation("Move completed: {Moved} moved", _totalMoved);

            return completedSuccessfully;
        }

        #region privates

        private async Task ResetStuckItemsAsync(CancellationToken ct)
        {
            try
            {
                var timeout = TimeSpan.FromMinutes(_options.Value.StuckItemsTimeoutMinutes);
                _fileLogger.LogDebug("Checking for stuck documents with timeout: {Minutes} minutes", _options.Value.StuckItemsTimeoutMinutes);

                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

                var isPreview = _options.Value.PreviewTypeMigration;
                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var resetCount = await docRepo.ResetMoveStuckDocumentsAsync(
                        uow.Connection,
                        uow.Transaction,
                        timeout,
                        isPreview,
                        ct).ConfigureAwait(false);

                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    if (resetCount > 0)
                    {
                        _fileLogger.LogWarning(
                            "Reset {Count} stuck documents that were IN PROGRESS for more than {Minutes} minutes",
                            resetCount, _options.Value.StuckItemsTimeoutMinutes);
                        _dbLogger.LogWarning(
                            "Reset {Count} stuck documents (timeout: {Minutes} minutes)",
                            resetCount, _options.Value.StuckItemsTimeoutMinutes);
                        _uiLogger.LogWarning("Reset {Count} stuck documents", resetCount);
                    }
                    else
                    {
                        _fileLogger.LogInformation("No stuck documents found");
                    }
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning("Failed to reset stuck documents: {Error}", ex.Message);
                _dbLogger.LogError(ex, "Failed to reset stuck documents");
                _uiLogger.LogWarning("Could not reset stuck items");
            }
        }

        private async Task LoadCheckpointAsync(CancellationToken ct)
        {
            try
            {
                _fileLogger.LogDebug("Loading checkpoint for Move service...");
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var checkpointRepo = scope.ServiceProvider.GetRequiredService<IMigrationCheckpointRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var checkpoint = await checkpointRepo.GetByServiceNameAsync(ServiceName, ct).ConfigureAwait(false);
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    if (checkpoint != null)
                    {
                        _totalMoved = checkpoint.TotalProcessed;
                        _totalFailed = checkpoint.TotalFailed;
                        _batchCounter = checkpoint.BatchCounter;

                        _fileLogger.LogInformation(
                            "Checkpoint loaded: {TotalMoved} moved, {TotalFailed} failed, batch {BatchCounter}",
                            _totalMoved, _totalFailed, _batchCounter);
                        _dbLogger.LogInformation(
                            "Checkpoint loaded: {TotalMoved} moved, {TotalFailed} failed, batch {BatchCounter}",
                            _totalMoved, _totalFailed, _batchCounter);
                    }
                    else
                    {
                        _fileLogger.LogInformation("No checkpoint found, starting fresh");
                        _totalMoved = 0;
                        _totalFailed = 0;
                        _batchCounter = 0;
                    }
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning("Failed to load checkpoint, starting fresh: {Error}", ex.Message);
                _dbLogger.LogError(ex, "Failed to load checkpoint, starting fresh");
                _uiLogger.LogInformation("Starting fresh migration");
                _totalMoved = 0;
                _totalFailed = 0;
                _batchCounter = 0;
            }
        }

        private async Task SaveCheckpointAsync(CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var checkpointRepo = scope.ServiceProvider.GetRequiredService<IMigrationCheckpointRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var checkpoint = new MigrationCheckpoint
                    {
                        ServiceName = ServiceName,
                        TotalProcessed = _totalMoved,
                        TotalFailed = _totalFailed,
                        BatchCounter = _batchCounter
                    };

                    await checkpointRepo.UpsertAsync(checkpoint, ct).ConfigureAwait(false);
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    _fileLogger.LogDebug("Checkpoint saved: {TotalMoved} moved, {TotalFailed} failed, batch {BatchCounter}",
                        _totalMoved, _totalFailed, _batchCounter);
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning("[{Method}] Failed to save checkpoint: {ErrorType} - {Message}",
                    nameof(SaveCheckpointAsync), ex.GetType().Name, ex.Message);
                _dbLogger.LogWarning(ex, "[{Method}] Failed to save checkpoint",
                    nameof(SaveCheckpointAsync));
            }
        }

        private async Task<IReadOnlyList<DocStaging>> AcquireDocumentsForMoveAsync(int batch, CancellationToken ct)
        {
            _fileLogger.LogDebug("Acquiring {BatchSize} documents for processing", batch);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                var documents = await docRepo.TakeReadyForProcessingAsync(batch, ct).ConfigureAwait(false);
                _fileLogger.LogDebug("Retrieved {Count} documents from database", documents.Count);
                
                //var updates = documents.Select(d => (
                //    d.Id,
                //    MigrationStatus.InProgress.ToDbString(),
                //    (string?)null
                //));

                //await docRepo.BatchSetDocumentStatusAsync_v1(
                //    uow.Connection,
                //    uow.Transaction,
                //    updates,
                //    ct).ConfigureAwait(false);

                await uow.CommitAsync().ConfigureAwait(false);
                _fileLogger.LogDebug("Marked {Count} documents as IN PROGRESS", documents.Count);

                return documents;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("Failed to acquire documents: {Error}", ex.Message);
                _dbLogger.LogError(ex, "Failed to acquire documents for move");
                _uiLogger.LogError("Database error acquiring documents");
                await uow.RollbackAsync().ConfigureAwait(false);
                throw;
            }

        }
       
        private async Task<bool> MoveSingleDocumentAsync(DocStaging doc, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(doc.DestinationFolderId))
            {
                _uiLogger.LogError("Missing destination folder for document {DocId}", doc.Id);
                throw new InvalidOperationException(
                    $"DestinationFolderId is NULL for document {doc.Id} (NodeId: {doc.NodeId}). " +
                    $"FolderPreparationService (FAZA 3) must run first and populate this field!");
            }

            var useCopy   = _options.Value.MoveService.UseCopy;
            var properties = DocStagingPropertyMapper.BuildMigrationProperties(doc);

            if (!properties.ContainsKey("ecm:docTypeName"))
                _fileLogger.LogError(
                    "ecm:docTypeName cannot be set for document {DocId} — NewDocumentName and DocDescription are both empty.",
                    doc.Id);

            _fileLogger.LogDebug("{Op} document {DocId} (NodeId: {NodeId}) to folder {FolderId} with {PropCount} properties.",
                useCopy ? "Copying" : "Moving", doc.Id, doc.NodeId, doc.DestinationFolderId, properties.Count);

            await _moveExecutor.MoveWithPropertiesAsync(
                doc.NodeId, doc.DestinationFolderId, useCopy, properties, ct).ConfigureAwait(false);

            _fileLogger.LogInformation("Document {DocId} successfully {Op} and properties updated.",
                doc.Id, useCopy ? "copied" : "moved");

            return true;
        }

        private async Task MarkDocumentsAsFailedAsync(
            ConcurrentBag<(long DocId, Exception Error)> errors,
            CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                // Koristi batch extension method umesto pojedinačnih update-a
                var updates = errors.Select(e => (
                    e.DocId,
                    MigrationStatus.Error.ToDbString(),
                    e.Error.Message.Length > 4000
                        ? e.Error.Message[..4000]
                        : e.Error.Message
                ));

                await docRepo.BatchSetDocumentStatusAsync_v1(
                    uow.Connection,
                    uow.Transaction,
                    updates,
                    ct).ConfigureAwait(false);

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                _fileLogger.LogWarning("Marked {Count} documents as failed", errors.Count);
            }
            catch (Exception ex)
            {
                _dbLogger.LogError(ex, "Failed to mark documents as failed");
                _uiLogger.LogWarning("Could not update failed documents status");
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
            }
        }

        private async Task MarkDocumentsAsDoneAsync(
            ConcurrentBag<long> docsIds,
            CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                // Koristi batch extension method umesto pojedinačnih update-a
                var updates = docsIds.Select(id => (
                    id,
                    MigrationStatus.Done.ToDbString(),
                    (string?)null
                ));

                await docRepo.BatchSetDocumentStatusAsync_v1(
                    uow.Connection,
                    uow.Transaction,
                    updates,
                    ct).ConfigureAwait(false);

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                _fileLogger.LogWarning("Marked {Count} documents as succesed", docsIds.Count);
            }
            catch (Exception ex)
            {
                _dbLogger.LogError(ex, "Failed to mark documents as succesed");
                _uiLogger.LogWarning("Could not update successful documents status");
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
            }
        }

        public async Task<bool> RunLoopAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback)
        {
            var emptyResultCounter = 0;
            var delay = _options.Value.IdleDelayInMs;
            var maxEmptyResults = _options.Value.BreakEmptyResults;
            var batchSize = _options.Value.MoveService.BatchSize ?? _options.Value.BatchSize;
            var completedSuccessfully = false;

            _fileLogger.LogInformation("Move worker started");

            // Reset stuck documents from previous crashed run
            await ResetStuckItemsAsync(ct).ConfigureAwait(false);

            // Load checkpoint to resume from last position
            await LoadCheckpointAsync(ct).ConfigureAwait(false);

            // Start from next batch after checkpoint
            var batchCounter = _batchCounter + 1;

            // Try to get total count of documents to move
            long totalCount = 0;
           

            // Initial progress report
            var progress = new WorkerProgress
            {
                TotalItems = totalCount, // Will be 0 if count failed
                ProcessedItems = _totalMoved,
                BatchSize = batchSize,
                CurrentBatch = 0,
                Message = totalCount > 0
                    ? $"Starting move operation... (Total documents: {totalCount})"
                    : "Starting move operation..."
            };
            progressCallback?.Invoke(progress);

            while (!ct.IsCancellationRequested)
            {
                using var batchScope = _fileLogger.BeginScope(new Dictionary<string, object>
                {
                    ["BatchCounter"] = batchCounter
                });

                try
                {
                    _fileLogger.LogDebug("Starting batch {BatchCounter}", batchCounter);

                    var result = await RunBatchAsync(ct).ConfigureAwait(false);

                    // Update progress after each batch
                    progress.ProcessedItems = _totalMoved;
                    progress.CurrentBatch = batchCounter;
                    progress.CurrentBatchCount = result.Done + result.Failed;
                    progress.SuccessCount = result.Done;
                    progress.FailedCount = result.Failed;
                    progress.Timestamp = DateTimeOffset.UtcNow;
                    progress.Message = (result.Done + result.Failed) > 0
                        ? $"Moved {result.Done} documents in batch {batchCounter} ({result.Failed} failed)"
                        : "No more documents to move";

                    progressCallback?.Invoke(progress);

                    if (result.Done == 0 && result.Failed == 0)
                    {
                        emptyResultCounter++;
                        _fileLogger.LogDebug(
                            "Empty result ({Counter}/{Max})",
                            emptyResultCounter, maxEmptyResults);

                        if (emptyResultCounter >= maxEmptyResults)
                        {
                            _fileLogger.LogInformation(
                                "Breaking after {Count} consecutive empty results",
                                emptyResultCounter);

                            progress.Message = $"Completed: {_totalMoved} documents moved, {_totalFailed} failed";
                            progressCallback?.Invoke(progress);
                            completedSuccessfully = true;
                            break;
                        }

                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        emptyResultCounter = 0; // Reset counter on success

                        var betweenDelay = _options.Value.MoveService.DelayBetweenBatchesInMs
                            ?? _options.Value.DelayBetweenBatchesInMs;

                        if (betweenDelay > 0)
                        {
                            await Task.Delay(betweenDelay, ct).ConfigureAwait(false);
                        }
                    }

                    batchCounter++;
                }
                catch (OperationCanceledException)
                {
                    _fileLogger.LogInformation("Move worker cancelled");
                    progress.Message = $"Cancelled after moving {_totalMoved} documents ({_totalFailed} failed)";
                    progressCallback?.Invoke(progress);
                    throw;
                }
                catch (AlfrescoTimeoutException timeoutEx)
                {
                    _fileLogger.LogError("Move worker stopped - Alfresco Timeout: {Message}", timeoutEx.Message);
                    _dbLogger.LogError(timeoutEx, "Move worker stopped - Timeout");
                    _uiLogger.LogError("Move worker stopped - Timeout: {Operation}", timeoutEx.Operation);
                    throw; // Re-throw to stop migration
                }
                catch (AlfrescoRetryExhaustedException retryEx)
                {
                    _fileLogger.LogError("Move worker stopped - Alfresco Retry Exhausted: {Message}", retryEx.Message);
                    _dbLogger.LogError(retryEx, "Move worker stopped - Retry Exhausted");
                    _uiLogger.LogError("Move worker stopped - Retry Exhausted: {Operation}", retryEx.Operation);
                    throw; // Re-throw to stop migration
                }
                catch (AlfrescoException alfrescoEx)
                {
                    _fileLogger.LogError("Move worker stopped - Alfresco Error: {Message}", alfrescoEx.Message);
                    _dbLogger.LogError(alfrescoEx, "Move worker stopped - Alfresco Error");
                    _uiLogger.LogError("Move worker stopped - Alfresco Error (Status: {StatusCode})", alfrescoEx.StatusCode);
                    throw; // Re-throw to stop migration
                }
                catch (Exception ex)
                {
                    _dbLogger.LogError(ex, "Error in batch {BatchCounter}", batchCounter);
                    _uiLogger.LogError("Batch error: {Error}", ex.Message);

                    progress.Message = $"Error in batch {batchCounter}: {ex.Message}";
                    progressCallback?.Invoke(progress);

                    // Exponential backoff on error
                    await Task.Delay(delay * 2, ct).ConfigureAwait(false);
                    batchCounter++;
                }
            }

            _fileLogger.LogInformation(
                "Move worker completed after {Count} batches. Total: {Moved} moved, {Failed} failed",
                batchCounter - 1, _totalMoved, _totalFailed);

            return completedSuccessfully;
        }

        

        #endregion


    }
}
