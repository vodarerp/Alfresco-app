using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Abstraction.Models;
//using Migration.Extensions.Oracle;
using Migration.Extensions.SqlServer;
//using Oracle.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    public class MoveService : IMoveService
    {
        private readonly IMoveReader _moveReader;
        private readonly IMoveExecutor _moveExecutor;
        private readonly IDocStagingRepository _docRepo;
        private readonly IDocumentResolver _resolver;
        private readonly IAlfrescoWriteApi _write;
        private readonly IAlfrescoReadApi _read;
        private readonly IOptions<MigrationOptions> _options;
        private readonly IServiceScopeFactory _scopeFactory;
        //private readonly ILogger<MoveService> _fileLogger;

        private readonly ILogger _dbLogger;
        private readonly ILogger _fileLogger;

        private long _totalMoved = 0;
        private long _totalFailed = 0;
        private int _batchCounter = 0;

        private const string ServiceName = "Move";

        private readonly ILogger _uiLogger;

        // NOTE: Removed _folderCache and _folderLocks after refactoring
        // Folder caching is now handled by DocumentResolver (with lock striping)
        // All folders are pre-created by FolderPreparationService in FAZA 3

        public MoveService(IMoveReader moveService, 
                           IMoveExecutor moveExecutor, 
                           IDocStagingRepository docRepo,
                           IDocumentResolver resolver,
                           IAlfrescoWriteApi write, 
                           IAlfrescoReadApi read, 
                           IOptions<MigrationOptions> options,
                           IServiceScopeFactory scopeFactory, 
                           ILoggerFactory logger)
        {
            _moveReader = moveService;
            _moveExecutor = moveExecutor;
            _docRepo = docRepo;
            _resolver = resolver;
            _write = write;
            _read = read;
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
                            errors.Add((doc.Id, new InvalidOperationException("Migration returned false")));
                        }
                    }
                    catch (Exception ex)
                    {
                        _fileLogger.LogError("Failed to migrate document {DocId} - Error: {Error}", doc.Id, ex.Message);
                        _dbLogger.LogError(ex,
                            "Failed to migrate document {DocId} ({NodeId})",
                            doc.Id, doc.NodeId);
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

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var resetCount = await docRepo.ResetStuckDocumentsAsync(
                        uow.Connection,
                        uow.Transaction,
                        timeout,
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
                _fileLogger.LogWarning(ex, "Failed to save checkpoint");
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
                
                var updates = documents.Select(d => (
                    d.Id,
                    MigrationStatus.InProgress.ToDbString(),
                    (string?)null
                ));

                await docRepo.BatchSetDocumentStatusAsync_v1(
                    uow.Connection,
                    uow.Transaction,
                    updates,
                    ct).ConfigureAwait(false);

                await uow.CommitAsync().ConfigureAwait(false);
                _fileLogger.LogDebug("Marked {Count} documents as IN PROGRESS", documents.Count);

                return documents;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("Failed to acquire documents: {Error}", ex.Message);
                _dbLogger.LogError(ex, "Failed to acquire documents for move");
                await uow.RollbackAsync().ConfigureAwait(false);
                throw;
            }

        }

        /// <summary>
        /// Migrates single document from old to new Alfresco
        ///
        /// Process:
        /// 1. Create/Get destination folder (with caching)
        /// 2. Move document to destination folder
        /// 3. Lookup DocumentMapping to get migrated document type and name
        /// 4. Update document properties in new Alfresco
        ///
        /// Per Analiza_migracije_v2.md: All metadata prepared by DocumentDiscoveryService
        /// </summary>
        private async Task<bool> MoveSingleDocumentAsync(DocStaging doc, CancellationToken ct)
        {
            using var scope = _fileLogger.BeginScope(new Dictionary<string, object>
            {
                ["DocumentId"] = doc.Id,
                ["NodeId"] = doc.NodeId,
                ["DossierDestId"] = doc.DossierDestFolderId ?? "null",
                ["TargetDossierType"] = doc.TargetDossierType?.ToString() ?? "null"
            });

            try
            {
                // ========================================
                // STEP 1: Create/Get destination folder
                // ========================================
                _fileLogger.LogDebug("Creating destination folder for document {DocId}", doc.Id);
                var destFolderId = await CreateOrGetDestinationFolder(doc, ct).ConfigureAwait(false);

                _fileLogger.LogInformation("Destination folder for document {DocId}: {FolderId}",
                    doc.Id, destFolderId);

                // ========================================
                // STEP 2: Move or Copy document to destination folder
                // ========================================
                var useCopy = _options.Value.MoveService.UseCopy;
                var operationName = useCopy ? "Copying" : "Moving";

                _fileLogger.LogDebug("{Operation} document {DocId} (NodeId: {NodeId}) to folder {FolderId}",
                    operationName, doc.Id, doc.NodeId, destFolderId);

                bool success;
                if (useCopy)
                {
                    success = await _moveExecutor.CopyAsync(doc.NodeId, destFolderId, ct).ConfigureAwait(false);
                }
                else
                {
                    success = await _moveExecutor.MoveAsync(doc.NodeId, destFolderId, ct).ConfigureAwait(false);
                }

                if (!success)
                {
                    throw new InvalidOperationException(
                        $"{(useCopy ? "Copy" : "Move")} operation returned false for document {doc.Id} (NodeId: {doc.NodeId})");
                }

                _fileLogger.LogInformation("Document {DocId} successfully {Operation} to {FolderId}",
                    doc.Id, useCopy ? "copied" : "moved", destFolderId);

                // ========================================
                // STEP 3: Lookup DocumentMapping to get migrated docType and naziv
                // ========================================
                _fileLogger.LogDebug("Looking up DocumentMapping for document {DocId} with ecm:docDesc='{DocDesc}'",
                    doc.Id, doc.DocDescription);

                await using var mappingScope = _scopeFactory.CreateAsyncScope();
                var uow = mappingScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var mappingService = mappingScope.ServiceProvider.GetRequiredService<IDocumentMappingService>();

                string? migratedDocType = null;
                string? migratedNaziv = null;

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    if (!string.IsNullOrWhiteSpace(doc.DocDescription))
                    {
                        // Try to find by original name (Naziv field)
                        var mapping = await mappingService.FindByOriginalNameAsync(doc.DocDescription, ct).ConfigureAwait(false);

                        // If not found, try by Serbian name (NazivDokumenta field)
                        if (mapping == null)
                        {
                            mapping = await mappingService.FindBySerbianNameAsync(doc.DocDescription, ct).ConfigureAwait(false);
                        }

                        // If not found, try by migrated name (NazivDokumentaMigracija field)
                        if (mapping == null)
                        {
                            mapping = await mappingService.FindByMigratedNameAsync(doc.DocDescription, ct).ConfigureAwait(false);
                        }

                        if (mapping != null)
                        {
                            migratedDocType = mapping.SifraDokumentaMigracija;
                            migratedNaziv = mapping.NazivDokumentaMigracija;

                            _fileLogger.LogInformation(
                                "DocumentMapping found for document {DocId}: DocType='{DocType}', Naziv='{Naziv}'",
                                doc.Id, migratedDocType ?? "null", migratedNaziv ?? "null");
                        }
                        else
                        {
                            _fileLogger.LogWarning(
                                "DocumentMapping NOT found for document {DocId} with ecm:docDesc='{DocDesc}' - will use default values",
                                doc.Id, doc.DocDescription);
                        }
                    }

                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _fileLogger.LogError(ex,
                        "Failed to lookup DocumentMapping for document {DocId}, will use default values",
                        doc.Id);
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    // Continue with default values - don't fail the entire migration
                }

                // ========================================
                // STEP 4: Read existing document properties and update only ecm:docType and ecm:naziv
                // ========================================
                _fileLogger.LogDebug("Reading existing properties for document {DocId} (NodeId: {NodeId})", doc.Id, doc.NodeId);

                // Read current document properties from Alfresco
                var existingNode = await _read.GetNodeByIdAsync(doc.NodeId, ct).ConfigureAwait(false);
                var existingProperties = existingNode?.Entry?.Properties ?? new Dictionary<string, object>();

                _fileLogger.LogDebug("Document {DocId} has {Count} existing properties", doc.Id, existingProperties.Count);

                // Prepare properties to update - only ecm:docType and ecm:naziv
                var propertiesToUpdate = new Dictionary<string, object>();

                // Update ecm:docType if we have mapping
                if (!string.IsNullOrWhiteSpace(migratedDocType))
                {
                    propertiesToUpdate["ecm:docType"] = migratedDocType;
                    _fileLogger.LogDebug("Will update ecm:docType to '{DocType}' for document {DocId}", migratedDocType, doc.Id);
                }
                else if (!string.IsNullOrWhiteSpace(doc.DocumentType))
                {
                    propertiesToUpdate["ecm:docType"] = doc.DocumentType;
                    _fileLogger.LogDebug("Will update ecm:docType to '{DocType}' (from DocStaging fallback) for document {DocId}", doc.DocumentType, doc.Id);
                }

                // Update ecm:naziv if we have mapping
                if (!string.IsNullOrWhiteSpace(migratedNaziv))
                {
                    propertiesToUpdate["ecm:naziv"] = migratedNaziv;
                    _fileLogger.LogDebug("Will update ecm:naziv to '{Naziv}' for document {DocId}", migratedNaziv, doc.Id);
                }
                else if (!string.IsNullOrWhiteSpace(doc.DocDescription))
                {
                    propertiesToUpdate["ecm:naziv"] = doc.DocDescription;
                    _fileLogger.LogDebug("Will update ecm:naziv to '{Naziv}' (from DocStaging fallback) for document {DocId}", doc.DocDescription, doc.Id);
                }

                // Only update if we have properties to change
                if (propertiesToUpdate.Count > 0)
                {
                    _fileLogger.LogInformation("Updating {Count} properties for document {DocId}: {Properties}",
                        propertiesToUpdate.Count, doc.Id, string.Join(", ", propertiesToUpdate.Keys));

                    await _write.UpdateNodePropertiesAsync(doc.NodeId, propertiesToUpdate, ct).ConfigureAwait(false);

                    _fileLogger.LogInformation("Document {DocId} properties updated successfully (ecm:docType='{DocType}', ecm:naziv='{Naziv}')",
                        doc.Id, propertiesToUpdate.ContainsKey("ecm:docType") ? propertiesToUpdate["ecm:docType"] : "unchanged",
                        propertiesToUpdate.ContainsKey("ecm:naziv") ? propertiesToUpdate["ecm:naziv"] : "unchanged");
                }
                else
                {
                    _fileLogger.LogWarning("No properties to update for document {DocId} - skipping property update", doc.Id);
                }

                return true;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex,
                    "Failed to migrate document {DocId} (NodeId: {NodeId})",
                    doc.Id, doc.NodeId);
                throw;
            }
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
                catch (Exception ex)
                {
                    _dbLogger.LogError(ex, "Error in batch {BatchCounter}", batchCounter);

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

       
        private string GetParentFolderName(int? targetDossierType)
        {
            if (!targetDossierType.HasValue)
            {
                _fileLogger.LogWarning("TargetDossierType is null, defaulting to DOSSIERS-UNKNOWN");
                return "DOSSIERS-UNKNOWN";
            }

            var dossierType = (DossierType)targetDossierType.Value;

            var folderName = dossierType switch
            {
                DossierType.ClientFL => "DOSSIERS-PI",       // 500
                DossierType.ClientPL => "DOSSIERS-LE",       // 400
                DossierType.AccountPackage => "DOSSIERS-ACC", // 300
                DossierType.Deposit => "DOSSIERS-D",         // 700
                _ => "DOSSIERS-UNKNOWN"                      // 999 or other
            };

            _fileLogger.LogTrace("Mapped DossierType {DossierType} ({Code}) → Parent folder '{FolderName}'",
                dossierType, targetDossierType.Value, folderName);

            return folderName;
        }

       
        private async Task<string> CreateOrGetDestinationFolder(DocStaging doc, CancellationToken ct)
        {
            
            var parentFolderName = GetParentFolderName(doc.TargetDossierType);

            _fileLogger.LogDebug("Resolving parent folder '{ParentFolderName}' under root {RootId}",
                parentFolderName, _options.Value.RootDestinationFolderId);

            var parentFolderId = await _resolver.ResolveAsync(
                _options.Value.RootDestinationFolderId,
                parentFolderName,
                ct).ConfigureAwait(false);

            _fileLogger.LogTrace("Parent folder '{ParentFolderName}' → ID: {ParentFolderId}",
                parentFolderName, parentFolderId);

            // Step 2: Get individual dossier folder (PI102206, LE500342, etc.)
            var dossierId = doc.DossierDestFolderId;

            if (string.IsNullOrWhiteSpace(dossierId))
            {
                _fileLogger.LogError("DossierDestFolderId is empty for document {DocId}, cannot resolve destination folder", doc.Id);
                throw new InvalidOperationException($"DossierDestFolderId is empty for document {doc.Id}");
            }

            _fileLogger.LogDebug("Resolving dossier folder '{DossierId}' under parent {ParentFolderId}",
                dossierId, parentFolderId);

            
            var dossierFolderId = await _resolver.ResolveAsync(
                parentFolderId,
                dossierId,
                null, // No properties - folder already exists
                ct).ConfigureAwait(false);

            _fileLogger.LogTrace("Dossier folder '{DossierId}' → ID: {DossierFolderId}",
                dossierId, dossierFolderId);

            return dossierFolderId;
        }

       
        [Obsolete("No longer used - folders are created by FolderPreparationService in FAZA 3")]
        private Dictionary<string, object> BuildDossierProperties(DocStaging doc, Dictionary<string, object>? oldFolderProperties)
        {
            var properties = new Dictionary<string, object>();

            // Helper to safely get value from old properties
            object? GetOldProperty(string key)
            {
                if (oldFolderProperties == null)
                    return null;
                return oldFolderProperties.TryGetValue(key, out var value) ? value : null;
            }

            // ========================================
            // CRITICAL PROPERTIES (from doc or old folder)
            // ========================================

            // ecm:coreId
            if (!string.IsNullOrWhiteSpace(doc.CoreId))
                properties["ecm:coreId"] = doc.CoreId;
            else if (GetOldProperty("ecm:coreId") != null)
                properties["ecm:coreId"] = GetOldProperty("ecm:coreId")!;

            // ecm:docClientType (PI, LE, etc.)
            if (!string.IsNullOrWhiteSpace(doc.ClientSegment))
                properties["ecm:docClientType"] = doc.ClientSegment;
            else if (GetOldProperty("ecm:docClientType") != null)
                properties["ecm:docClientType"] = GetOldProperty("ecm:docClientType")!;

            // ecm:docDossierType ("Dosije klijenta FL", "Dosije klijenta PL", etc.)
            if (!string.IsNullOrWhiteSpace(doc.TipDosijea))
                properties["ecm:docDossierType"] = doc.TipDosijea;
            else if (GetOldProperty("ecm:docDossierType") != null)
                properties["ecm:docDossierType"] = GetOldProperty("ecm:docDossierType")!;

            // ========================================
            // COPY ALL OTHER RELEVANT PROPERTIES FROM OLD FOLDER
            // ========================================

            if (oldFolderProperties != null)
            {
                var propertiesToCopy = new[]
                {
                    // Core identification
                    "ecm:folderId",

                    // Client info
                    "ecm:clientName", "ecm:jmbg",
                    "ecm:bnkClientType",

                    // Product and contract info
                    "ecm:bnkTypeOfProduct",
                    "ecm:bnkNumberOfContract",
                    "ecm:bnkAccountNumber",

                    // Source info
                    "ecm:bnkSource", "ecm:bnkSourceId",

                    // Status and flags
                    "ecm:active", "ecm:bnkStatus",

                    // Staff and collaborator
                    "ecm:docStaff",
                    "ecm:barclex",

                    // Office and operational
                    "ecm:bnkOfficeId",

                    // Residence
                    "ecm:bnkResidence",

                    // Dates
                    "ecm:datumKreiranja",

                    // Creator info
                    "ecm:createdByName", "ecm:kreiraoId",

                    // Standard Content Model properties
                    "cm:title", "cm:description"
                };

                foreach (var prop in propertiesToCopy)
                {
                    var value = GetOldProperty(prop);
                    if (value != null && !properties.ContainsKey(prop))
                    {
                        properties[prop] = value;
                    }
                }

                // Properties that need to be renamed (old name -> new name)
                var propertiesToRename = new Dictionary<string, string>
                {
                    { "ecm:collaborator", "ecm:bnkContributor" },
                    { "ecm:opuRealization", "ecm:bnkRealizationOPUID" },
                    { "ecm:archiveDate", "ecm:bnkArchiveDate" },
                    { "ecm:creator", "ecm:bnkCreator" },
                    { "ecm:clientSubtype", "ecm:bnkClientSubtype" }
                };

                foreach (var (oldName, newName) in propertiesToRename)
                {
                    var value = GetOldProperty(oldName);
                    if (value != null && !properties.ContainsKey(newName))
                    {
                        properties[newName] = value;
                    }
                }
            }

            _fileLogger.LogTrace("Built dossier properties: {Count} properties (from doc: 3, copied from old folder: {Copied})",
                properties.Count, properties.Count - 3);

            return properties;
        }

        /// <summary>
        /// Builds Alfresco properties for migrated document
        /// Per Analiza_migracije_v2.md and application property mapping
        /// </summary>
        /// <param name="doc">Document staging data</param>
        /// <param name="migratedDocType">Migrated document type code from DocumentMapping (SifraDokumentaMigracija)</param>
        /// <param name="migratedNaziv">Migrated document name from DocumentMapping (NazivDokumentaMigracija)</param>
        private Dictionary<string, object> BuildDocumentProperties(DocStaging doc, string? migratedDocType, string? migratedNaziv)
        {
            var properties = new Dictionary<string, object>
            {
                // ========================================
                // Core properties (ALWAYS set)
                // ========================================
                ["cm:title"] = doc.DocDescription ?? doc.Name ?? "Unknown",
                ["cm:description"] = doc.DocDescription ?? "",
                ["ecm:docDesc"] = doc.DocDescription ?? "",
                ["ecm:coreId"] = doc.CoreId ?? "",
                ["ecm:status"] = doc.NewAlfrescoStatus ?? "validiran",
                ["ecm:docDossierType"] = doc.TipDosijea ?? "",
                ["ecm:docClientType"] = doc.ClientSegment ?? "",
                ["ecm:source"] = doc.Source ?? "Heimdall"
            };

            // ========================================
            // CRITICAL: Set ecm:docType and ecm:naziv from DocumentMapping
            // ========================================
            // ecm:docType - Use migrated code from DocumentMapping if available, otherwise fallback to doc.DocumentType
            if (!string.IsNullOrWhiteSpace(migratedDocType))
            {
                properties["ecm:docType"] = migratedDocType;
                _fileLogger.LogDebug("Set ecm:docType='{DocType}' from DocumentMapping for document {DocId}",
                    migratedDocType, doc.Id);
            }
            else if (!string.IsNullOrWhiteSpace(doc.DocumentType))
            {
                properties["ecm:docType"] = doc.DocumentType;
                _fileLogger.LogDebug("Set ecm:docType='{DocType}' from doc.DocumentType (fallback) for document {DocId}",
                    doc.DocumentType, doc.Id);
            }
            else
            {
                properties["ecm:docType"] = "";
                _fileLogger.LogWarning("ecm:docType is empty for document {DocId} - no mapping found and no fallback", doc.Id);
            }

            // ecm:naziv - Use migrated name from DocumentMapping if available, otherwise fallback to doc.DocDescription
            if (!string.IsNullOrWhiteSpace(migratedNaziv))
            {
                properties["ecm:naziv"] = migratedNaziv;
                _fileLogger.LogDebug("Set ecm:naziv='{Naziv}' from DocumentMapping for document {DocId}",
                    migratedNaziv, doc.Id);
            }
            else if (!string.IsNullOrWhiteSpace(doc.DocDescription))
            {
                properties["ecm:naziv"] = doc.DocDescription;
                _fileLogger.LogDebug("Set ecm:naziv='{Naziv}' from doc.DocDescription (fallback) for document {DocId}",
                    doc.DocDescription, doc.Id);
            }
            else
            {
                properties["ecm:naziv"] = "";
                _fileLogger.LogWarning("ecm:naziv is empty for document {DocId} - no mapping found and no fallback", doc.Id);
            }

            // ========================================
            // Optional properties (set if available)
            // ========================================

            // ecm:docCreationDate - Original creation date (NOT migration date!)
            if (doc.OriginalCreatedAt.HasValue)
                properties["ecm:docCreationDate"] = doc.OriginalCreatedAt.Value.ToString("o");

            // ecm:docAccountNumbers - Account numbers (for KDP documents)
            if (!string.IsNullOrWhiteSpace(doc.AccountNumbers))
                properties["ecm:docAccountNumbers"] = doc.AccountNumbers;

            // ecm:brojUgovora - Contract number
            if (!string.IsNullOrWhiteSpace(doc.ContractNumber))
                properties["ecm:brojUgovora"] = doc.ContractNumber;

            // ecm:tipProizvoda - Product type
            if (!string.IsNullOrWhiteSpace(doc.ProductType))
                properties["ecm:tipProizvoda"] = doc.ProductType;

            _fileLogger.LogTrace("Built document properties: {Count} properties for document {DocId} (ecm:docType='{DocType}', ecm:naziv='{Naziv}')",
                properties.Count, doc.Id, properties["ecm:docType"], properties["ecm:naziv"]);

            return properties;
        }

        #endregion


    }
}
