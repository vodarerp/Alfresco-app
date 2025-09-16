
using Alfresco.Contracts.Models;

namespace Migration.Apstaction.Models
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

    public sealed record FolderReaderRequest(string RootId, string NameFilter, int Skip, int Take, FolderSeekCursor? Cursor);

    public sealed record FolderReaderResult(IReadOnlyList<ListEntry> Items, FolderSeekCursor? Next) 
    {
        public bool HasMore => Items != null && Items.Count > 0 && Next is not null;
    }

    public sealed record FolderSeekCursor(string LastObjectId, DateTimeOffset LastObjectCreated);

    public sealed record MoveReaderResponse(long DocStagingId, string DocumentNodeId, string FolderDestId);



}
