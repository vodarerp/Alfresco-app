using Migration.Abstraction.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces.Wrappers
{
    public interface IPreviewFolderCreationService
    {
        /// <summary>
        /// Faza 3: Kreira Alfresco foldere za sve zapise sa statusom FOLDER_PENDING_CREATION.
        /// Koristi ClientApi* podatke koji su vec upisani u PreviewDocStaging (Faza 2).
        /// Nakon kreiranja: DossierDestinationFolderId = nodeId, Status = FOLDER_CREATED
        /// </summary>
        Task<bool> RunAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback = null);
    }
}
