using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces.Wrappers
{
    public interface IPreviewExportService
    {
        /// <summary>
        /// Eksportuje PreviewDocStaging u .xlsx fajl sa dva sheet-a: PI i LE.
        /// Filtrira po DossierType i DocumentType (null = sve).
        /// Vraća putanju do generisanog fajla.
        /// </summary>
        Task<string> ExportAsync(
            string? dossierType,
            string? documentType,
            string outputPath,
            CancellationToken ct = default);
    }
}
