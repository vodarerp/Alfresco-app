using Alfresco.Contracts.Oracle.Models;


namespace Oracle.Abstraction.Interfaces
{
    public interface IFolderStagingRepository : IRepository<FolderStaging, long>
    {
        Task<IReadOnlyList<FolderStaging>> TakeReadyForProcessingAsync(int take, CancellationToken ct);
        Task SetStatusAsync(long id, string status, string? error, CancellationToken ct);
        Task FailAsync(long id, string error, CancellationToken ct);
    }
}
