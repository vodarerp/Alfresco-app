using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Oracle.Models
{
    public class FolderStaging
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public string? NodeId { get; set; }

        public string? ParentId { get; set; }

        public string? Name { get; set; }

        public string Status { get; set; }

        public string? DestFolderId { get; set; }

        /// <summary>
        /// NodeId of the DOSSIER-{folderType} destination folder (e.g., DOSSIER-PL, DOSSIER-FL)
        /// This is the parent folder under RootDestinationFolderId where target folders will be created
        /// Populated by FolderDiscoveryService during initial discovery
        /// </summary>
        public string? DossierDestFolderId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        //public DateTimeOffset InsertedAtAlfresco {  get; set; }

        // ========== EXTENDED FIELDS FOR MIGRATION (Per Documentation) ==========

        /// <summary>
        /// Client type: "FL" (Fizičko Lice - Natural Person) or "PL" (Pravno Lice - Legal Entity)
        /// Per documentation: Determines which folder type to create (Dosije klijenta FL vs PL)
        /// </summary>
        public string? ClientType { get; set; }

        /// <summary>
        /// Client's Core ID - unique identifier in core banking system
        /// Per documentation line 146: Required for ClientAPI enrichment
        /// Used in unique folder identifier: DE-{CoreId}-{ProductType}-{ContractNumber}
        /// </summary>
        public string? CoreId { get; set; }

        /// <summary>
        /// Full client name retrieved from ClientAPI
        /// Per documentation line 28-29: "Klijentske podatke na dosijeu (atributi dosijea)
        /// popuniti pozivom ClientAPI-a"
        /// </summary>
        public string? ClientName { get; set; }

        /// <summary>
        /// MBR (for legal entities) or JMBG (for natural persons)
        /// Retrieved from ClientAPI
        /// </summary>
        public string? MbrJmbg { get; set; }

        /// <summary>
        /// Product type code
        /// Per documentation line 148:
        /// - "00008" for Fizička lica – Depozitni proizvodi
        /// - "00010" for SB- Depozitni proizvodi (Pravna lica)
        /// </summary>
        public string? ProductType { get; set; }

        /// <summary>
        /// Contract number (Broj Ugovora)
        /// Per documentation line 149: Essential for creating deposit folder unique identifier
        /// </summary>
        public string? ContractNumber { get; set; }

        /// <summary>
        /// Batch number (Partija) - optional attribute
        /// Per documentation line 150-151: "Partija-opciono (ukoliko postoji u Aflfresco
        /// podataka o broju partije, migrirati navedeno kao atribut u dosijeu depozita)"
        /// </summary>
        public string? Batch { get; set; }

        /// <summary>
        /// Source system identifier (e.g., "Heimdall", "DUT", etc.)
        /// Per documentation: Important for tracking document origin
        /// </summary>
        public string? Source { get; set; }

        /// <summary>
        /// Unique folder identifier for deposit folders
        /// Per documentation line 156: "Referenca jedinsvenog identifikatora dosijea
        /// treba da bude: DE-{CoreId}{SifraTipaProizvoda}-{brojUgovora}"
        /// Example: DE-10194302-00008-10104302_20241105154459
        /// </summary>
        public string? UniqueIdentifier { get; set; }

        /// <summary>
        /// Date when deposit was processed (NOT migration date!)
        /// Per documentation line 190-191: "datum kreiranja koji ne treba da bude jedan datumu
        /// kada je migrirana dokumentacija, vec datumu kada je orocen depozit procesuiran"
        /// </summary>
        public DateTime? ProcessDate { get; set; }

        /// <summary>
        /// Client residency status (Resident/Non-resident)
        /// Retrieved from ClientAPI
        /// </summary>
        public string? Residency { get; set; }

        /// <summary>
        /// Client segment classification
        /// Retrieved from ClientAPI
        /// </summary>
        public string? Segment { get; set; }

        /// <summary>
        /// Client subtype for additional classification
        /// Retrieved from ClientAPI
        /// </summary>
        public string? ClientSubtype { get; set; }

        /// <summary>
        /// Staff indicator (if client is a bank employee)
        /// Retrieved from ClientAPI
        /// </summary>
        public string? Staff { get; set; }

        /// <summary>
        /// OPU (Organizational Unit) of the user
        /// Retrieved from ClientAPI
        /// </summary>
        public string? OpuUser { get; set; }

        /// <summary>
        /// OPU/ID of realization
        /// Retrieved from ClientAPI
        /// </summary>
        public string? OpuRealization { get; set; }

        /// <summary>
        /// Barclex identifier
        /// Retrieved from ClientAPI
        /// </summary>
        public string? Barclex { get; set; }

        /// <summary>
        /// Collaborator/Partner information
        /// Retrieved from ClientAPI
        /// </summary>
        public string? Collaborator { get; set; }

        /// <summary>
        /// BarCLEX Name
        /// Retrieved from ClientAPI
        /// </summary>
        public string? BarCLEXName { get; set; }

        /// <summary>
        /// BarCLEX OPU
        /// Retrieved from ClientAPI
        /// </summary>
        public string? BarCLEXOpu { get; set; }

        /// <summary>
        /// BarCLEX Group Name
        /// Retrieved from ClientAPI
        /// </summary>
        public string? BarCLEXGroupName { get; set; }

        /// <summary>
        /// BarCLEX Group Code
        /// Retrieved from ClientAPI
        /// </summary>
        public string? BarCLEXGroupCode { get; set; }

        /// <summary>
        /// BarCLEX Code
        /// Retrieved from ClientAPI
        /// </summary>
        public string? BarCLEXCode { get; set; }

        /// <summary>
        /// Creator of the folder/document
        /// </summary>
        public string? Creator { get; set; }

        /// <summary>
        /// Archival date (when document was archived, may differ from creation date)
        /// </summary>
        public DateTime? ArchivedAt { get; set; }

        public string? TipDosijea { get; set; }           // "Dosije paket računa", "Dosije depozita", itd.
        public string? TargetDossierType { get; set; }       // 300, 400, 500, 700, 999 (DossierType enum vrednost)

        public string? ClientSegment { get; set; }        // "PI", "LE", "RETAIL", "SME"
    }
}
