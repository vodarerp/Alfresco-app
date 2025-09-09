using Migration.Apstaction.Models;

namespace Migration.Apstaction.Interfaces
{
    public interface IDestinationResolver
    {
        Task<DestinationFolder> EnsureAsync(string destinationRootId, string newFolderName, CancellationToken ct);
    }

}
