using Alfresco.Contracts.Models;
using Migration.Abstraction.Models;

namespace Migration.Infrastructure.Extensions
{
    /// <summary>
    /// Extension methods for ClientData mapping
    /// </summary>
    public static class ClientDataExtensions
    {
        /// <summary>
        /// Converts ClientData from ClientAPI to ClientProperties for Entry model
        /// </summary>
        public static ClientProperties ToClientProperties(this ClientData clientData)
        {
            return new ClientProperties
            {
                CoreId = clientData.CoreId,
                MbrJmbg = clientData.MbrJmbg,
                ClientName = clientData.ClientName,
                ClientType = clientData.ClientType,
                ClientSubtype = clientData.ClientSubtype,
                Residency = clientData.Residency,
                Segment = clientData.Segment,
                Staff = clientData.Staff,
                OpuUser = clientData.OpuUser,
                OpuRealization = clientData.OpuRealization,
                Barclex = clientData.Barclex,
                Collaborator = clientData.Collaborator
            };
        }

        /// <summary>
        /// Enriches Entry with ClientData from ClientAPI
        /// </summary>
        public static void EnrichWithClientData(this Entry entry, ClientData clientData)
        {
            entry.ClientProperties = clientData.ToClientProperties();
        }
    }
}
