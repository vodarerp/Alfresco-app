using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Models;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Document
{
    public class DocumentReader : IDocumentReader
    {
        private readonly IAlfrescoReadApi _read;

        public DocumentReader(IAlfrescoReadApi read)
        {
            _read = read;
        }

        /// <summary>
        /// Reads documents from a folder without pagination (loads ALL documents at once).
        /// WARNING: May cause OutOfMemory exception for folders with many documents.
        /// </summary>
        [Obsolete("Use ReadBatchWithPaginationAsync to prevent OutOfMemory exceptions")]
        public async Task<IReadOnlyList<ListEntry>> ReadBatchAsync(string folderNodeId, CancellationToken ct)
        {
            var docs = (await _read.GetNodeChildrenAsync(folderNodeId, ct).ConfigureAwait(false)).List?.Entries ?? new();
            return docs;
        }

        /// <summary>
        /// Reads documents from a folder with pagination support.
        /// Prevents OutOfMemory exceptions by limiting number of documents loaded at once.
        /// </summary>
        public async Task<DocumentReaderResult> ReadBatchWithPaginationAsync(
            string folderNodeId,
            int skipCount = 0,
            int maxItems = 100,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(folderNodeId))
            {
                return DocumentReaderResult.Empty(skipCount, maxItems);
            }

            // Call Alfresco API with pagination parameters
            var response = await _read.GetNodeChildrenAsync(folderNodeId, skipCount, maxItems, ct).ConfigureAwait(false);

            if (response?.List == null)
            {
                return DocumentReaderResult.Empty(skipCount, maxItems);
            }

            var entries = response.List.Entries ?? new List<ListEntry>();
            var pagination = response.List.Pagination;

            // If pagination info is available, use it
            if (pagination != null)
            {
                return DocumentReaderResult.Create(
                    documents: entries,
                    totalCount: pagination.TotalItems,
                    skipCount: pagination.SkipCount,
                    maxItems: pagination.MaxItems
                );
            }

            // Fallback: no pagination info, use heuristic
            return DocumentReaderResult.Create(
                documents: entries,
                totalCount: null,
                skipCount: skipCount,
                maxItems: maxItems
            );
        }
    }
}
