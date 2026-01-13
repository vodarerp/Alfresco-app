
using Alfresco.Contracts.Models;
using Migration.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    public interface IMoveExecutor
    {
       // Task<int> ExecuteMoveAsync(int take, CancellationToken ct);

        /// <summary>
        /// Moves a document to a destination folder and returns the updated Entry object
        /// </summary>
        /// <returns>Entry object with updated properties, or null if move failed</returns>
        Task<Entry?> MoveAsync(string DocumentId, string DestFolderId, CancellationToken ct);

        /// <summary>
        /// Copies a document to a destination folder and returns the new Entry object
        /// </summary>
        /// <returns>Entry object for the copied document, or null if copy failed</returns>
        Task<Entry?> CopyAsync(string DocumentId, string DestFolderId, CancellationToken ct);

    }
}
