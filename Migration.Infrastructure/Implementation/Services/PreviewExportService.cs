using Alfresco.Contracts.Oracle.Models;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces.Wrappers;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
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
            string? documentType,
            string outputPath,
            CancellationToken ct = default)
        {
            _uiLogger.LogInformation("PreviewExportService: Pokretanje eksporta...");

            IList<PreviewDocStaging> all;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                await uow.BeginAsync(ct: ct).ConfigureAwait(false);
                try
                {
                    var result = await repo.GetForExportAsync(dossierType, documentType, ct).ConfigureAwait(false);
                    all = result.ToList();
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                }
                catch
                {
                    await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                    throw;
                }
            }

            _fileLogger.LogInformation(
                "PreviewExportService: Dohvaceno {Count} zapisa za eksport.", all.Count);

            var piRows = all.Where(r => r.DossierType == "PI").ToList();
            var leRows = all.Where(r => r.DossierType == "LE").ToList();

            using var wb = new XLWorkbook();

            WriteSheet(wb, "PI", piRows);
            WriteSheet(wb, "LE", leRows);

            wb.SaveAs(outputPath);

            _uiLogger.LogInformation(
                "PreviewExportService: Eksport zavrsen. PI={PI}, LE={LE}. Putanja: {Path}",
                piRows.Count, leRows.Count, outputPath);

            return outputPath;
        }

        private static void WriteSheet(XLWorkbook wb, string sheetName, IList<PreviewDocStaging> rows)
        {
            var ws = wb.Worksheets.Add(sheetName);

            // Header
            var headers = new[]
            {
                "Id", "NodeId", "Name", "NodeType", "ParentId", "ParentFolderName",
                "DocDescription", "OriginalDocumentCode", "NewDocumentCode",
                "OldAlfrescoStatus", "NewAlfrescoStatus", "IsActive",
                "DocumentType", "DocumentTypeMigration", "DossierType", "TargetDossierType",
                "DossierDestinationFolderId", "DossierDestinationFolderName", "DossierDestinationFolderIsCreated",
                "Status", "CoreId", "ClientSegment", "Source",
                "CategoryCode", "CategoryName", "ContractNumber", "ProductType", "AccountNumbers",
                "OriginalCreatedAt", "NewDocumentName", "OriginalDocumentName", "FinalDocumentType",
                "RecordInserted", "RecordExportedMigration",
                "ClientApiMbrJmbg", "ClientApiClientName", "ClientApiClientType", "ClientApiClientSubtype",
                "ClientApiResidency", "ClientApiSegment", "ClientApiStaff", "ClientApiOpuUser",
                "ClientApiOpuRealization", "ClientApiBarclex", "ClientApiCollaborator",
                "ClientApiBarCLEXName", "ClientApiBarCLEXOpu", "ClientApiBarCLEXGroupName",
                "ClientApiBarCLEXGroupCode", "ClientApiBarCLEXCode"
            };

            for (int c = 0; c < headers.Length; c++)
                ws.Cell(1, c + 1).Value = headers[c];

            // Style header row
            var headerRow = ws.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#2E75B6");
            headerRow.Style.Font.FontColor = XLColor.White;

            // Data rows
            for (int r = 0; r < rows.Count; r++)
            {
                var doc = rows[r];
                var row = r + 2;
                int c = 1;

                ws.Cell(row, c++).Value = doc.Id;
                ws.Cell(row, c++).Value = doc.NodeId;
                ws.Cell(row, c++).Value = doc.Name;
                ws.Cell(row, c++).Value = doc.NodeType;
                ws.Cell(row, c++).Value = doc.ParentId;
                ws.Cell(row, c++).Value = doc.ParentFolderName;
                ws.Cell(row, c++).Value = doc.DocDescription;
                ws.Cell(row, c++).Value = doc.OriginalDocumentCode;
                ws.Cell(row, c++).Value = doc.NewDocumentCode;
                ws.Cell(row, c++).Value = doc.OldAlfrescoStatus;
                ws.Cell(row, c++).Value = doc.NewAlfrescoStatus;
                ws.Cell(row, c++).Value = doc.IsActive;
                ws.Cell(row, c++).Value = doc.DocumentType;
                ws.Cell(row, c++).Value = doc.DocumentTypeMigration;
                ws.Cell(row, c++).Value = doc.DossierType;
                ws.Cell(row, c++).Value = doc.TargetDossierType;
                ws.Cell(row, c++).Value = doc.DossierDestinationFolderId;
                ws.Cell(row, c++).Value = doc.DossierDestinationFolderName;
                ws.Cell(row, c++).Value = doc.DossierDestinationFolderIsCreated;
                ws.Cell(row, c++).Value = doc.Status;
                ws.Cell(row, c++).Value = doc.CoreId;
                ws.Cell(row, c++).Value = doc.ClientSegment;
                ws.Cell(row, c++).Value = doc.Source;
                ws.Cell(row, c++).Value = doc.CategoryCode;
                ws.Cell(row, c++).Value = doc.CategoryName;
                ws.Cell(row, c++).Value = doc.ContractNumber;
                ws.Cell(row, c++).Value = doc.ProductType;
                ws.Cell(row, c++).Value = doc.AccountNumbers;
                ws.Cell(row, c++).Value = doc.OriginalCreatedAt;
                ws.Cell(row, c++).Value = doc.NewDocumentName;
                ws.Cell(row, c++).Value = doc.OriginalDocumentName;
                ws.Cell(row, c++).Value = doc.FinalDocumentType;
                ws.Cell(row, c++).Value = doc.RecordInserted;
                ws.Cell(row, c++).Value = doc.RecordExportedMigration;
                ws.Cell(row, c++).Value = doc.ClientApiMbrJmbg;
                ws.Cell(row, c++).Value = doc.ClientApiClientName;
                ws.Cell(row, c++).Value = doc.ClientApiClientType;
                ws.Cell(row, c++).Value = doc.ClientApiClientSubtype;
                ws.Cell(row, c++).Value = doc.ClientApiResidency;
                ws.Cell(row, c++).Value = doc.ClientApiSegment;
                ws.Cell(row, c++).Value = doc.ClientApiStaff;
                ws.Cell(row, c++).Value = doc.ClientApiOpuUser;
                ws.Cell(row, c++).Value = doc.ClientApiOpuRealization;
                ws.Cell(row, c++).Value = doc.ClientApiBarclex;
                ws.Cell(row, c++).Value = doc.ClientApiCollaborator;
                ws.Cell(row, c++).Value = doc.ClientApiBarCLEXName;
                ws.Cell(row, c++).Value = doc.ClientApiBarCLEXOpu;
                ws.Cell(row, c++).Value = doc.ClientApiBarCLEXGroupName;
                ws.Cell(row, c++).Value = doc.ClientApiBarCLEXGroupCode;
                ws.Cell(row, c).Value   = doc.ClientApiBarCLEXCode;

                // Alternating row color
                if (r % 2 == 1)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF3F8");
            }

            // Auto-fit columns (cap at 60 width)
            ws.Columns().AdjustToContents(1, 60);

            // Freeze header + enable auto-filter
            ws.SheetView.FreezeRows(1);
            if (rows.Count > 0)
                ws.RangeUsed()?.SetAutoFilter();
        }
    }
}
