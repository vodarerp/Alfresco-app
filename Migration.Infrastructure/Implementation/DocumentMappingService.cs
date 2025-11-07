using Alfresco.Contracts.Oracle.Models;
using Migration.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation
{
    /// <summary>
    /// Servis za mapiranje dokumenata iz DocumentMappings tabele.
    /// Zamenjuje statički HeimdallDocumentMapper sa asinhronym API-jem.
    /// Svi podaci se čitaju iz SQL Server tabele DocumentMappings.
    /// </summary>
    public class DocumentMappingService : IDocumentMappingService
    {
        private readonly IDocumentMappingRepository _repository;

        public DocumentMappingService(IDocumentMappingRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        /// <summary>
        /// Pronalazi mapping po originalnom imenu dokumenta (Naziv)
        /// </summary>
        public async Task<DocumentMapping?> FindByOriginalNameAsync(string originalName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(originalName))
                return null;

            return await _repository.FindByOriginalNameAsync(originalName, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Pronalazi mapping po originalnoj šifri dokumenta
        /// </summary>
        public async Task<DocumentMapping?> FindByOriginalCodeAsync(string originalCode, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(originalCode))
                return null;

            return await _repository.FindByOriginalCodeAsync(originalCode, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Pronalazi mapping po srpskom nazivu dokumenta
        /// </summary>
        public async Task<DocumentMapping?> FindBySerbianNameAsync(string serbianName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(serbianName))
                return null;

            return await _repository.FindBySerbianNameAsync(serbianName, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Pronalazi mapping po migriranom nazivu dokumenta
        /// </summary>
        public async Task<DocumentMapping?> FindByMigratedNameAsync(string migratedName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(migratedName))
                return null;

            return await _repository.FindByMigratedNameAsync(migratedName, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Da li će dokument dobiti sufiks migracija
        /// </summary>
        public async Task<bool> WillReceiveMigrationSuffixAsync(string originalName, CancellationToken ct = default)
        {
            var mapping = await FindByOriginalNameAsync(originalName, ct).ConfigureAwait(false);
            if (mapping == null || string.IsNullOrWhiteSpace(mapping.NazivDokumentaMigracija))
                return false;

            return mapping.NazivDokumentaMigracija.EndsWith("- migracija", StringComparison.OrdinalIgnoreCase) ||
                   mapping.NazivDokumentaMigracija.EndsWith("– migracija", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Da li će se šifra dokumenta promeniti
        /// </summary>
        public async Task<bool> CodeWillChangeAsync(string originalCode, CancellationToken ct = default)
        {
            var mapping = await FindByOriginalCodeAsync(originalCode, ct).ConfigureAwait(false);
            if (mapping == null)
                return false;

            return !string.Equals(
                mapping.SifraDokumenta?.Trim(),
                mapping.SifraDokumentaMigracija?.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Vraća migriranu šifru dokumenta
        /// </summary>
        public async Task<string> GetMigratedCodeAsync(string originalCode, CancellationToken ct = default)
        {
            var mapping = await FindByOriginalCodeAsync(originalCode, ct).ConfigureAwait(false);
            return mapping?.SifraDokumentaMigracija ?? originalCode;
        }

        /// <summary>
        /// Vraća migrirani naziv dokumenta
        /// </summary>
        public async Task<string> GetMigratedNameAsync(string originalName, CancellationToken ct = default)
        {
            var mapping = await FindByOriginalNameAsync(originalName, ct).ConfigureAwait(false);
            return mapping?.NazivDokumentaMigracija ?? originalName;
        }

        /// <summary>
        /// Vraća tip dosijea za dokument
        /// </summary>
        public async Task<string> GetDossierTypeAsync(string originalName, CancellationToken ct = default)
        {
            var mapping = await FindByOriginalNameAsync(originalName, ct).ConfigureAwait(false);
            return mapping?.TipDosijea ?? string.Empty;
        }

        /// <summary>
        /// Vraća srpski naziv dokumenta
        /// </summary>
        public async Task<string> GetSerbianNameAsync(string originalName, CancellationToken ct = default)
        {
            var mapping = await FindByOriginalNameAsync(originalName, ct).ConfigureAwait(false);
            return mapping?.NazivDokumenta ?? originalName;
        }

        /// <summary>
        /// Vraća sva mapiranja
        /// </summary>
        public async Task<IReadOnlyList<DocumentMapping>> GetAllMappingsAsync(CancellationToken ct = default)
        {
            return await _repository.GetAllMappingsAsync(ct).ConfigureAwait(false);
        }
    }
}
