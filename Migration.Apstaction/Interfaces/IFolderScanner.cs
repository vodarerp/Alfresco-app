using Migration.Apstaction.Models;

namespace Migration.Apstaction.Interfaces
{
    public interface IFolderScanner
    {
        Task<IReadOnlyList<FolderSource>> ScanAsync(ScanRequest inRequest, CancellationToken ct);
    }
    
}
