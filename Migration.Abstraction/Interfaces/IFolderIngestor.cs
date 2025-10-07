

using Alfresco.Contracts.Models;
using Alfresco.Contracts.Oracle.Models;
using Migration.Abstraction.Models;

namespace Migration.Abstraction.Interfaces
{
    public interface IFolderIngestor
    {
        //Task UpsertAsync(FolderIngestorItem item, CancellationToken ct);

        Task<int> InserManyAsync(IReadOnlyList<FolderStaging> items, CancellationToken ct);
    }


}
