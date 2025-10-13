using System;
using System.Collections.Generic;

namespace Migration.Abstraction.Models
{
    /// <summary>
    /// Represents a deposit offer from DUT application's OfferBO table.
    /// Per documentation: Only offers with status "Booked" should be migrated.
    /// </summary>
    public class DutOffer
    {
        /// <summary>
        /// Unique offer identifier from DUT system
        /// </summary>
        public string OfferId { get; set; } = string.Empty;

        /// <summary>
        /// Client's Core ID
        /// </summary>
        public string CoreId { get; set; } = string.Empty;

        /// <summary>
        /// Contract number (Broj Ugovora) - essential for unique folder identifier
        /// Format for unique ID: DE-{CoreId}-{ProductType}-{ContractNumber}_{Timestamp}
        /// </summary>
        public string ContractNumber { get; set; } = string.Empty;

        /// <summary>
        /// Deposit amount
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// Currency code (e.g., "RSD", "EUR", "USD")
        /// </summary>
        public string Currency { get; set; } = string.Empty;

        /// <summary>
        /// Date when deposit was made (Datum oročenja)
        /// Used for matching documents when contract number is missing
        /// </summary>
        public DateTime DepositDate { get; set; }

        /// <summary>
        /// Offer status - must be "Booked" for migration
        /// Per documentation line 171-172: "Migraciju je potrebno sprovoditi samo za
        /// dokumentaciju koja u OfferBO tabeli DUT aplikaciji ima status Booked"
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Batch number (Partija) - optional attribute for deposit folder
        /// </summary>
        public string? Batch { get; set; }

        /// <summary>
        /// Product type code (Tip proizvoda)
        /// - "00008" for Fizička lica – Depozitni proizvodi
        /// - "00010" for SB- Depozitni proizvodi (Pravna lica)
        /// </summary>
        public string ProductType { get; set; } = string.Empty;

        /// <summary>
        /// Date when offer was created in DUT system
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Date when offer was processed/booked
        /// Important: This should be used as ProcessDate for the folder, NOT migration date!
        /// Per documentation line 190: "datum kreiranja koji ne treba da bude jedan datumu
        /// kada je migrirana dokumentacija, vec datumu kada je orocen depozit procesuiran"
        /// </summary>
        public DateTime? ProcessedAt { get; set; }
    }

    /// <summary>
    /// Detailed information about a deposit offer including all attributes and metadata.
    /// </summary>
    public class DutOfferDetails : DutOffer
    {
        /// <summary>
        /// Interest rate for the deposit
        /// </summary>
        public decimal? InterestRate { get; set; }

        /// <summary>
        /// Maturity date of the deposit
        /// </summary>
        public DateTime? MaturityDate { get; set; }

        /// <summary>
        /// Duration in days/months
        /// </summary>
        public int? Duration { get; set; }

        /// <summary>
        /// Additional notes or comments
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// List of document IDs associated with this offer
        /// </summary>
        public List<string> DocumentIds { get; set; } = new List<string>();
    }

    /// <summary>
    /// Represents a document associated with a deposit offer in DUT system.
    /// Per documentation: Need to migrate both unsigned (v1.1) and signed (v1.2) versions.
    /// </summary>
    public class DutDocument
    {
        /// <summary>
        /// Unique document identifier in DUT system
        /// </summary>
        public string DocumentId { get; set; } = string.Empty;

        /// <summary>
        /// Document type code (e.g., "Ugovor o oročenom depozitu", "Ponuda", etc.)
        /// Minimum required documents per documentation lines 179-185:
        /// - Ugovor o oročenom depozitu
        /// - Ponuda
        /// - Plan isplate depozita
        /// - Obavezni elementi Ugovora
        /// </summary>
        public string DocumentType { get; set; } = string.Empty;

        /// <summary>
        /// Document type code from mapping table
        /// </summary>
        public string DocumentTypeCode { get; set; } = string.Empty;

        /// <summary>
        /// Alfresco Node ID where document is stored in old Alfresco
        /// </summary>
        public string AlfrescoNodeId { get; set; } = string.Empty;

        /// <summary>
        /// Original creation date in old Alfresco system
        /// Per documentation line 193-194: "datum kreiranja ne treba da bude datum
        /// kada je migracija sprovedena, nego datum kada je dokument arhiviran u starom alfresco"
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Indicates if this is a signed version of the document
        /// Per documentation line 168-170:
        /// - Unsigned version should be migrated as version 1.1
        /// - Signed version should be migrated as version 1.2
        /// </summary>
        public bool IsSigned { get; set; }

        /// <summary>
        /// Version number in old system
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Category code for document classification
        /// </summary>
        public string? CategoryCode { get; set; }

        /// <summary>
        /// Category name for document classification
        /// </summary>
        public string? CategoryName { get; set; }

        /// <summary>
        /// Document status - should be "Active" after migration (line 186)
        /// </summary>
        public string Status { get; set; } = "Active";
    }

    /// <summary>
    /// Result of matching documents to offers when contract number is missing.
    /// Used per documentation lines 196-202 for matching by Core ID, date, and amount.
    /// </summary>
    public class DutOfferMatchResult
    {
        /// <summary>
        /// Client's Core ID
        /// </summary>
        public string CoreId { get; set; } = string.Empty;

        /// <summary>
        /// Deposit date used for matching
        /// </summary>
        public DateTime DepositDate { get; set; }

        /// <summary>
        /// List of matching offers
        /// If only one offer matches, can auto-match
        /// If multiple offers match, need manual intervention per documentation line 199-202
        /// </summary>
        public List<DutOffer> MatchingOffers { get; set; } = new List<DutOffer>();

        /// <summary>
        /// Indicates if automatic matching is possible (only one offer for the date)
        /// </summary>
        public bool CanAutoMatch => MatchingOffers.Count == 1;

        /// <summary>
        /// Indicates if manual intervention is required (multiple offers for the date)
        /// Per documentation: "Ukoliko je klijent imao vise orocenja za jedan datum,
        /// neophodno je iz OfferBo tabele za konkretnog klijenta dostaviti sledece podatke"
        /// </summary>
        public bool RequiresManualMatching => MatchingOffers.Count > 1;
    }
}
