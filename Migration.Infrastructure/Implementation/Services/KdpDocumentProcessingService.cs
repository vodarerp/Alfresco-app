using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Request;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    /// <summary>
    /// Servis za obradu KDP dokumenata (tipovi 00824 i 00099)
    /// </summary>
    public class KdpDocumentProcessingService : IKdpDocumentProcessingService
    {
        private readonly IAlfrescoReadApi _alfrescoReadApi;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptions<MigrationOptions> _options;
        private readonly ILogger<KdpDocumentProcessingService> _logger;

        public KdpDocumentProcessingService(
            IAlfrescoReadApi alfrescoReadApi,
            IServiceScopeFactory scopeFactory,
            IOptions<MigrationOptions> options,
            ILogger<KdpDocumentProcessingService> logger)
        {
            _alfrescoReadApi = alfrescoReadApi ?? throw new ArgumentNullException(nameof(alfrescoReadApi));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Učitava sve KDP dokumente (00824 i 00099) iz Alfresca i puni staging tabelu
        /// </summary>
        public async Task<int> LoadKdpDocumentsToStagingAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Početak učitavanja KDP dokumenata iz Alfresca...");

            try
            {
                // Očisti staging tabelu
                await ClearStagingAsync(ct);

                // Učitaj sve KDP dokumente iz Alfresca
                var allKdpDocs = await LoadAllKdpDocumentsFromAlfrescoAsync(ct);

                _logger.LogInformation("Učitano {Count} KDP dokumenata iz Alfresca", allKdpDocs.Count);

                if (allKdpDocs.Count == 0)
                {
                    _logger.LogWarning("Nema KDP dokumenata za učitavanje");
                    return 0;
                }

                // Mapiranje Entry objekata u KdpDocumentStaging entitete
                var stagingDocuments = allKdpDocs.Select(MapToKdpDocumentStaging).ToList();

                // Bulk insert u staging tabelu (koristi InsertManyAsync iz base repository-a)
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var kdpStagingRepo = scope.ServiceProvider.GetRequiredService<IKdpDocumentStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var insertedCount = await kdpStagingRepo.InsertManyAsync(stagingDocuments, ct);
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    _logger.LogInformation("Uspešno upisano {Count} dokumenata u staging tabelu", insertedCount);

                    return insertedCount;
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Greška pri učitavanju KDP dokumenata");
                throw;
            }
        }

        /// <summary>
        /// Procesuira staging podatke pozivom sp_ProcessKdpDocuments
        /// </summary>
        public async Task<(int totalCandidates, int totalDocuments)> ProcessKdpDocumentsAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Pokretanje obrade KDP dokumenata (sp_ProcessKdpDocuments)...");

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var kdpExportRepo = scope.ServiceProvider.GetRequiredService<IKdpExportResultRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var result = await kdpExportRepo.ProcessKdpDocumentsAsync(ct);
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                    _logger.LogInformation(
                        "Obrada završena: {Candidates} kandidata, {Documents} dokumenata",
                        result.totalCandidates,
                        result.totalDocuments);

                    return result;
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Greška pri obradi KDP dokumenata");
                throw;
            }
        }

        /// <summary>
        /// Eksportuje rezultate u Excel fajl (placeholder za buduću implementaciju)
        /// </summary>
        public Task ExportToExcelAsync(string filePath, CancellationToken ct = default)
        {
            // TODO: Implementirati Excel export korišćenjem ClosedXML ili EPPlus
            // SELECT * FROM KdpExportResult -> Excel
            _logger.LogWarning("ExportToExcelAsync nije još implementirana");
            throw new NotImplementedException("Excel export će biti implementiran kasnije");
        }

        /// <summary>
        /// Briše staging tabelu
        /// </summary>
        public async Task ClearStagingAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Čišćenje staging tabele...");

            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var kdpStagingRepo = scope.ServiceProvider.GetRequiredService<IKdpDocumentStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                await kdpStagingRepo.ClearStagingAsync(ct);
                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                _logger.LogInformation("Staging tabela očišćena");
            }
            catch
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Vraća statistiku obrade
        /// </summary>
        public async Task<KdpProcessingStatistics> GetStatisticsAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Učitavanje statistike...");

            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var kdpStagingRepo = scope.ServiceProvider.GetRequiredService<IKdpDocumentStagingRepository>();
            var kdpExportRepo = scope.ServiceProvider.GetRequiredService<IKdpExportResultRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                var stagingCount = await kdpStagingRepo.CountAsync(ct);
                var exportCount = await kdpExportRepo.CountAsync(ct);

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                // TODO: Dodati detaljnije statistike ako je potrebno
                // (npr. COUNT po statusu, COUNT po tipu, MIN/MAX datum, itd.)

                return new KdpProcessingStatistics
                {
                    TotalDocumentsInStaging = stagingCount,
                    TotalCandidateFolders = exportCount,
                    TotalDocumentsInCandidateFolders = 0, // Može se izvući iz stored procedure rezultata
                    OldestDocumentDate = null,
                    NewestDocumentDate = null,
                    InactiveDocumentsCount = 0,
                    ActiveDocumentsCount = 0,
                    Type00824Count = 0,
                    Type00099Count = 0
                };
            }
            catch
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        // ============================================
        // PRIVATE HELPER METHODS
        // ============================================

        /// <summary>
        /// Učitava sve KDP dokumente iz Alfresca koristeći AFTS query
        /// </summary>
        private async Task<List<Entry>> LoadAllKdpDocumentsFromAlfrescoAsync(CancellationToken ct)
        {
            var kdpOptions = _options.Value.KdpProcessing;
            var batchSize = kdpOptions.BatchSize;
            var docTypes = kdpOptions.DocTypes ?? new List<string> { "00824", "00099" };

            // Build AFTS query for KDP documents
            var docTypeConditions = string.Join(" OR ", docTypes.Select(t => $"=ecm\\:docType:\"{t}\""));
            var query = $"({docTypeConditions}) AND TYPE:\"cm:content\"";

            // Add ANCESTOR condition if configured
            if (!string.IsNullOrWhiteSpace(kdpOptions.AncestorFolderId))
            {
                var ancestorId = kdpOptions.AncestorFolderId;

                // Ensure the ancestor ID is in the correct format (workspace://SpacesStore/xxx)
                if (!ancestorId.StartsWith("workspace://", StringComparison.OrdinalIgnoreCase))
                {
                    ancestorId = $"workspace://SpacesStore/{ancestorId}";
                }

                query += $" AND ANCESTOR:\"{ancestorId}\"";
                _logger.LogInformation("Using ANCESTOR filter: {AncestorId}", ancestorId);
            }

            var allDocs = new List<Entry>();
            int skipCount = 0;

            _logger.LogInformation("Početak AFTS query-ja za KDP dokumente: {Query}", query);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var request = new PostSearchRequest
                {
                    Query = new QueryRequest
                    {
                        Query = query,
                        Language = "afts"
                    },
                    Include = new[] { "properties", "path" },
                    Paging = new PagingRequest
                    {
                        MaxItems = batchSize,
                        SkipCount = skipCount
                    }
                };

                var response = await _alfrescoReadApi.SearchAsync(request, ct);
                var batch = response.List.Entries.Select(e => e.Entry).ToList();

                allDocs.AddRange(batch);

                _logger.LogInformation(
                    "Učitano {BatchCount} dokumenata (ukupno: {TotalCount})",
                    batch.Count,
                    allDocs.Count);

                // Ako je broj vraćenih dokumenata manji od batchSize, stigli smo do kraja
                if (batch.Count < batchSize)
                    break;

                skipCount += batchSize;
            }

            _logger.LogInformation("AFTS query završen - ukupno učitano {Count} dokumenata", allDocs.Count);

            return allDocs;
        }

        /// <summary>
        /// Mapira Alfresco Entry objekat u KdpDocumentStaging entitet
        /// </summary>
        private KdpDocumentStaging MapToKdpDocumentStaging(Entry entry)
        {
            var documentPath = entry.Path?.Name;
            var accFolderName = ExtractAccFolderFromPath(documentPath);
            var coreId = ExtractCoreId(accFolderName);

            // Extract CreatedDate from ecm:docCreationDate property
            var createdDate = GetDateTimePropertyValue(entry, "ecm:docCreationDate")
                              ?? GetDateTimePropertyValue(entry, "ecm:docCreatedDate")
                              ?? entry.CreatedAt.DateTime;

            return new KdpDocumentStaging
            {
                NodeId = entry.Id,
                DocumentName = entry.Name,
                DocumentPath = documentPath,
                ParentFolderId = entry.ParentId,
                ParentFolderName = ExtractParentFolderName(documentPath),
                DocumentType = GetPropertyValue(entry, "ecm:docType"),
                DocumentStatus = GetPropertyValue(entry, "ecm:docStatus"),
                CreatedDate = createdDate,
                AccountNumbers = GetPropertyValue(entry, "ecm:bnkAccountNumber"),
                AccFolderName = accFolderName,
                CoreId = coreId,
                ProcessedDate = DateTime.Now
            };
        }

        /// <summary>
        /// Ekstrahuje vrednost property-ja iz Entry objekta
        /// </summary>
        private string? GetPropertyValue(Entry entry, string propertyName)
        {
            if (entry.Properties == null)
                return null;

            if (entry.Properties.TryGetValue(propertyName, out var value))
            {
                return value?.ToString();
            }

            return null;
        }

        /// <summary>
        /// Ekstrahuje DateTime vrednost property-ja iz Entry objekta
        /// </summary>
        private DateTime? GetDateTimePropertyValue(Entry entry, string propertyName)
        {
            if (entry.Properties == null)
                return null;

            if (entry.Properties.TryGetValue(propertyName, out var value))
            {
                if (value is DateTime dt)
                    return dt;

                if (DateTime.TryParse(value?.ToString(), out var parsedDate))
                    return parsedDate;
            }

            return null;
        }

        /// <summary>
        /// Ekstrahuje ACC folder iz putanje
        /// Primer: /Company Home/Sites/bank/documentLibrary/ACC-123456/DOSSIERS-FL/... -> ACC-123456
        /// </summary>
        private string? ExtractAccFolderFromPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var match = Regex.Match(path, @"ACC-\d+");
            return match.Success ? match.Value : null;
        }

        /// <summary>
        /// Ekstrahuje Core ID iz ACC folder name
        /// Primer: ACC-123456 -> 123456
        /// </summary>
        private string? ExtractCoreId(string? accFolderName)
        {
            if (string.IsNullOrEmpty(accFolderName))
                return null;

            return accFolderName.Replace("ACC-", "");
        }

        /// <summary>
        /// Ekstrahuje naziv parent foldera iz putanje
        /// </summary>
        private string? ExtractParentFolderName(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var parts = path.Split('/');
            return parts.Length > 1 ? parts[^2] : null;
        }
    }
}
