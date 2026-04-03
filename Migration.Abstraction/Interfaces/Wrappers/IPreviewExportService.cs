using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces.Wrappers
{
    public interface IPreviewExportService
    {
        /// <summary>
        /// Eksportuje PreviewDocStaging u .xlsx fajl, sheet po sheet za svaki TargetDossierType.
        /// Filtrira po DossierType i TargetDossierType (null = sve).
        /// Vraća putanju do generisanog fajla.
        /// </summary>
        Task<string> ExportAsync(
            string? dossierType,
            string? targetDossierType,
            string outputPath,
            CancellationToken ct = default);
    }
}
