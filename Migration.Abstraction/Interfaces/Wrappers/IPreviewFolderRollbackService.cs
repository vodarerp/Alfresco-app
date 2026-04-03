using Migration.Abstraction.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces.Wrappers
{
    public interface IPreviewFolderRollbackService
    {
        /// <summary>
        /// Briše sve Alfresco foldere kreirane u Fazi 3 (Status = FOLDER_CREATED)
        /// i resetuje njihov status nazad na FOLDER_PENDING_CREATION.
        /// Namenjen isključivo za testiranje.
        /// </summary>
        Task<bool> RunAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback = null);
    }
}
