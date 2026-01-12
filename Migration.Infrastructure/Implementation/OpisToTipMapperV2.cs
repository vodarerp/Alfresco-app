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
    public class OpisToTipMapperV2 : IOpisToTipMapper
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public OpisToTipMapperV2(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        }

       
        public async Task<string> GetTipDokumentaAsync(string opisDokumenta, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return "UNKNOWN";

            await using var scope = _scopeFactory.CreateAsyncScope();
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

       
        public async Task<bool> IsKnownOpisAsync(string opisDokumenta, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return false;

            var tipDokumenta = await GetTipDokumentaAsync(opisDokumenta, ct).ConfigureAwait(false);
            return tipDokumenta != "UNKNOWN";
        }

        
        public async Task<IReadOnlyDictionary<string, string>> GetAllMappingsAsync(CancellationToken ct = default)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
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

        
        public async Task<DocumentMapping?> GetFullMappingAsync(string opisDokumenta,string docCode, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return null;

            DocumentMapping? mapping = null;
            await using var scope = _scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var mappingService = scope.ServiceProvider.GetRequiredService<IDocumentMappingService>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);
            try
            {
                if (!string.IsNullOrWhiteSpace(opisDokumenta))
                {
                    mapping = await mappingService.FindByOriginalNameAsync(opisDokumenta, ct).ConfigureAwait(false);

                }
                else if (!string.IsNullOrWhiteSpace(docCode) && docCode != "UNKNOWN")
                {
                    mapping = await mappingService.FindByOriginalCodeAsync(docCode, ct).ConfigureAwait(false);                    
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
