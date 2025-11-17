using Alfresco.Contracts.Models;
using Migration.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    public interface IDocumentReader
    {
        /// <summary>
        /// Reads documents from a folder without pagination (loads ALL documents at once).
        /// WARNING: May cause OutOfMemory exception for folders with many documents.
        /// Consider using ReadBatchWithPaginationAsync instead.
        /// </summary>
        [Obsolete("Use ReadBatchWithPaginationAsync to prevent OutOfMemory exceptions")]
        public Task<IReadOnlyList<ListEntry>> ReadBatchAsync(string folderNodeId, CancellationToken ct);

        /// <summary>
        /// Reads documents from a folder with pagination support.
        /// Prevents OutOfMemory exceptions by limiting number of documents loaded at once.
        /// </summary>
        /// <param name="folderNodeId">The folder node ID</param>
        /// <param name="skipCount">Number of documents to skip (pagination offset)</param>
        /// <param name="maxItems">Maximum number of documents to return per page (default: 100)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Result with documents and pagination info</returns>
        public Task<DocumentReaderResult> ReadBatchWithPaginationAsync(string folderNodeId, int skipCount = 0, int maxItems = 100, CancellationToken ct = default);
    }
}
