
namespace Migration.Apstaction.Models
{
    public sealed record FolderSource(string id, string name, string nodeID, string fullPath);
    public sealed record ScanRequest(string rootId, string nameFilter, int skip, int take);

    public sealed record DestinationFolder(string id, string name, string parentId, string fullPath);
    public sealed record FolderIngestorItem(string srcFolderId, string srcFolderName, string srcRootId, string destFolderId, string destRootId);



    public sealed record DiscoverRequest(string SrcRootId, string DestRootId, string NameFilter, int PageSize);
}
        