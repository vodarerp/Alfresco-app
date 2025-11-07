using Alfresco.Contracts.Oracle.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    /// <summary>
    /// Servis za mapiranje dokumenata iz DocumentMappings tabele.
    /// Zamenjuje statički HeimdallDocumentMapper sa asinhronym API-jem.
    /// </summary>
    public interface IDocumentMappingService
    {
        /// <summary>
        /// Pronalazi mapping po originalnom imenu dokumenta (Naziv)
        /// </summary>
        Task<DocumentMapping?> FindByOriginalNameAsync(string originalName, CancellationToken ct = default);

        /// <summary>
        /// Pronalazi mapping po originalnoj šifri dokumenta
        /// </summary>
        Task<DocumentMapping?> FindByOriginalCodeAsync(string originalCode, CancellationToken ct = default);

        /// <summary>
        /// Pronalazi mapping po srpskom nazivu dokumenta
        /// </summary>
        Task<DocumentMapping?> FindBySerbianNameAsync(string serbianName, CancellationToken ct = default);

        /// <summary>
        /// Pronalazi mapping po migriranom nazivu dokumenta
        /// </summary>
        Task<DocumentMapping?> FindByMigratedNameAsync(string migratedName, CancellationToken ct = default);

        /// <summary>
        /// Da li će dokument dobiti sufiks migracija
        /// </summary>
        Task<bool> WillReceiveMigrationSuffixAsync(string originalName, CancellationToken ct = default);

        /// <summary>
        /// Da li će se šifra dokumenta promeniti
        /// </summary>
        Task<bool> CodeWillChangeAsync(string originalCode, CancellationToken ct = default);

        /// <summary>
        /// Vraća migriranu šifru dokumenta
        /// </summary>
        Task<string> GetMigratedCodeAsync(string originalCode, CancellationToken ct = default);

        /// <summary>
        /// Vraća migrirani naziv dokumenta
        /// </summary>
        Task<string> GetMigratedNameAsync(string originalName, CancellationToken ct = default);

        /// <summary>
        /// Vraća tip dosijea za dokument
        /// </summary>
        Task<string> GetDossierTypeAsync(string originalName, CancellationToken ct = default);

        /// <summary>
        /// Vraća srpski naziv dokumenta
        /// </summary>
        Task<string> GetSerbianNameAsync(string originalName, CancellationToken ct = default);

        /// <summary>
        /// Vraća sva mapiranja
        /// </summary>
        Task<IReadOnlyList<DocumentMapping>> GetAllMappingsAsync(CancellationToken ct = default);
    }
}
