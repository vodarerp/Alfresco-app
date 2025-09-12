using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Apstaction.Interfaces
{
    public interface IDocumentResolver
    {
        Task<string> ResolveAsync(string destinationRootId, string newFolderName,CancellationToken ct);
    }
}
