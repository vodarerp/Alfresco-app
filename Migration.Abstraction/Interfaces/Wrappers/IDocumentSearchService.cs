using Migration.Abstraction.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces.Wrappers
{
    /// <summary>
    /// Service for discovering documents directly by ecm:docType (MigrationByDocument mode).
    /// This is an alternative to FolderDiscovery + DocumentDiscovery flow.
    ///
    /// Flow:
    /// 1. Search documents by ecm:docType using AFTS query
    /// 2. Extract parent folders from document path
    /// 3. Insert unique folders into FolderStaging (ignore duplicates)
    /// 4. Apply document mapping and insert into DocStaging
    /// </summary>
    public interface IDocumentSearchService
    {
        /// <summary>
        /// Runs a single batch of document search and processing
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Result containing count of documents and folders processed</returns>
        Task<DocumentSearchBatchResult> RunBatchAsync(CancellationToken ct);

        /// <summary>
        /// Runs the document search loop until completion or cancellation
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>True if completed successfully, false otherwise</returns>
        Task<bool> RunLoopAsync(CancellationToken ct);

        /// <summary>
        /// Runs the document search loop with progress reporting
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <param name="progressCallback">Callback for progress updates</param>
        /// <returns>True if completed successfully, false otherwise</returns>
        Task<bool> RunLoopAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback);
    }

    /// <summary>
    /// Result of a single batch execution in DocumentSearchService
    /// </summary>
    public class DocumentSearchBatchResult
    {
        /// <summary>
        /// Number of documents found in this batch
        /// </summary>
        public int DocumentsFound { get; set; }

        /// <summary>
        /// Number of documents successfully inserted into DocStaging
        /// </summary>
        public int DocumentsInserted { get; set; }

        /// <summary>
        /// Number of unique folders found in this batch
        /// </summary>
        public int FoldersFound { get; set; }

        /// <summary>
        /// Number of folders inserted into FolderStaging (new folders only)
        /// </summary>
        public int FoldersInserted { get; set; }

        /// <summary>
        /// Indicates if there are more documents to process
        /// </summary>
        public bool HasMore { get; set; }

        /// <summary>
        /// List of errors encountered during processing
        /// </summary>
        public List<string> Errors { get; set; } = new();

        public DocumentSearchBatchResult() { }

        public DocumentSearchBatchResult(int documentsFound, int documentsInserted, int foldersFound, int foldersInserted, bool hasMore = false)
        {
            DocumentsFound = documentsFound;
            DocumentsInserted = documentsInserted;
            FoldersFound = foldersFound;
            FoldersInserted = foldersInserted;
            HasMore = hasMore;
        }
    }
}
