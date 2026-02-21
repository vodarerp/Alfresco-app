using Alfresco.Contracts.Oracle.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SqlServer.Abstraction.Interfaces
{
    /// <summary>
    /// Repository za pristup DocumentMappings tabeli
    /// Zamenjuje statički HeimdallDocumentMapper
    /// </summary>
    public interface IDocumentMappingRepository : IRepository<DocumentMapping, int>
    {
        /// <summary>
        /// Pronalazi mapping po originalnom imenu dokumenta (NAZIV polje)
        /// </summary>
        Task<DocumentMapping?> FindByOriginalNameAsync(string originalName, CancellationToken ct = default);

        /// <summary>
        /// Pronalazi mapping po originalnoj šifri dokumenta (sifraDokumenta polje)
        /// </summary>
        Task<DocumentMapping?> FindByOriginalCodeAsync(string originalCode, CancellationToken ct = default);

        /// <summary>
        /// Pronalazi mapping po srpskom nazivu dokumenta (NazivDokumenta polje)
        /// </summary>
        Task<DocumentMapping?> FindBySerbianNameAsync(string serbianName, CancellationToken ct = default);

        /// <summary>
        /// Pronalazi mapping po migriranom nazivu dokumenta (NazivDokumentaMigracija polje)
        /// </summary>
        Task<DocumentMapping?> FindByMigratedNameAsync(string migratedName, CancellationToken ct = default);

        /// <summary>
        /// Vraća sva mapiranja iz tabele (keširano)
        /// </summary>
        Task<IReadOnlyList<DocumentMapping>> GetAllMappingsAsync(CancellationToken ct = default);

        /// <summary>
        /// Vraća straničeni rezultat sa pretrakom
        /// </summary>
        /// <param name="searchText">Tekst za pretragu (NAZIV, NazivDokumenta, sifraDokumenta, TipDosijea)</param>
        /// <param name="pageNumber">Broj stranice (počinje od 1)</param>
        /// <param name="pageSize">Broj zapisa po stranici</param>
        /// <param name="tipDosijea">Filter po tipu dosijea (null = svi)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Lista dokumenata i ukupan broj zapisa</returns>
        Task<(IReadOnlyList<DocumentMapping> Items, int TotalCount)> SearchWithPagingAsync(
            string? searchText,
            int pageNumber,
            int pageSize,
            string? tipDosijea = null,
            CancellationToken ct = default);

        /// <summary>
        /// Vraća distinct vrednosti TipDosijea kolone
        /// </summary>
        Task<IReadOnlyList<string>> GetDistinctTipDosijeaAsync(CancellationToken ct = default);
    }
}
