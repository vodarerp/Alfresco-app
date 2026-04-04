using Alfresco.Contracts.Oracle.Models;
using MiniExcelLibs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces.Wrappers;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    public class PreviewExportService : IPreviewExportService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _fileLogger;
        private readonly ILogger _uiLogger;

        private const int MaxRowsPerSheet = 500_000;

        public PreviewExportService(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _fileLogger   = loggerFactory.CreateLogger("FileLogger");
            _uiLogger     = loggerFactory.CreateLogger("UiLogger");
        }

        public async Task<IList<string>> ExportAsync(
            string? dossierType,
            string? targetDossierType,
            string outputDirectory,
            CancellationToken ct = default)
        {
            _uiLogger.LogInformation("PreviewExportService: Pokretanje eksporta...");

            // Korak 1: count po TargetDossierType
            IList<(string? Type, long Count)> typeCounts;
            await using (var typeScope = _scopeFactory.CreateAsyncScope())
            {
                var typeUow  = typeScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var typeRepo = typeScope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                await typeUow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var all = await typeRepo.GetExportTargetTypeCountsAsync(dossierType, ct).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(targetDossierType))
                    {
                        var filtered = new List<(string?, long)>();
                        foreach (var (t, c) in all)
                            if (string.Equals(t, targetDossierType, StringComparison.OrdinalIgnoreCase))
                                filtered.Add((t, c));
                        if (filtered.Count == 0)
                            filtered.Add((targetDossierType, 0));
                        typeCounts = filtered;
                    }
                    else
                    {
                        typeCounts = all;
                    }

                    await typeUow.CommitAsync(ct: ct).ConfigureAwait(false);
                }
                catch
                {
                    await typeUow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }

            _fileLogger.LogInformation(
                "PreviewExportService: Tipovi za eksport: {Summary}",
                FormatTypeSummary(typeCounts));

            Directory.CreateDirectory(outputDirectory);

            var timestamp    = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var createdFiles = new List<string>();

            // Korak 2: jedan fajl po TargetDossierType
            foreach (var (type, count) in typeCounts)
            {
                var baseSheetName = string.IsNullOrWhiteSpace(type) ? "Other" : type;
                var safeTypeName  = string.IsNullOrWhiteSpace(type) ? "Other" : MakeSafeFileName(type);
                var fileName      = $"PreviewExport_{safeTypeName}_{timestamp}.xlsx";
                var outputPath    = Path.Combine(outputDirectory, fileName);

                var sheets = new Dictionary<string, object>();

                if (count <= MaxRowsPerSheet)
                {
                    var capturedType = type;
                    sheets[baseSheetName] = CreateSheetStream(dossierType, capturedType);
                }
                else
                {
                    int totalParts = (int)Math.Ceiling((double)count / MaxRowsPerSheet);
                    _fileLogger.LogInformation(
                        "PreviewExportService: Tip {Type} ima {Count} redova — deli se u {Parts} sheet-ova.",
                        baseSheetName, count, totalParts);

                    for (int part = 1; part <= totalParts; part++)
                    {
                        long partOffset  = (long)(part - 1) * MaxRowsPerSheet;
                        var partName     = $"{baseSheetName} ({part}/{totalParts})";
                        var capturedType = type;
                        var capturedOff  = partOffset;
                        sheets[partName] = CreateSheetStreamPaged(dossierType, capturedType, capturedOff, MaxRowsPerSheet);
                    }
                }

                var tempPath = outputPath + ".tmp";
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);

                    MiniExcel.SaveAs(tempPath, sheets, overwriteFile: true, excelType: ExcelType.XLSX);

                    if (File.Exists(outputPath))
                        File.Delete(outputPath);
                    File.Move(tempPath, outputPath);

                    createdFiles.Add(outputPath);
                    _uiLogger.LogInformation("PreviewExportService: Kreiran fajl: {FileName}", fileName);
                }
                catch
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    throw;
                }
            }

            // Edge case: nema podataka
            if (createdFiles.Count == 0)
            {
                var emptyFile = Path.Combine(outputDirectory, $"PreviewExport_Empty_{timestamp}.xlsx");
                var tempPath  = emptyFile + ".tmp";
                try
                {
                    MiniExcel.SaveAs(tempPath,
                        new Dictionary<string, object> { ["Empty"] = Array.Empty<PreviewDocStaging>() },
                        overwriteFile: true, excelType: ExcelType.XLSX);
                    File.Move(tempPath, emptyFile);
                    createdFiles.Add(emptyFile);
                }
                catch
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    throw;
                }
            }

            _uiLogger.LogInformation(
                "PreviewExportService: Eksport zavrsen. Kreirano {Count} fajl(ova) u {Dir}.",
                createdFiles.Count, outputDirectory);

            return createdFiles;
        }

        private IEnumerable<PreviewDocStaging> CreateSheetStream(string? dossierType, string? targetDossierType)
        {
            using var scope = _scopeFactory.CreateScope();
            var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

            uow.BeginAsync().GetAwaiter().GetResult();

            foreach (var row in repo.GetForExportUnbuffered(dossierType, targetDossierType))
                yield return row;

            uow.CommitAsync().GetAwaiter().GetResult();
        }

        private IEnumerable<PreviewDocStaging> CreateSheetStreamPaged(
            string? dossierType, string? targetDossierType, long offset, int pageSize)
        {
            using var scope = _scopeFactory.CreateScope();
            var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

            uow.BeginAsync().GetAwaiter().GetResult();

            foreach (var row in repo.GetForExportUnbufferedPaged(dossierType, targetDossierType, offset, pageSize))
                yield return row;

            uow.CommitAsync().GetAwaiter().GetResult();
        }

        private static string MakeSafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        private static string FormatTypeSummary(IList<(string? Type, long Count)> typeCounts)
        {
            var parts = new List<string>();
            foreach (var (type, count) in typeCounts)
            {
                var name    = string.IsNullOrWhiteSpace(type) ? "Other" : type;
                var nSheets = count > MaxRowsPerSheet
                    ? (int)Math.Ceiling((double)count / MaxRowsPerSheet)
                    : 1;
                parts.Add(nSheets > 1 ? $"{name}={count}→{nSheets}sh" : $"{name}={count}");
            }
            return string.Join(", ", parts);
        }
    }
}
