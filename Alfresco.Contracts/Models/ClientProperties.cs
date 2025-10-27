using System;

namespace Alfresco.Contracts.Models
{
    /// <summary>
    /// Client-specific properties that can be retrieved from Alfresco folder properties
    /// or enriched from ClientAPI if not present in Alfresco.
    /// These properties match the ClientData model from Migration.Abstraction.
    /// </summary>
    public class ClientProperties
    {
        /// <summary>
        /// Client's Core ID (unique identifier in the core banking system)
        /// </summary>
        public string? CoreId { get; set; }

        /// <summary>
        /// MBR (for legal entities) or JMBG (for natural persons)
        /// </summary>
        public string? MbrJmbg { get; set; }

        /// <summary>
        /// Full client name
        /// </summary>
        public string? ClientName { get; set; }

        /// <summary>
        /// Client type: "FL" (Fizičko Lice - Natural Person) or "PL" (Pravno Lice - Legal Entity)
        /// </summary>
        public string? ClientType { get; set; }

        /// <summary>
        /// Client subtype for additional classification
        /// </summary>
        public string? ClientSubtype { get; set; }

        /// <summary>
        /// Residency status (Resident/Non-resident)
        /// </summary>
        public string? Residency { get; set; }

        /// <summary>
        /// Client segment classification
        /// </summary>
        public string? Segment { get; set; }

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
        /// Checks if client properties are present (at least CoreId should be set)
        /// </summary>
        public bool HasClientData => !string.IsNullOrWhiteSpace(CoreId);

        /// <summary>
        /// Checks if all required properties are populated
        /// </summary>
        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(CoreId) &&
            !string.IsNullOrWhiteSpace(MbrJmbg) &&
            !string.IsNullOrWhiteSpace(ClientName) &&
            !string.IsNullOrWhiteSpace(ClientType);
    }
}
