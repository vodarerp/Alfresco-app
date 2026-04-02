using Migration.Abstraction.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces.Wrappers
{
    public interface IPreviewFolderPreparationService
    {
        /// <summary>
        /// Faza 2: Za svaki distinct DossierDestinationFolderName iz PreviewDocStaging
        /// proverava da li folder postoji u Alfresci.
        /// Postojeci → status FOLDER_EXISTS + NodeId
        /// Ne postoji → poziva ClientAPI, upisuje ClientApi* podatke, status FOLDER_PENDING_CREATION
        /// </summary>
        Task<bool> RunAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback = null);
    }
}
