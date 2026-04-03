using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.SqlServer;
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
    public class PreviewFolderRollbackService : IPreviewFolderRollbackService
    {
        private readonly IAlfrescoWriteApi _alfrescoWriteApi;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _fileLogger;
        private readonly ILogger _uiLogger;

        public PreviewFolderRollbackService(
            IAlfrescoWriteApi alfrescoWriteApi,
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory)
        {
            _alfrescoWriteApi = alfrescoWriteApi ?? throw new ArgumentNullException(nameof(alfrescoWriteApi));
            _scopeFactory     = scopeFactory     ?? throw new ArgumentNullException(nameof(scopeFactory));
            _fileLogger = loggerFactory.CreateLogger("FileLogger");
            _uiLogger   = loggerFactory.CreateLogger("UiLogger");
        }

        public async Task<bool> RunAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback = null)
        {
            var sw = Stopwatch.StartNew();

            _uiLogger.LogInformation("PreviewFolderRollbackService: Pokretanje rollbacka Faze 3...");

            IList<(string FolderName, string FolderId)> folders;

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var result = await repo.GetCreatedFolderIdsAsync(ct).ConfigureAwait(false);
                    folders = result.ToList();
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }

            if (folders.Count == 0)
            {
                _uiLogger.LogInformation("PreviewFolderRollbackService: Nema foldera sa statusom FOLDER_CREATED, nista za rollback.");
                return true;
            }

            _fileLogger.LogInformation(
                "PreviewFolderRollbackService: Pronadjeno {Count} foldera za brisanje.", folders.Count);

            long deleted = 0;
            long failed  = 0;

            await Parallel.ForEachAsync(
                folders,
                new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = ct },
                async (folder, token) =>
                {
                    var (folderName, folderId) = folder;
                    try
                    {
                        var success = await _alfrescoWriteApi.DeleteNodeAsync(folderId, token).ConfigureAwait(false);

                        if (!success)
                            throw new InvalidOperationException($"DeleteNodeAsync vratio false za NodeId={folderId}.");

                        _fileLogger.LogInformation(
                            "PreviewFolderRollbackService: Obrisan folder '{FolderName}' (NodeId={FolderId})",
                            folderName, folderId);

                        await ResetFolderStatusAsync(folderName, token).ConfigureAwait(false);
                        Interlocked.Increment(ref deleted);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        _fileLogger.LogError(
                            "PreviewFolderRollbackService: Greska pri brisanju '{FolderName}' (NodeId={FolderId}): {Error}",
                            folderName, folderId, ex.Message);
                    }

                    progressCallback?.Invoke(new WorkerProgress
                    {
                        ProcessedItems = (int)(Interlocked.Read(ref deleted) + Interlocked.Read(ref failed)),
                        TotalItems     = folders.Count,
                        SuccessCount   = (int)Interlocked.Read(ref deleted),
                        FailedCount    = (int)Interlocked.Read(ref failed),
                        Message        = $"Obrisano: {Interlocked.Read(ref deleted)}/{folders.Count}  |  Greske: {Interlocked.Read(ref failed)}",
                        Timestamp      = DateTimeOffset.UtcNow
                    });
                });

            var summary = $"Rollback zavrsen za {sw.Elapsed.TotalSeconds:F1}s — " +
                          $"obrisano={deleted}, greske={failed}";
            _fileLogger.LogInformation("PreviewFolderRollbackService: {Summary}", summary);
            _uiLogger.LogInformation("PreviewFolderRollbackService: {Summary}", summary);

            return failed == 0;
        }

        private async Task ResetFolderStatusAsync(string folderName, CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                await repo.UpdateFolderDataAsync(folderName, null, isCreated: 0, status: "FOLDER_PENDING_CREATION", ct)
                          .ConfigureAwait(false);
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);
            }
            catch
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }
    }
}
