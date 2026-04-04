using Alfresco.Contracts.Oracle.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Abstraction.Models;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    public class PreviewToStagingTransferService : IPreviewToStagingTransferService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _fileLogger;
        private readonly ILogger _uiLogger;

        private const int BatchSize = 500;

        public PreviewToStagingTransferService(
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
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
            _fileLogger.LogInformation(
                "PreviewToStagingTransferService: Start. DossierType={DossierType}, TargetDossierType={TargetDossierType}",
                dossierType ?? "*", targetDossierType ?? "*");
            _uiLogger.LogInformation("PreviewToStagingTransferService: Pokretanje transfera...");

            long totalTransferred = 0;
            long totalFailed      = 0;

            // Dohvatamo sve kandidate odjednom (filtriramo u SQL-u)
            IList<PreviewDocStaging> candidates;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var result = await repo.GetForTransferAsync(dossierType, targetDossierType, ct).ConfigureAwait(false);
                    candidates = result.ToList();
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }

            if (candidates.Count == 0)
            {
                _uiLogger.LogInformation("PreviewToStagingTransferService: Nema zapisa za transfer.");
                progressCallback?.Invoke(new WorkerProgress
                {
                    ProcessedItems = 0,
                    SuccessCount   = 0,
                    FailedCount    = 0,
                    Message        = "Nema zapisa za transfer.",
                    Timestamp      = DateTimeOffset.UtcNow
                });
                return true;
            }

            _fileLogger.LogInformation(
                "PreviewToStagingTransferService: Pronadjeno {Count} zapisa za transfer.",
                candidates.Count);

            // Procesiramo u batchevima
            for (int offset = 0; offset < candidates.Count; offset += BatchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batch = candidates.Skip(offset).Take(BatchSize).ToList();

                var docStagingBatch  = new List<DocStaging>(batch.Count);
                var transferredIds   = new List<long>(batch.Count);

                foreach (var preview in batch)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var doc = MapToDocStaging(preview);
                        docStagingBatch.Add(doc);
                        transferredIds.Add(preview.Id);
                        totalTransferred++;
                    }
                    catch (Exception ex)
                    {
                        totalFailed++;
                        _fileLogger.LogError(
                            "PreviewToStagingTransferService: Greska pri mapiranju NodeId={NodeId}: {Error}",
                            preview.NodeId, ex.Message);
                    }
                }

                if (docStagingBatch.Count > 0)
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var uow      = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var docRepo  = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();
                    var prevRepo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                    await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                    try
                    {
                        await docRepo.InsertManyIgnoreDuplicatesAsync(docStagingBatch, ct).ConfigureAwait(false);
                        await prevRepo.UpdateTransferredBatchAsync(transferredIds, ct).ConfigureAwait(false);
                        await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                        throw;
                    }
                }

                var batchNum = (offset / BatchSize) + 1;
                var msg = $"Batch {batchNum}: transferovano {totalTransferred}, greske {totalFailed}";
                _fileLogger.LogInformation("PreviewToStagingTransferService: {Msg}", msg);

                progressCallback?.Invoke(new WorkerProgress
                {
                    ProcessedItems = totalTransferred + totalFailed,
                    SuccessCount   = (int)totalTransferred,
                    FailedCount    = (int)totalFailed,
                    Message        = msg,
                    Timestamp      = DateTimeOffset.UtcNow
                });
            }

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
                Status   = "READY",
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
                // DossierDestinationFolderIsCreated=1 znaci kreiran migracijom → DossierDestFolderIsCreated=false
                // DossierDestinationFolderIsCreated=0 znaci vec postojao   → DossierDestFolderIsCreated=true
                DossierDestFolderIsCreated = src.DossierDestinationFolderIsCreated != 1,
            };
        }
    }
}
