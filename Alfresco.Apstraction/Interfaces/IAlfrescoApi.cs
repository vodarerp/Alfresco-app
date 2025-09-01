using Alfresco.Contracts.Request;
using Alfresco.Contracts.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Apstraction.Interfaces
{
    public interface IAlfrescoApi
    {
        Task<bool> PingAsync(CancellationToken cancellationToken = default);

        Task<string> GetNodeChildrenAsync(string nodeId, CancellationToken cancellationToken = default); //Umesto Tast<string> staviti Task<ChildrenResponse>

        Task<NodeChildrenResponse> PostSearch(PostSearchRequest inRequest, CancellationToken cancellationToken = default);


        Task<bool> MoveDocumentAsync(string nodeId, string targetFolderId, string? newName = default, CancellationToken ct = default);

    }
}
