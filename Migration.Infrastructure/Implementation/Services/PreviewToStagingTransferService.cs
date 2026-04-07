using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Abstraction.Models;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    public class PreviewToStagingTransferService : IPreviewToStagingTransferService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptions<MigrationOptions> _options;
        private readonly ILogger _fileLogger;
        private readonly ILogger _uiLogger;

        private const int MaxDeadlockRetries = 3;

        public PreviewToStagingTransferService(
            IServiceScopeFactory scopeFactory,
            IOptions<MigrationOptions> options,
            ILoggerFactory loggerFactory)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _options      = options      ?? throw new ArgumentNullException(nameof(options));
            _fileLogger   = loggerFactory.CreateLogger("FileLogger");
            _uiLogger     = loggerFactory.CreateLogger("UiLogger");
        }

        public async Task<bool> RunAsync(
            string? dossierType,
            string? targetDossierType,
            CancellationToken ct,
            Action<WorkerProgress>? progressCallback = null)
        {
            var sw = Stopwatch.StartNew();
            var mdp       = _options.Value.PreviewToStagingTransfer.MaxDegreeOfParallelism ?? 6;
            var batchSize = _options.Value.PreviewToStagingTransfer.BatchSize ?? 500;

            _fileLogger.LogInformation(
                "PreviewToStagingTransferService: Start. DossierType={DossierType}, TargetDossierType={TargetDossierType}, MDP={MDP}, BatchSize={BatchSize}",
                dossierType ?? "*", targetDossierType ?? "*", mdp, batchSize);
            _uiLogger.LogInformation("PreviewToStagingTransferService: Pokretanje transfera...");

            long totalTransferred = 0;
            long totalFailed      = 0;

            await Parallel.ForEachAsync(
                FetchBatchesAsync(dossierType, targetDossierType, batchSize, ct),
                new ParallelOptions { MaxDegreeOfParallelism = mdp, CancellationToken = ct },
                async (batch, innerCt) =>
                {
                    var docStagingBatch = new List<DocStaging>(batch.Count);
                    var transferredIds  = new List<long>(batch.Count);
                    var failedIds       = new List<long>(batch.Count);

                    foreach (var preview in batch)
                    {
                        try
                        {
                            var doc = MapToDocStaging(preview);
                            docStagingBatch.Add(doc);
                            transferredIds.Add(preview.Id);
                        }
                        catch (Exception ex)
                        {
                            failedIds.Add(preview.Id);
                            _fileLogger.LogError(
                                "PreviewToStagingTransferService: Greska pri mapiranju NodeId={NodeId}: {Error}",
                                preview.NodeId, ex.Message);
                        }
                    }

                    if (docStagingBatch.Count > 0)
                    {
                        bool writeSuccess = false;
                        for (int attempt = 1; attempt <= MaxDeadlockRetries && !writeSuccess; attempt++)
                        {
                            if (attempt > 1)
                                await Task.Delay(attempt * 200, innerCt).ConfigureAwait(false);

                            await using var scope   = _scopeFactory.CreateAsyncScope();
                            var uow      = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                            var docRepo  = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();
                            var prevRepo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                            await uow.BeginAsync(ct: innerCt).ConfigureAwait(false);
                            try
                            {
                                await docRepo.InsertManyIgnoreDuplicatesAsync(docStagingBatch, innerCt).ConfigureAwait(false);
                                await prevRepo.UpdateTransferredBatchAsync(transferredIds, innerCt).ConfigureAwait(false);
                                await uow.CommitAsync(ct: innerCt).ConfigureAwait(false);

                                Interlocked.Add(ref totalTransferred, transferredIds.Count);
                                writeSuccess = true;
                            }
                            catch (SqlException ex) when (ex.Number == 1205) // Deadlock
                            {
                                await uow.RollbackAsync(ct: innerCt).ConfigureAwait(false);
                                _fileLogger.LogWarning(
                                    "PreviewToStagingTransferService: Deadlock na batch-u, pokusaj {Attempt}/{Max}.",
                                    attempt, MaxDeadlockRetries);

                                if (attempt == MaxDeadlockRetries)
                                {
                                    _fileLogger.LogError(
                                        "PreviewToStagingTransferService: Batch nije uspeo nakon {Max} pokusaja, resetovanje.", MaxDeadlockRetries);
                                    failedIds.AddRange(transferredIds);
                                }
                            }
                            catch (Exception ex)
                            {
                                await uow.RollbackAsync(ct: innerCt).ConfigureAwait(false);
                                _fileLogger.LogError(
                                    "PreviewToStagingTransferService: Greska pri upisu batcha: {Error}", ex.Message);
                                failedIds.AddRange(transferredIds);
                                break;
                            }
                        }
                    }

                    // Reset zapisa koji su ostali na TRANSFER_IN_PROGRESS zbog greske
                    if (failedIds.Count > 0)
                    {
                        Interlocked.Add(ref totalFailed, failedIds.Count);
                        await TryResetAsync(failedIds, innerCt).ConfigureAwait(false);
                    }

                    var transferred = Interlocked.Read(ref totalTransferred);
                    var failed      = Interlocked.Read(ref totalFailed);
                    var msg = $"Transferovano {transferred}, greske {failed}";
                    _fileLogger.LogInformation("PreviewToStagingTransferService: {Msg}", msg);

                    progressCallback?.Invoke(new WorkerProgress
                    {
                        ProcessedItems = transferred + failed,
                        SuccessCount   = (int)transferred,
                        FailedCount    = (int)failed,
                        Message        = msg,
                        Timestamp      = DateTimeOffset.UtcNow
                    });
                });

            var summary = $"Transfer zavrsen za {sw.Elapsed.TotalSeconds:F1}s — " +
                          $"transferovano={totalTransferred}, greske={totalFailed}";
            _fileLogger.LogInformation("PreviewToStagingTransferService: {Summary}", summary);
            _uiLogger.LogInformation("PreviewToStagingTransferService: {Summary}", summary);

            progressCallback?.Invoke(new WorkerProgress
            {
                ProcessedItems = totalTransferred + totalFailed,
                SuccessCount   = (int)totalTransferred,
                FailedCount    = (int)totalFailed,
                Message        = summary,
                Timestamp      = DateTimeOffset.UtcNow
            });

            return true;
        }

        private async IAsyncEnumerable<IList<PreviewDocStaging>> FetchBatchesAsync(
            string? dossierType,
            string? targetDossierType,
            int batchSize,
            [EnumeratorCancellation] CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                IList<PreviewDocStaging> batch;
                await using (var scope = _scopeFactory.CreateAsyncScope())
                {
                    var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                    await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                    try
                    {
                        batch = await repo.TakeReadyForTransferAsync(batchSize, dossierType, targetDossierType, ct)
                                          .ConfigureAwait(false);
                        await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                        throw;
                    }
                }

                if (batch.Count == 0)
                {
                    _uiLogger.LogInformation("PreviewToStagingTransferService: Nema vise zapisa za transfer.");
                    yield break;
                }

                _fileLogger.LogInformation(
                    "PreviewToStagingTransferService: Preuzet batch od {Count} zapisa.", batch.Count);

                yield return batch;
            }
        }

        private async Task TryResetAsync(IList<long> ids, CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                await repo.ResetTransferInProgressAsync(ids, ct).ConfigureAwait(false);
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(
                    "PreviewToStagingTransferService: Reset TRANSFER_IN_PROGRESS nije uspeo: {Error}", ex.Message);
            }
        }

        private static DocStaging MapToDocStaging(PreviewDocStaging src)
        {
            return new DocStaging
            {
                NodeId   = src.NodeId   ?? string.Empty,
                Name     = src.Name     ?? string.Empty,
                IsFolder = false,
                IsFile   = true,
                NodeType = src.NodeType ?? string.Empty,
                ParentId = src.ParentId ?? string.Empty,
                Status   = "PREPARED",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,

                DocumentType          = src.NewDocumentCode,
                DocumentTypeMigration = src.DocumentTypeMigration,
                Source                = src.Source,
                IsActive              = src.IsActive == 1,
                CategoryCode          = src.CategoryCode,
                CategoryName          = src.CategoryName,
                OriginalCreatedAt     = src.OriginalCreatedAt,
                ContractNumber        = src.ContractNumber,
                CoreId                = src.CoreId,
                AccountNumbers        = src.AccountNumbers,
                ProductType           = src.ProductType,
                OriginalDocumentName  = src.OriginalDocumentName,
                NewDocumentName       = src.NewDocumentName,
                OriginalDocumentCode  = src.OriginalDocumentCode,
                NewDocumentCode       = src.NewDocumentCode,
                TipDosijea            = src.DossierType,
                TargetDossierType     = int.TryParse(src.TargetDossierType, out var tdt) ? tdt : null,
                ClientSegment         = src.ClientSegment,
                OldAlfrescoStatus     = src.OldAlfrescoStatus,
                NewAlfrescoStatus     = src.NewAlfrescoStatus,
                DocDescription        = src.DocDescription,
                DossierDestFolderId   = src.DossierDestinationFolderName,
                DestinationFolderId   = src.DossierDestinationFolderId,
                DossierDestFolderIsCreated = src.DossierDestinationFolderIsCreated != 1,
            };
        }
    }
}
