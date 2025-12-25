namespace Migration.Abstraction.Models
{
    /// <summary>
    /// Represents client detail information from GetClientDetail endpoint.
    /// Contains essential client identification data.
    /// </summary>
    public class ClientDetailResponse
    {
        /// <summary>
        /// Client name
        /// Maps to: ecm:ClientName
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Client general information
        /// Contains resident indicator and client ID
        /// </summary>
        public ClientGeneralInfo ClientGeneral { get; set; } = new();
    }

    /// <summary>
    /// Represents client general information from GetClientDetail endpoint.
    /// </summary>
    public class ClientGeneralInfo
    {
        /// <summary>
        /// Resident indicator
        /// Maps to: ecm:bnkResidence
        /// </summary>
        public string ResidentIndicator { get; set; } = string.Empty;

        /// <summary>
        /// Client ID (MTBR - Matični broj)
        /// Maps to: ecm:bnkMTBR
        /// </summary>
        public string ClientID { get; set; } = string.Empty;
    }
}
