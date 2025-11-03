using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Oracle.Models
{
    //[Table("DOCSTAGING11")]
    public class DocStaging
    {

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public string NodeId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public bool IsFile { get; set; }
        public string NodeType { get; set; } = string.Empty;
        public string ParentId { get; set; } = string.Empty;
        public string FromPath { get; set; } = string.Empty;
        public string ToPath { get; set; } = string.Empty;
        public string Status { get; set; }  // NEW, DONE, ERR
        //public int RetryCount { get; set; }
        public string? ErrorMsg { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // ========== EXTENDED FIELDS FOR MIGRATION (Per Documentation) ==========

        /// <summary>
        /// Document type code (e.g., "00099", "00824", "00130")
        /// Per documentation: Used for mapping to new document types during migration
        /// </summary>
        public string? DocumentType { get; set; }

        /// <summary>
        /// Document type code with "migracija" suffix for documents with "nova verzija" policy
        /// (e.g., "00824-migracija" migrates to final type "00099")
        /// Per documentation line 31-34: "novi tipove dokumenata koji će imati sufiks 'migracija'"
        /// </summary>
        public string? DocumentTypeMigration { get; set; }

        /// <summary>
        /// Source system identifier (e.g., "Heimdall", "DUT", "Depo kartoni_Validan")
        /// Per documentation line 116: "Izvor odnosno Source ne treba menjati, odnosno treba da ostane Heimdall"
        /// </summary>
        public string? Source { get; set; }

        /// <summary>
        /// Indicates if document is active after migration
        /// Per documentation: Complex rules for KDP documents (00099, 00824, 00101, 00825, 00100, 00827)
        /// Default: true for "novi dokument" policy, false for "nova verzija" with migracija suffix
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Document category code for classification
        /// Per documentation line 117: "Dokumenta treba smestiti u odgovarajuće kategorije prema poslednje ažurnom šifarniku"
        /// </summary>
        public string? CategoryCode { get; set; }

        /// <summary>
        /// Document category name
        /// </summary>
        public string? CategoryName { get; set; }

        /// <summary>
        /// Original creation date from old Alfresco system (NOT migration date!)
        /// Per documentation line 193-194: "datum kreiranja ne treba da bude datum kada je migracija
        /// sprovedena, nego datum kada je dokument arhiviran u starom alfresco"
        /// </summary>
        public DateTime? OriginalCreatedAt { get; set; }

        /// <summary>
        /// Contract number (Broj Ugovora) - essential for deposit documents
        /// Per documentation: Used for creating unique folder identifier: DE-{CoreId}-{ProductType}-{ContractNumber}
        /// </summary>
        public string? ContractNumber { get; set; }

        /// <summary>
        /// Client's Core ID for linking to client data
        /// Required for ClientAPI enrichment
        /// </summary>
        public string? CoreId { get; set; }

        /// <summary>
        /// Document version number (e.g., 1.1 for unsigned, 1.2 for signed)
        /// Per documentation line 168-170: "nepotpisanu verziju dokumentacije kao verziju 1.1,
        /// dok je potpisana verzija dokumentacije treba da bude verzija 1.2"
        /// </summary>
        public decimal Version { get; set; } = 1.0m;

        /// <summary>
        /// Comma-separated list of account numbers (for KDP documents: 00099, 00824)
        /// Per documentation line 123-129: "popuniti listu racuna koji su sada aktivni
        /// a bili su otvoreni na dan kreiranja dokumenta"
        /// Attribute name in Alfresco: docAccountNumbers
        /// </summary>
        public string? AccountNumbers { get; set; }

        /// <summary>
        /// Indicates if this document requires type transformation after migration completes
        /// (e.g., 00824-migracija -> 00099 for documents that become active)
        /// Per documentation line 107-112
        /// </summary>
        public bool RequiresTypeTransformation { get; set; }

        /// <summary>
        /// Final document type code after transformation (e.g., "00099" when migrated as "00824-migracija")
        /// Per documentation: "CT izmeni tip dokument u 00099, što će rezultirati i izmenom naziva
        /// u KDP za fizička lica i izmenom politike čuvanja u nova verzija"
        /// </summary>
        public string? FinalDocumentType { get; set; }

        /// <summary>
        /// Indicates if this is a signed version of the document (for DUT deposit documents)
        /// Per documentation: Signed versions should be version 1.2
        /// </summary>
        public bool IsSigned { get; set; }

        /// <summary>
        /// Offer ID from DUT system (for deposit documents)
        /// Used for linking to DUT OfferBO table
        /// </summary>
        public string? DutOfferId { get; set; }

        /// <summary>
        /// Product type code (e.g., "00008" for FL deposits, "00010" for PL deposits)
        /// Per documentation line 148: "Tip proizvoda (Fizicka lica – Depozitni proizvodi I SB- Depozitni proizvodi)"
        /// </summary>
        public string? ProductType { get; set; }

        public string? OriginalDocumentName { get; set; }  // Pre mapiranja
        public string? NewDocumentName { get; set; }       // Posle mapiranja (iz DocumentNameMapper)
        public string? OriginalDocumentCode { get; set; }  // Pre mapiranja
        public string? NewDocumentCode { get; set; }       // Posle mapiranja (iz DocumentCodeMapper)
        public string? TipDosijea { get; set; }           // "Dosije paket računa", "Dosije klijenta FL/PL", itd.
        public int? TargetDossierType { get; set; }       // 300, 400, 500, 700, 999 (DossierType enum vrednost)
        public string? ClientSegment { get; set; }        // "PI", "LE", "RETAIL", "SME"
        public string? OldAlfrescoStatus { get; set; }    // "validiran", "poništen" iz starog Alfresco-a
        public string? NewAlfrescoStatus { get; set; }    // Novi status posle mapiranja
        public bool WillReceiveMigrationSuffix { get; set; }  // Da li dobija "-migracija" sufiks
        public bool CodeWillChange { get; set; }               // Da li se šifra menja

    }
}



