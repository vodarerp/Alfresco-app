using System;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    /// <summary>
    /// Direct database reader for Alfresco database queries
    /// </summary>
    public interface IAlfrescoDbReader
    {
        /// <summary>
        /// Counts total folders in Alfresco DB matching the discovery criteria
        /// </summary>
        /// <param name="rootFolderUuid">Root folder UUID (e.g., '8ccc0f18-5445-4358-8c0f-185445235836')</param>
        /// <param name="nameFilter">Name filter pattern (e.g., '-' for names containing dash)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Total count of folders, or -1 if database is not configured</returns>
        Task<long> CountTotalFoldersAsync(string rootFolderUuid, string nameFilter, CancellationToken ct);
    }
}
