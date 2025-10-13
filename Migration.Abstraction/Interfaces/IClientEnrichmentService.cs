using Alfresco.Contracts.Oracle.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    /// <summary>
    /// Service for enriching folder and document metadata with client data from ClientAPI.
    /// Per documentation: Client data must be populated via ClientAPI call for new folders.
    /// </summary>
    public interface IClientEnrichmentService
    {
        /// <summary>
        /// Enriches folder with comprehensive client data from ClientAPI.
        /// Per documentation line 28-29: "Klijentske podatke na dosijeu (atributi dosijea)
        /// popuniti pozivom ClientAPI-a"
        /// </summary>
        /// <param name="folder">Folder to enrich (must have CoreId populated)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Enriched folder with client data</returns>
        Task<FolderStaging> EnrichFolderWithClientDataAsync(
            FolderStaging folder,
            CancellationToken ct = default);

        /// <summary>
        /// Enriches document with account numbers for KDP documents (00099, 00824).
        /// Per documentation line 123-129: "popuniti listu racuna koji su sada aktivni
        /// a bili su otvoreni na dan kreiranja dokumenta"
        /// </summary>
        /// <param name="document">Document to enrich (must have CoreId and OriginalCreatedAt)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Enriched document with account numbers in docAccountNumbers attribute</returns>
        Task<DocStaging> EnrichDocumentWithAccountsAsync(
            DocStaging document,
            CancellationToken ct = default);

        /// <summary>
        /// Validates if client exists before processing folder/document.
        /// Useful for pre-validation to avoid unnecessary processing.
        /// </summary>
        /// <param name="coreId">Client's Core ID</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>True if client exists in the system</returns>
        Task<bool> ValidateClientAsync(string coreId, CancellationToken ct = default);
    }
}
