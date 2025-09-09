

using Migration.Apstaction.Models;

namespace Migration.Apstaction.Interfaces
{
    public interface IFolderIngestor
    {
        Task UpsertAsync(FolderIngestorItem item, CancellationToken ct);
    }


}
