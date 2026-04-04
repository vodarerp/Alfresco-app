using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces.Wrappers
{
    public interface IPreviewExportService
    {
        /// <summary>
        /// Eksportuje PreviewDocStaging u zasebne .xlsx fajlove — jedan fajl po TargetDossierType.
        /// Sheet-ovi se dele na 500 000 redova. Fajlovi se kreiraju u outputDirectory.
        /// Filtrira po DossierType i TargetDossierType (null = sve).
        /// Vraća listu putanja kreiranih fajlova.
        /// </summary>
        Task<IList<string>> ExportAsync(
            string? dossierType,
            string? targetDossierType,
            string outputDirectory,
            CancellationToken ct = default);
    }
}
