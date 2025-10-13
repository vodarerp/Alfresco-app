using Migration.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    /// <summary>
    /// Interface for integrating with external Client API to retrieve client data.
    /// Used for enriching folder and document metadata during migration.
    /// </summary>
    public interface IClientApi
    {
        /// <summary>
        /// Retrieves comprehensive client data from the Client API based on Core ID.
        /// </summary>
        /// <param name="coreId">The client's Core ID (e.g., "10227858")</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Client data including MBR/JMBG, name, type, subtype, residency, segment</returns>
        Task<ClientData> GetClientDataAsync(string coreId, CancellationToken ct = default);

        /// <summary>
        /// Retrieves list of active account numbers for a client as of a specific date.
        /// Used for populating docAccountNumbers attribute for KDP documents (00099, 00824).
        /// </summary>
        /// <param name="coreId">The client's Core ID</param>
        /// <param name="asOfDate">Date to check for active accounts (typically document creation date)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>List of account numbers that were active on the specified date and not closed</returns>
        Task<List<string>> GetActiveAccountsAsync(string coreId, DateTime asOfDate, CancellationToken ct = default);

        /// <summary>
        /// Validates if a client exists in the system.
        /// </summary>
        /// <param name="coreId">The client's Core ID</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>True if client exists, false otherwise</returns>
        Task<bool> ValidateClientExistsAsync(string coreId, CancellationToken ct = default);
    }
}
