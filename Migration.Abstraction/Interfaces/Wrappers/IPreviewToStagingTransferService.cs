using Migration.Abstraction.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces.Wrappers
{
    public interface IPreviewToStagingTransferService
    {
        /// <summary>
        /// Prenosi zapise iz PreviewDocStaging u DocStaging.
        /// Filtrira po DossierType i DocumentType (null = sve).
        /// Status FOLDER_EXISTS / FOLDER_CREATED → mapira u DocStaging → Status TRANSFERRED.
        /// </summary>
        Task<bool> RunAsync(
            string? dossierType,
            string? documentType,
            CancellationToken ct,
            Action<WorkerProgress>? progressCallback = null);
    }
}
