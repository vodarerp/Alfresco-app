
using Alfresco.Contracts.Models;

namespace Migration.Abstraction.Models
{
    public sealed record FolderSource(string id, string name, string nodeID, string fullPath);
    public sealed record ScanRequest(string rootId, string nameFilter, int skip, int take);

    public sealed record DestinationFolder(string id, string name, string parentId, string fullPath);
    public sealed record FolderIngestorItem(string srcFolderId, string srcFolderName, string srcRootId, string destFolderId, string destRootId);



    public sealed record FolderDiscoverRequest(string SrcRootId, string DestRootId, string NameFilter, int PageSize);
    public sealed record DocumentDiscoverRequest(string SrcRootId);

    public sealed record MoveRequest(string DocumentId, string DestFolderId, string? NewDocumentName);
    //---------------------------------------------

    public sealed record FolderDiscoveryBatchRequest(int Take, FolderReaderRequest FolderRequest);
    public sealed record DocumentDiscoveryBatchRequest(int Take, string RootDestinationFolder);

    public sealed record MoveBatchRequest(int Take, int defreeOfParralelism);
    public sealed record MoveLoopOptions(MoveBatchRequest Batch, TimeSpan IdleDelay);


    public sealed record FolderDiscoveryLoopOptions(FolderDiscoveryBatchRequest Batch, TimeSpan IdleDelay);
    public sealed record DocumentDiscoveryLoopOptions(DocumentDiscoveryBatchRequest Batch, TimeSpan IdleDelay);


    public sealed record MoveBatchResult(int Done, int Failed);
    public sealed record FolderBatchResult(int InsertedCount);
    public sealed record DocumentBatchResult(int PlannedCount);

    public sealed record MoveExecutorRequest(string DocumentId, string DestFolderId, string? NewDocumentName);

    public sealed record FolderReaderRequest(string RootId, string NameFilter, int Skip, int Take, FolderSeekCursor? Cursor, List<string>? TargetCoreIds = null);

    public sealed record FolderReaderResult(IReadOnlyList<ListEntry> Items, FolderSeekCursor? Next, bool? HasMoreItems = null)
    {
        /// <summary>
        /// Indicates whether there are more items to fetch.
        /// Uses HasMoreItems from API if available, otherwise falls back to cursor-based check.
        /// </summary>
        public bool HasMore => HasMoreItems ?? (Items != null && Items.Count > 0 && Next is not null);
    }

    /// <summary>
    /// Cursor for folder pagination using composite key (CreatedAt + Name)
    /// </summary>
    public sealed record FolderSeekCursor(
        string LastObjectId,
        DateTimeOffset LastObjectCreated,
        string LastObjectName);  // Added for composite cursor to avoid skipping folders with same timestamp

    /// <summary>
    /// Tracks progress through multiple DOSSIER subfolders during discovery
    /// </summary>
    public class MultiFolderDiscoveryCursor
    {
        /// <summary>
        /// Maps folder type (e.g., "PL", "FL") to its folder ID
        /// </summary>
        public Dictionary<string, string> SubfolderMap { get; set; } = new();

        /// <summary>
        /// Current subfolder type being processed (e.g., "PL")
        /// </summary>
        public string? CurrentFolderType { get; set; }

        /// <summary>
        /// Cursor within the current subfolder
        /// </summary>
        public FolderSeekCursor? CurrentCursor { get; set; }

        /// <summary>
        /// Index of current folder in ordered list
        /// </summary>
        public int CurrentFolderIndex { get; set; }

        /// <summary>
        /// Current skip count for skip/take pagination (used by ReadBatchAsync_v2)
        /// </summary>
        public int CurrentSkipCount { get; set; } = 0;
    }

    public sealed record MoveReaderResponse(long DocStagingId, string DocumentNodeId, string FolderDestId);



}
