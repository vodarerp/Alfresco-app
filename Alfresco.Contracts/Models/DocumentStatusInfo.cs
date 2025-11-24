using System;

namespace Alfresco.Contracts.Models
{
    /// <summary>
    /// Sadrži detaljne informacije o statusu dokumenta nakon migracije.
    /// Koristi se u DocumentStatusDetectorV2 i DocumentStatusDetectorV3.
    /// </summary>
    public record DocumentStatusInfo
    {
        /// <summary>
        /// Da li je dokument aktivan nakon migracije
        /// </summary>
        public bool IsActive { get; init; }

        /// <summary>
        /// Alfresco status string: "validiran" ili "poništen"
        /// </summary>
        public string Status { get; init; } = string.Empty;

        /// <summary>
        /// Razlog određivanja statusa (za debugging)
        /// </summary>
        public string DeterminationReason { get; init; } = string.Empty;

        /// <summary>
        /// Prioritet pravila koje je odredilo status (1 = najviši, 4 = najniži)
        /// Koristi se u V3, u V2 je obično 99
        /// </summary>
        public int Priority { get; init; }

        /// <summary>
        /// Šifra dokumenta iz mapiranja
        /// </summary>
        public string? MappingCode { get; init; }

        /// <summary>
        /// Naziv dokumenta iz mapiranja
        /// </summary>
        public string? MappingName { get; init; }

        /// <summary>
        /// Politika čuvanja iz mapiranja (koristi se u V3)
        /// </summary>
        public string? PolitikaCuvanja { get; init; }

        /// <summary>
        /// Da li naziv dokumenta sadrži sufiks "- migracija" (koristi se u V3)
        /// </summary>
        public bool HasMigrationSuffix { get; init; }

        /// <summary>
        /// Da li opis dokumenta sadrži sufiks "- migracija" (koristi se u V2 - stara logika)
        /// </summary>
        [Obsolete("Stara logika iz V2 - u V3 koristiti HasMigrationSuffix")]
        public bool HasMigrationSuffixInOpis { get; init; }

        /// <summary>
        /// Da li je dokument bio neaktivan u starom sistemu (koristi se u V2 - stara logika)
        /// </summary>
        [Obsolete("Stara logika iz V2 - nije više u upotrebi u V3")]
        public bool WasInactiveInOldSystem { get; init; }
    }
}
