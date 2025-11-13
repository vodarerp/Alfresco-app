using Alfresco.Contracts.Oracle.Models;
using Migration.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation
{
    /// <summary>
    /// Maps ecm:opisDokumenta (document description) to ecm:tipDokumenta (document type code).
    /// Supports both Serbian and English descriptions from old Alfresco system.
    ///
    /// VERZIJA 3.0: Koristi DocumentMappingService koji čita podatke iz SQL tabele DocumentMappings.
    /// Zamenjuje statički HeimdallDocumentMapper sa database-driven pristupom.
    /// </summary>
    public class OpisToTipMapperV2
    {
        private readonly IServiceProvider _serviceProvider;

        public OpisToTipMapperV2(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>
        /// Gets the document type code (ecm:tipDokumenta) from document description (ecm:opisDokumenta).
        /// NOVA LOGIKA: Koristi DocumentMappingService za mapiranje iz SQL tabele.
        /// </summary>
        /// <param name="opisDokumenta">Document description from Alfresco (ecm:docDesc)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Document type code (SifraDocMigracija) or "UNKNOWN" if not found</returns>
        public async Task<string> GetTipDokumentaAsync(string opisDokumenta, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return "UNKNOWN";

            await using var scope = _serviceProvider.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var mappingService = scope.ServiceProvider.GetRequiredService<IDocumentMappingService>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                // Try to find by original name (Naziv field)
                var mapping = await mappingService.FindByOriginalNameAsync(opisDokumenta, ct).ConfigureAwait(false);

                if (mapping != null && !string.IsNullOrWhiteSpace(mapping.SifraDokumentaMigracija))
                {
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                    return mapping.SifraDokumentaMigracija;
                }

                // Try to find by Serbian name (NazivDokumenta field)
                mapping = await mappingService.FindBySerbianNameAsync(opisDokumenta, ct).ConfigureAwait(false);

                if (mapping != null && !string.IsNullOrWhiteSpace(mapping.SifraDokumentaMigracija))
                {
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                    return mapping.SifraDokumentaMigracija;
                }

                // Try to find by migrated name (NazivDokumentaMigracija field) - supports "- migracija" suffix
                mapping = await mappingService.FindByMigratedNameAsync(opisDokumenta, ct).ConfigureAwait(false);

                if (mapping != null && !string.IsNullOrWhiteSpace(mapping.SifraDokumentaMigracija))
                {
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                    return mapping.SifraDokumentaMigracija;
                }

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                return "UNKNOWN";
            }
            catch(Exception ex)
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Checks if the given document description has a known mapping.
        /// NOVA LOGIKA: Koristi DocumentMappingService za proveru.
        /// </summary>
        /// <param name="opisDokumenta">Document description from Alfresco</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>True if mapping exists, false otherwise</returns>
        public async Task<bool> IsKnownOpisAsync(string opisDokumenta, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return false;

            var tipDokumenta = await GetTipDokumentaAsync(opisDokumenta, ct).ConfigureAwait(false);
            return tipDokumenta != "UNKNOWN";
        }

        /// <summary>
        /// Gets all registered mappings from DocumentMappingService (for debugging/testing purposes).
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Dictionary of all mappings (Naziv/NazivDokumenta → SifraDokumentaMigracija)</returns>
        public async Task<IReadOnlyDictionary<string, string>> GetAllMappingsAsync(CancellationToken ct = default)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var mappingService = scope.ServiceProvider.GetRequiredService<IDocumentMappingService>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var allMappings = await mappingService.GetAllMappingsAsync(ct).ConfigureAwait(false);

                foreach (var mapping in allMappings)
                {
                    if (string.IsNullOrWhiteSpace(mapping.SifraDokumentaMigracija))
                        continue;

                    // Add English name mapping
                    if (!string.IsNullOrWhiteSpace(mapping.Naziv) && !mappings.ContainsKey(mapping.Naziv))
                    {
                        mappings[mapping.Naziv] = mapping.SifraDokumentaMigracija;
                    }

                    // Add Serbian name mapping
                    if (!string.IsNullOrWhiteSpace(mapping.NazivDokumenta) && !mappings.ContainsKey(mapping.NazivDokumenta))
                    {
                        mappings[mapping.NazivDokumenta] = mapping.SifraDokumentaMigracija;
                    }

                    // Add migrated name mapping (with "- migracija" suffix)
                    if (!string.IsNullOrWhiteSpace(mapping.NazivDokumentaMigracija) && !mappings.ContainsKey(mapping.NazivDokumentaMigracija))
                    {
                        mappings[mapping.NazivDokumentaMigracija] = mapping.SifraDokumentaMigracija;
                    }
                }

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                return mappings;
            }
            catch
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Gets the full mapping info from DocumentMappingService for given document description.
        /// </summary>
        /// <param name="opisDokumenta">Document description from Alfresco</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Full mapping or null if not found</returns>
        public async Task<DocumentMapping?> GetFullMappingAsync(string opisDokumenta, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return null;

            await using var scope = _serviceProvider.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var mappingService = scope.ServiceProvider.GetRequiredService<IDocumentMappingService>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                // Try all search methods
                var mapping = await mappingService.FindByOriginalNameAsync(opisDokumenta, ct).ConfigureAwait(false);

                if (mapping == null)
                {
                    mapping = await mappingService.FindBySerbianNameAsync(opisDokumenta, ct).ConfigureAwait(false);
                }

                if (mapping == null)
                {
                    mapping = await mappingService.FindByMigratedNameAsync(opisDokumenta, ct).ConfigureAwait(false);
                }

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                return mapping;
            }
            catch
            {
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }
    }
}
