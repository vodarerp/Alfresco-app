using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Alfresco.Contracts.Oracle.Models
{
    /// <summary>
    /// Finalna tabela sa rezultatima obrade KDP dokumenata
    /// Sadrži najmlađe KDP dokumente iz foldera koji imaju samo neaktivne KDP dokumente
    /// </summary>
    [Table("KdpExportResult")]
    public class KdpExportResult
    {
        /// <summary>
        /// Primary key - auto-generated
        /// </summary>
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>
        /// Referenca dosijea - puna putanja ACC foldera
        /// Primer: /Company Home/Sites/bank/documentLibrary/ACC-123456/DOSSIERS-FL
        /// </summary>
        public string? ReferncaDosijea { get; set; }

        /// <summary>
        /// Klijentski broj (Core ID)
        /// Primer: 123456
        /// </summary>
        public string? KlijentskiBroj { get; set; }

        /// <summary>
        /// Referenca dokumenta - NodeId u Alfrescu
        /// </summary>
        public string ReferencaDokumenta { get; set; } = string.Empty;

        /// <summary>
        /// Tip dokumenta (00824 ili 00099)
        /// </summary>
        public string? TipDokumenta { get; set; }

        /// <summary>
        /// Datum kreiranja dokumenta
        /// </summary>
        public DateTime? DatumKreiranjaDokumenta { get; set; }

        /// <summary>
        /// Lista računa - za buduću upotrebu (banka popunjava)
        /// Računi odvojeni zarezom
        /// </summary>
        public string? ListaRacuna { get; set; }

        /// <summary>
        /// Naziv dokumenta
        /// </summary>
        public string? DocumentName { get; set; }

        /// <summary>
        /// Naziv ACC foldera
        /// Primer: ACC-123456
        /// </summary>
        public string? AccFolderName { get; set; }

        /// <summary>
        /// Ukupan broj KDP dokumenata u folderu
        /// </summary>
        public int? TotalKdpDocumentsInFolder { get; set; }

        /// <summary>
        /// Datum eksporta rezultata
        /// </summary>
        public DateTime ExportDate { get; set; } = DateTime.Now;

        /// <summary>
        /// Da li je dokument aktiviran u Alfrescu
        /// </summary>
        public bool IsActivated { get; set; } = false;

        /// <summary>
        /// Datum aktivacije dokumenta
        /// </summary>
        public DateTime? ActivationDate { get; set; }

        /// <summary>
        /// Akcija izvršena nad dokumentom
        /// </summary>
        public int? Action { get; set; }

        /// <summary>
        /// Izuzetak - da li je dokument izuzetak
        /// </summary>
        public int? Izuzetak { get; set; }

        /// <summary>
        /// Poruka o rezultatu obrade
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Stari status dokumenta
        /// </summary>
        public string? OldDocumentStatus { get; set; }

        /// <summary>
        /// Novi status dokumenta
        /// </summary>
        public string? NewDocumentStatus { get; set; }
    }
}
