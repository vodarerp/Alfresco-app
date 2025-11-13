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
    }
}
