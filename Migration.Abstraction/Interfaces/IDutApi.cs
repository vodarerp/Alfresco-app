using Migration.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    /// <summary>
    /// Interface for integrating with DUT (Deposit Understanding/Processing) API.
    /// Used for retrieving deposit-related data from OfferBO table for migration validation and enrichment.
    ///
    /// Per documentation: Migration should only process documents with status "Booked" in DUT OfferBO table.
    /// </summary>
    public interface IDutApi
    {
        /// <summary>
        /// Retrieves all booked deposit offers for a specific client.
        /// Used to match documents with deposit contracts during migration.
        /// </summary>
        /// <param name="coreId">Client's Core ID</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>List of booked offers with contract numbers, dates, amounts</returns>
        Task<List<DutOffer>> GetBookedOffersAsync(string coreId, CancellationToken ct = default);

        /// <summary>
        /// Retrieves detailed information about a specific offer.
        /// </summary>
        /// <param name="offerId">Unique offer identifier</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Detailed offer information</returns>
        Task<DutOfferDetails> GetOfferDetailsAsync(string offerId, CancellationToken ct = default);

        /// <summary>
        /// Retrieves all documents associated with a specific deposit offer.
        /// Used to validate document migration and version tracking.
        /// </summary>
        /// <param name="offerId">Unique offer identifier</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>List of documents with their versions and signatures</returns>
        Task<List<DutDocument>> GetOfferDocumentsAsync(string offerId, CancellationToken ct = default);

        /// <summary>
        /// Matches documents to offers based on Core ID, deposit date, and contract number.
        /// Used when Alfresco doesn't have contract number but needs to match documents.
        /// </summary>
        /// <param name="coreId">Client's Core ID</param>
        /// <param name="depositDate">Date of deposit</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>List of offers matching the criteria</returns>
        Task<List<DutOffer>> FindOffersByDateAsync(string coreId, DateTime depositDate, CancellationToken ct = default);

        /// <summary>
        /// Validates if an offer exists and has "Booked" status.
        /// Per documentation: Only "Booked" offers should be migrated.
        /// </summary>
        /// <param name="offerId">Unique offer identifier</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>True if offer exists and is booked, false otherwise</returns>
        Task<bool> IsOfferBookedAsync(string offerId, CancellationToken ct = default);
    }
}
