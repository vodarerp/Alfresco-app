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

        /// <summary>
        /// Indicates whether the ClientAPI returned an error response (e.g., ORA-01403: no data found).
        /// When true, the client data fields are empty but migration should continue.
        /// </summary>
        public bool HasError { get; set; } = false;

        /// <summary>
        /// Error message from ClientAPI when HasError is true.
        /// Example: "ORA-01403: no data found"
        /// </summary>
        public string? ErrorMessage { get; set; }
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
