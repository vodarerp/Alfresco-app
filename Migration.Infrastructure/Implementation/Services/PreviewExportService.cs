using Alfresco.Contracts.Oracle.Models;
using MiniExcelLibs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces.Wrappers;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    public class PreviewExportService : IPreviewExportService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _fileLogger;
        private readonly ILogger _uiLogger;

        public PreviewExportService(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _fileLogger   = loggerFactory.CreateLogger("FileLogger");
            _uiLogger     = loggerFactory.CreateLogger("UiLogger");
        }

        public async Task<string> ExportAsync(
            string? dossierType,
            string? targetDossierType,
            string outputPath,
            CancellationToken ct = default)
        {
            _uiLogger.LogInformation("PreviewExportService: Pokretanje eksporta (streaming)...");

            // Korak 1: lightweight query za distinct TargetDossierType vrednosti (= sheet nazivi)
            IList<string?> types;
            if (!string.IsNullOrWhiteSpace(targetDossierType))
            {
                types = new List<string?> { targetDossierType };
            }
            else
            {
                await using var typeScope = _scopeFactory.CreateAsyncScope();
                var typeUow  = typeScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var typeRepo = typeScope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                await typeUow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    types = await typeRepo.GetDistinctExportTargetTypesAsync(dossierType, ct).ConfigureAwait(false);
                    await typeUow.CommitAsync(ct: ct).ConfigureAwait(false);
                }
                catch
                {
                    await typeUow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }

            _fileLogger.LogInformation(
                "PreviewExportService: Pronadjeno {Count} sheet-ova: {Types}",
                types.Count, string.Join(", ", types));

            // Korak 2 + 3: jedan scope, BeginAsync PRE GetForExportUnbuffered.
            // GetForExportUnbuffered pristupa Conn u trenutku poziva — mora biti otvoren.
            // Dapper vraca lazy IEnumerable<T>, SQL se izvrsava tek kada MiniExcel pocne iteraciju.
            // MiniExcel procesira sheet-ove sekvencijalno, pa je samo jedan Dapper reader aktivan u isto vreme.
            await using var exportScope = _scopeFactory.CreateAsyncScope();
            var exportUow  = exportScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var exportRepo = exportScope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

            await exportUow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                var sheets = new Dictionary<string, object>();
                foreach (var type in types)
                {
                    var sheetName    = string.IsNullOrWhiteSpace(type) ? "Other" : type;
                    var capturedType = type;
                    sheets[sheetName] = exportRepo.GetForExportUnbuffered(dossierType, capturedType);
                }

                if (sheets.Count == 0)
                    sheets["Empty"] = Array.Empty<PreviewDocStaging>();

                // SaveAs (sync) — sheets sadrze sync IEnumerable<PreviewDocStaging> (POCO).
                // MiniExcel streama POCO tip red po red bez internog bufferinga u List<T>.
                MiniExcel.SaveAs(outputPath, sheets, overwriteFile: true);

                await exportUow.CommitAsync(ct: ct).ConfigureAwait(false);
            }
            catch
            {
                await exportUow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }

            _uiLogger.LogInformation(
                "PreviewExportService: Eksport zavrsen. Putanja: {Path}", outputPath);

            return outputPath;
        }
    }
}
