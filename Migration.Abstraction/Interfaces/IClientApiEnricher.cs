using Migration.Abstraction.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    public interface IClientApiEnricher
    {
        /// <summary>
        /// Poziva ClientAPI za dati folder name, ekstraktuje CoreId iz naziva
        /// i vraća popunjeni ClientData objekat.
        /// Baca ClientApiTimeoutException / ClientApiRetryExhaustedException za fatalne greške.
        /// Za ostale greške loguje warning i vraća prazan ClientData (ne blokira tok).
        /// </summary>
        Task<ClientData> EnrichFromFolderNameAsync(string folderName, CancellationToken ct = default);

        /// <summary>
        /// Gradi Dictionary sa Alfresco folder properties na osnovu ClientData.
        /// Koristi se u DocumentResolver i PreviewFolderCreationService.
        /// </summary>
        Dictionary<string, object> BuildFolderProperties(ClientData clientData, string folderName);
    }
}
