

using Alfresco.Contracts.Models;
using Alfresco.Contracts.Oracle.Models;
using Migration.Apstaction.Models;

namespace Migration.Apstaction.Interfaces
{
    public interface IFolderIngestor
    {
        //Task UpsertAsync(FolderIngestorItem item, CancellationToken ct);

        Task<int> InserManyAsync(IReadOnlyList<FolderStaging> items, CancellationToken ct);
    }


}
