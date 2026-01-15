using System;

namespace Migration.Abstraction.Models
{
    /// <summary>
    /// Represents comprehensive client data retrieved from the Client API.
    /// Used for enriching folder metadata during migration process.
    /// </summary>
    public class ClientData
    {
        /// <summary>
        /// Client's Core ID (unique identifier in the core banking system)
        /// </summary>
        public string CoreId { get; set; } = string.Empty;

        /// <summary>
        /// MBR (for legal entities) or JMBG (for natural persons)
        /// </summary>
        public string MbrJmbg { get; set; } = string.Empty;

        /// <summary>
        /// Full client name
        /// </summary>
        public string ClientName { get; set; } = string.Empty;

        /// <summary>
        /// Client type: "FL" (Fizičko Lice - Natural Person) or "PL" (Pravno Lice - Legal Entity)
        /// </summary>
        public string ClientType { get; set; } = string.Empty;

        /// <summary>
        /// Client subtype for additional classification
        /// </summary>
        public string ClientSubtype { get; set; } = string.Empty;

        /// <summary>
        /// Residency status (Resident/Non-resident)
        /// </summary>
        public string Residency { get; set; } = string.Empty;

        /// <summary>
        /// Client segment classification
        /// </summary>
        public string Segment { get; set; } = string.Empty;

        /// <summary>
        /// Staff indicator (if client is a bank employee)
        /// </summary>
        public string? Staff { get; set; }

        /// <summary>
        /// OPU (Organizational Unit) of the user
        /// </summary>
        public string? OpuUser { get; set; }

        /// <summary>
        /// OPU/ID of realization
        /// </summary>
        public string? OpuRealization { get; set; }

        /// <summary>
        /// Barclex identifier
        /// </summary>
        public string? Barclex { get; set; }

        /// <summary>
        /// Collaborator/Partner information
        /// </summary>
        public string? Collaborator { get; set; }

        /// <summary>
        /// BarCLEX Name
        /// </summary>
        public string? BarCLEXName { get; set; }

        /// <summary>
        /// BarCLEX OPU
        /// </summary>
        public string? BarCLEXOpu { get; set; }

        /// <summary>
        /// BarCLEX Group Name
        /// </summary>
        public string? BarCLEXGroupName { get; set; }

        /// <summary>
        /// BarCLEX Group Code
        /// </summary>
        public string? BarCLEXGroupCode { get; set; }

        /// <summary>
        /// BarCLEX Code
        /// </summary>
        public string? BarCLEXCode { get; set; }

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
}
