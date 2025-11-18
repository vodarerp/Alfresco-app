using Alfresco.Contracts.Oracle.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    /// <summary>
    /// Maps ecm:opisDokumenta (document description) to ecm:tipDokumenta (document type code).
    /// Supports both Serbian and English descriptions from old Alfresco system.
    /// </summary>
    public interface IOpisToTipMapper
    {
        /// <summary>
        /// Gets the document type code (ecm:tipDokumenta) from document description (ecm:opisDokumenta).
        /// </summary>
        /// <param name="opisDokumenta">Document description from Alfresco (ecm:docDesc)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Document type code (SifraDocMigracija) or "UNKNOWN" if not found</returns>
        Task<string> GetTipDokumentaAsync(string opisDokumenta, CancellationToken ct = default);

        /// <summary>
        /// Checks if the given document description has a known mapping.
        /// </summary>
        /// <param name="opisDokumenta">Document description from Alfresco</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>True if mapping exists, false otherwise</returns>
        Task<bool> IsKnownOpisAsync(string opisDokumenta, CancellationToken ct = default);

        /// <summary>
        /// Gets all registered mappings from DocumentMappingService (for debugging/testing purposes).
        /// </summary>
        /// <param name="opisDokumenta">Document description from Alfresco</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Dictionary of all mappings (Naziv/NazivDokumenta → SifraDokumentaMigracija)</returns>
        Task<IReadOnlyDictionary<string, string>> GetAllMappingsAsync(CancellationToken ct = default);

        /// <summary>
        /// Gets the full mapping info from DocumentMappingService for given document description.
        /// </summary>
        /// <param name="opisDokumenta">Document description from Alfresco</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Full mapping or null if not found</returns>
        Task<DocumentMapping?> GetFullMappingAsync(string opisDokumenta, CancellationToken ct = default);
    }
}
