using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Alfresco.Contracts.Oracle.Models
{
    /// <summary>
    /// Staging tabela za sve KDP dokumente učitane iz Alfresca
    /// Tipovi dokumenata: 00824 (KDP vlasnici za FL - original), 00099 (KDP vlasnici za FL - konačan)
    /// </summary>
    [Table("KdpDocumentStaging")]
    public class KdpDocumentStaging
    {
        /// <summary>
        /// Primary key - auto-generated
        /// </summary>
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>
        /// Alfresco NodeId dokumenta
        /// </summary>
        public string NodeId { get; set; } = string.Empty;

        /// <summary>
        /// Naziv dokumenta (sa ekstenzijom)
        /// </summary>
        public string? DocumentName { get; set; }

        /// <summary>
        /// Puna putanja dokumenta u Alfrescu (path name)
        /// Primer: /Company Home/Sites/bank/documentLibrary/ACC-123456/DOSSIERS-FL/...
        /// </summary>
        public string? DocumentPath { get; set; }

        /// <summary>
        /// NodeId parent foldera
        /// </summary>
        public string? ParentFolderId { get; set; }

        /// <summary>
        /// Naziv parent foldera
        /// </summary>
        public string? ParentFolderName { get; set; }

        /// <summary>
        /// Tip dokumenta (ecm:docType custom property)
        /// Vrednosti: "00824" (original KDP) ili "00099" (konačan KDP)
        /// </summary>
        public string? DocumentType { get; set; }

        /// <summary>
        /// Status dokumenta (ecm:docStatus custom property)
        /// Vrednosti: "1" = aktivan, "2" = neaktivan
        /// </summary>
        public string? DocumentStatus { get; set; }

        /// <summary>
        /// Datum kreiranja dokumenta u Alfrescu (cm:created)
        /// </summary>
        public DateTime? CreatedDate { get; set; }

        /// <summary>
        /// Lista računa (ecm:bnkAccountNumber custom property)
        /// Računi odvojeni zarezom
        /// </summary>
        public string? AccountNumbers { get; set; }

        /// <summary>
        /// Naziv ACC foldera ekstrarhovan iz DocumentPath
        /// Primer: ACC-123456
        /// </summary>
        public string? AccFolderName { get; set; }

        /// <summary>
        /// Core ID klijenta (ekstrahovano iz AccFolderName)
        /// Primer: 123456 (iz ACC-123456)
        /// </summary>
        public string? CoreId { get; set; }

        /// <summary>
        /// Datum obrade/učitavanja u staging tabelu
        /// </summary>
        public DateTime ProcessedDate { get; set; } = DateTime.Now;
    }
}
