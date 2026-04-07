using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Mapper;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
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
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    public class PreviewFolderCreationService : IPreviewFolderCreationService
    {
        private readonly IAlfrescoReadApi _alfrescoReadApi;
        private readonly IAlfrescoWriteApi _alfrescoWriteApi;
        private readonly IOptions<MigrationOptions> _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IClientApiEnricher _clientApiEnricher;
        private readonly ILogger _fileLogger;
        private readonly ILogger _dbLogger;
        private readonly ILogger _uiLogger;

        public PreviewFolderCreationService(
            IAlfrescoReadApi alfrescoReadApi,
            IAlfrescoWriteApi alfrescoWriteApi,
            IOptions<MigrationOptions> options,
            IServiceScopeFactory scopeFactory,
            IClientApiEnricher clientApiEnricher,
            ILoggerFactory loggerFactory)
        {
            _alfrescoReadApi    = alfrescoReadApi    ?? throw new ArgumentNullException(nameof(alfrescoReadApi));
            _alfrescoWriteApi   = alfrescoWriteApi   ?? throw new ArgumentNullException(nameof(alfrescoWriteApi));
            _options            = options            ?? throw new ArgumentNullException(nameof(options));
            _scopeFactory       = scopeFactory       ?? throw new ArgumentNullException(nameof(scopeFactory));
            _clientApiEnricher  = clientApiEnricher  ?? throw new ArgumentNullException(nameof(clientApiEnricher));
            _fileLogger = loggerFactory.CreateLogger("FileLogger");
            _dbLogger   = loggerFactory.CreateLogger("DbLogger");
            _uiLogger   = loggerFactory.CreateLogger("UiLogger");
        }

        public async Task<bool> RunAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback = null)
        {
            var sw = Stopwatch.StartNew();
            var batchSize = _options.Value.PreviewFolderCreation.BatchSize ?? 50;

            _fileLogger.LogInformation("PreviewFolderCreationService: Start.");
            _uiLogger.LogInformation("PreviewFolderCreationService: Pokretanje Faze 3 — kreiranje foldera i upis u FolderStaging...");

            long totalProcessed = 0;
            long totalFailed    = 0;
            int  batchNum       = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                // Atomično uzimamo batch foldera (FOLDER_PENDING_CREATION | FOLDER_EXISTS → IN_PROGRESS)
                IList<(string FolderName, bool NeedsCreation)> folderItems;
                await using (var scope = _scopeFactory.CreateAsyncScope())
                {
                    var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                    await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                    try
                    {
                        var items = await repo.GetDistinctFoldersForFolderStagingAsync(batchSize, ct).ConfigureAwait(false);
                        folderItems = items.ToList();
                        await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                        throw;
                    }
                }

                if (folderItems.Count == 0)
                {
                    _fileLogger.LogInformation("PreviewFolderCreationService: Nema vise foldera za obradu, zavrseno.");
                    break;
                }

                batchNum++;
                _fileLogger.LogInformation(
                    "PreviewFolderCreationService: Batch {Batch} — {Count} foldera",
                    batchNum, folderItems.Count);

                var folderStagingBag = new ConcurrentBag<FolderStaging>();

                await Parallel.ForEachAsync(
                    folderItems,
                    new ParallelOptions { MaxDegreeOfParallelism = _options.Value.PreviewFolderCreation.MaxDegreeOfParallelism ?? 5, CancellationToken = ct },
                    async (folderItem, token) =>
                    {
                        var (folderName, needsCreation) = folderItem;
                        try
                        {
                            // 1. Uzimamo reprezentativni zapis
                            PreviewDocStaging? record;
                            await using (var scope = _scopeFactory.CreateAsyncScope())
                            {
                                var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                                var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                                await uow.BeginAsync(ct: token).ConfigureAwait(false);
                                record = await repo.GetFirstRecordByFolderNameAsync(folderName, token).ConfigureAwait(false);
                                await uow.CommitAsync(ct: token).ConfigureAwait(false);
                            }

                            if (record == null)
                                throw new InvalidOperationException($"Nije pronađen nijedan zapis za folder '{folderName}'.");

                            string? nodeId = record.DossierDestinationFolderId;

                            // 2. Kreiranje u Alfresci samo ako je potrebno
                            if (needsCreation)
                            {
                                nodeId = await CreateInAlfrescoAsync(record, folderName, token).ConfigureAwait(false);
                                await PersistCreatedFolderAsync(folderName, nodeId, token).ConfigureAwait(false);

                                _fileLogger.LogInformation(
                                    "PreviewFolderCreationService: '{Folder}' kreiran → NodeId={NodeId}",
                                    folderName, nodeId);
                            }
                            else
                            {
                                _fileLogger.LogInformation(
                                    "PreviewFolderCreationService: '{Folder}' vec postoji → NodeId={NodeId}",
                                    folderName, nodeId);
                                // Vracamo status sa IN_PROGRESS na FOLDER_EXISTS
                                // kako bi ih naredne faze (export/transfer) mogle obuhvatiti
                                await PersistExistingFolderAsync(folderName, nodeId, token).ConfigureAwait(false);
                            }

                            // 3. Dodajemo u bag za FolderStaging
                            folderStagingBag.Add(BuildFolderStagingRecord(record, nodeId, needsCreation));
                            Interlocked.Increment(ref totalProcessed);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref totalFailed);
                            _fileLogger.LogError(
                                "PreviewFolderCreationService: Greska za folder '{Folder}': {Error}",
                                folderName, ex.Message);
                            _dbLogger.LogError(ex, "PreviewFolderCreationService: Folder '{Folder}'", folderName);

                            await TryResetStatusAsync(folderName, needsCreation ? "FOLDER_PENDING_CREATION" : "FOLDER_PENDING_EXISTS", token)
                                .ConfigureAwait(false);
                        }
                    }).ConfigureAwait(false);

                progressCallback?.Invoke(new WorkerProgress
                {
                    ProcessedItems = (int)(Interlocked.Read(ref totalProcessed) + Interlocked.Read(ref totalFailed)),
                    SuccessCount   = (int)Interlocked.Read(ref totalProcessed),
                    FailedCount    = (int)Interlocked.Read(ref totalFailed),
                    Message        = $"Batch {batchNum}: obradjeno {Interlocked.Read(ref totalProcessed)}, greske {Interlocked.Read(ref totalFailed)}",
                    Timestamp      = DateTimeOffset.UtcNow
                });

                // Bulk-insert FolderStaging posle svakog batcha
                if (!folderStagingBag.IsEmpty)
                {
                    var toInsert = folderStagingBag.ToList();
                    await using var fsScope = _scopeFactory.CreateAsyncScope();
                    var fsUow  = fsScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var fsRepo = fsScope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();

                    await fsUow.BeginAsync(ct: ct).ConfigureAwait(false);
                    try
                    {
                        await fsRepo.InsertManyIgnoreDuplicatesAsync(toInsert, ct).ConfigureAwait(false);
                        await fsUow.CommitAsync(ct: ct).ConfigureAwait(false);
                        _fileLogger.LogInformation(
                            "PreviewFolderCreationService: FolderStaging — upisano {Count} foldera (batch {Batch})",
                            toInsert.Count, batchNum);
                    }
                    catch
                    {
                        await fsUow.RollbackAsync(ct: ct).ConfigureAwait(false);
                        throw;
                    }
                }
            }

            var summary = $"Faza 3 zavrsena za {sw.Elapsed.TotalSeconds:F1}s — " +
                          $"obradjeno={totalProcessed}, greske={totalFailed}";
            _fileLogger.LogInformation("PreviewFolderCreationService: {Summary}", summary);
            _uiLogger.LogInformation("PreviewFolderCreationService: {Summary}", summary);

            progressCallback?.Invoke(new WorkerProgress
            {
                ProcessedItems = (int)(totalProcessed + totalFailed),
                SuccessCount   = (int)totalProcessed,
                FailedCount    = (int)totalFailed,
                Message        = summary,
                Timestamp      = DateTimeOffset.UtcNow
            });

            return true;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Kreiranje foldera u Alfresci
        // ──────────────────────────────────────────────────────────────────────

        private async Task<string> CreateInAlfrescoAsync(PreviewDocStaging record, string folderName, CancellationToken ct)
        {
            var prefix          = DossierIdFormatter.ExtractPrefix(folderName)?.ToUpperInvariant() ?? "";
            var dossierParentId = ResolveDossierParentId(prefix);

            var clientData = ReconstructClientData(record);
            var properties = _clientApiEnricher.BuildFolderProperties(clientData, folderName);
            var nodeType   = DetermineNodeType(record.TargetDossierType);

            _fileLogger.LogInformation(
                "PreviewFolderCreationService: Kreiranje '{Folder}' pod '{Parent}' (nodeType={NodeType}, props={PropCount})",
                folderName, dossierParentId, nodeType, properties.Count);

            try
            {
                var created = await _alfrescoWriteApi
                    .CreateFolderAsync_v1(dossierParentId, folderName, properties, nodeType, ct)
                    .ConfigureAwait(false);

                return created.Id;
            }
            catch (Exception ex)
            {
                _fileLogger.LogWarning(
                    "PreviewFolderCreationService: Kreiranje '{Folder}' nije uspelo ({Error}), proveravam race condition...",
                    folderName, ex.Message);

                // Race condition: možda je drugi thread već kreirao
                var existing = await _alfrescoReadApi
                    .GetFolderByNameAsync(dossierParentId, folderName, ct)
                    .ConfigureAwait(false);

                if (existing?.Entry?.Id != null)
                {
                    _fileLogger.LogInformation(
                        "PreviewFolderCreationService: Race condition — '{Folder}' vec postoji (NodeId={NodeId})",
                        folderName, existing.Entry.Id);
                    return existing.Entry.Id;
                }

                throw;
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // DB persist
        // ──────────────────────────────────────────────────────────────────────

        private async Task PersistCreatedFolderAsync(string folderName, string nodeId, CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                await repo.UpdateFolderDataAsync(folderName, nodeId, isCreated: 1, status: "FOLDER_CREATED", ct)
                          .ConfigureAwait(false);
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);
            }
            catch
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        private async Task PersistExistingFolderAsync(string folderName, string? nodeId, CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                await repo.UpdateFolderDataAsync(folderName, nodeId, isCreated: 1, status: "FOLDER_EXISTS", ct)
                          .ConfigureAwait(false);
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);
            }
            catch
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        private async Task TryResetStatusAsync(string folderName, string originalStatus, CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                await repo.UpdateFolderDataAsync(folderName, null, 0, originalStatus, ct).ConfigureAwait(false);
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(
                    "PreviewFolderCreationService: Ne mogu reset status za '{Folder}': {Error}",
                    folderName, ex.Message);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Gradnja FolderStaging zapisa iz PreviewDocStaging
        // ──────────────────────────────────────────────────────────────────────

        private static FolderStaging BuildFolderStagingRecord(PreviewDocStaging record, string? nodeId, bool needsCreation)
        {
            var now = DateTime.UtcNow;
            return new FolderStaging
            {
                NodeId            = nodeId,
                Name              = record.DossierDestinationFolderName,
                Status            = "DONE",
                CreatedAt         = now,
                UpdatedAt         = now,
                ArchivedAt        = now,
                ProcessDate       = now,
                ClientType        = record.ClientApiClientType,
                CoreId            = record.CoreId,
                ClientName        = record.ClientApiClientName,
                MbrJmbg           = record.ClientApiMbrJmbg,
                ProductType       = record.ProductType,
                Source            = record.Source,
                Residency         = record.ClientApiResidency,
                Segment           = record.ClientApiSegment,
                ClientSubtype     = record.ClientApiClientSubtype,
                Staff             = record.ClientApiStaff,
                OpuUser           = record.ClientApiOpuUser,
                OpuRealization    = record.ClientApiOpuRealization,
                Barclex           = record.ClientApiBarclex,
                Collaborator      = record.ClientApiCollaborator,
                BarCLEXName       = record.ClientApiBarCLEXName,
                BarCLEXOpu        = record.ClientApiBarCLEXOpu,
                BarCLEXGroupName  = record.ClientApiBarCLEXGroupName,
                BarCLEXGroupCode  = record.ClientApiBarCLEXGroupCode,
                BarCLEXCode       = record.ClientApiBarCLEXCode,
                TipDosijea        = record.DossierType,
                TargetDossierType = record.TargetDossierType,
                ClientSegment     = record.ClientApiSegment,
                // needsCreation=true → folder kreiran migracijom → IsNewlyCreated=1
                // needsCreation=false → folder vec postojao → IsNewlyCreated=0
                IsNewlyCreated    = needsCreation ? 1 : 0,
            };
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        private string ResolveDossierParentId(string prefix)
        {
            var opts = _options.Value;
            var raw = prefix switch
            {
                "PI"  => opts.RootPIFolderId,
                "LE"  => opts.RootLEFolderId,
                "ACC" => opts.RootACCFolderId,
                "DE"   => opts.RootDepoFolderId,
                _     => opts.RootOtherFolderId,
            };

            var guid = ExtractGuid(raw);
            if (string.IsNullOrWhiteSpace(guid))
                throw new InvalidOperationException(
                    $"RootFolderId za prefiks '{prefix}' nije konfigurisan ili je prazan u appsettings.");

            return guid;
        }

        private static string? ExtractGuid(string? workspaceUrl)
        {
            if (string.IsNullOrWhiteSpace(workspaceUrl)) return null;
            var idx = workspaceUrl.LastIndexOf('/');
            return idx >= 0 ? workspaceUrl[(idx + 1)..] : workspaceUrl;
        }

        private static ClientData ReconstructClientData(PreviewDocStaging record) => new ClientData
        {
            MbrJmbg          = record.ClientApiMbrJmbg          ?? string.Empty,
            ClientName       = record.ClientApiClientName        ?? string.Empty,
            ClientType       = record.ClientApiClientType        ?? string.Empty,
            ClientSubtype    = record.ClientApiClientSubtype     ?? string.Empty,
            Residency        = record.ClientApiResidency         ?? string.Empty,
            Segment          = record.ClientApiSegment           ?? string.Empty,
            Staff            = record.ClientApiStaff,
            OpuUser          = record.ClientApiOpuUser,
            OpuRealization   = record.ClientApiOpuRealization,
            Barclex          = record.ClientApiBarclex,
            Collaborator     = record.ClientApiCollaborator,
            BarCLEXName      = record.ClientApiBarCLEXName,
            BarCLEXOpu       = record.ClientApiBarCLEXOpu,
            BarCLEXGroupName = record.ClientApiBarCLEXGroupName,
            BarCLEXGroupCode = record.ClientApiBarCLEXGroupCode,
            BarCLEXCode      = record.ClientApiBarCLEXCode,
        };

        private string DetermineNodeType(string? targetDossierTypeStr)
        {
            if (int.TryParse(targetDossierTypeStr, out var typeInt))
            {
                var dossierType = (DossierType)typeInt;
                var enumName    = dossierType.ToString();
                var mapping     = _options.Value.FolderNodeTypeMapping;

                if (mapping.TryGetValue(enumName, out var nodeType))
                    return nodeType;
            }

            return _options.Value.FolderNodeTypeMapping.GetValueOrDefault("Default", "cm:folder");
        }
    }
}
