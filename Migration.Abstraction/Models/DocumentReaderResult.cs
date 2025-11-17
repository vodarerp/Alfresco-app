using Alfresco.Contracts.Models;
using System.Collections.Generic;

namespace Migration.Abstraction.Models
{
    /// <summary>
    /// Result of reading documents from a folder with pagination support.
    /// Prevents OutOfMemory exceptions by limiting number of documents loaded at once.
    /// </summary>
    public class DocumentReaderResult
    {
        /// <summary>
        /// List of documents read from the folder.
        /// </summary>
        public IReadOnlyList<ListEntry> Documents { get; init; } = new List<ListEntry>();

        /// <summary>
        /// Total number of documents in the folder (if available from API).
        /// Null if total count is not available.
        /// </summary>
        public long? TotalCount { get; init; }

        /// <summary>
        /// Number of documents skipped (pagination offset).
        /// </summary>
        public int SkipCount { get; init; }

        /// <summary>
        /// Maximum number of documents requested per page.
        /// </summary>
        public int MaxItems { get; init; }

        /// <summary>
        /// Indicates if there are more documents to fetch.
        /// True = call ReadBatchAsync again with increased skipCount.
        /// False = no more documents, pagination complete.
        /// </summary>
        public bool HasMore { get; init; }

        /// <summary>
        /// Creates a result with no documents (empty folder or end of pagination).
        /// </summary>
        public static DocumentReaderResult Empty(int skipCount, int maxItems)
        {
            return new DocumentReaderResult
            {
                Documents = new List<ListEntry>(),
                TotalCount = 0,
                SkipCount = skipCount,
                MaxItems = maxItems,
                HasMore = false
            };
        }

        /// <summary>
        /// Creates a result from documents and pagination info.
        /// </summary>
        public static DocumentReaderResult Create(
            IReadOnlyList<ListEntry> documents,
            long? totalCount,
            int skipCount,
            int maxItems)
        {
            var hasMore = totalCount.HasValue
                ? (skipCount + documents.Count) < totalCount.Value
                : documents.Count >= maxItems; // Heuristic: if we got full page, assume more exist

            return new DocumentReaderResult
            {
                Documents = documents,
                TotalCount = totalCount,
                SkipCount = skipCount,
                MaxItems = maxItems,
                HasMore = hasMore
            };
        }
    }
}
