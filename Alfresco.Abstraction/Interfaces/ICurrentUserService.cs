using Alfresco.Contracts.Models;

namespace Alfresco.Abstraction.Interfaces
{
   
    public interface ICurrentUserService
    {
        
        Task InitializeAsync(CancellationToken ct = default);

        string UserId { get; }
       
        string DisplayName { get; }

        string Email { get; }

        AlfrescoUserInfo? CurrentUser { get; }
    }
}
