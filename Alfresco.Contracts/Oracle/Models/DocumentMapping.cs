using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Alfresco.Contracts.Oracle.Models
{
    /// <summary>
    /// Mapiranje dokumenata iz tabele DocumentMappings u SQL Server-u.
    /// Ova tabela zamenjuje statički HeimdallDocumentMapper.
    /// </summary>
    [Table("DocumentMappings")]
    public class DocumentMapping
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ID { get; set; }

        /// <summary>
        /// Naziv dokumenta (engleski naziv iz starog sistema)
        /// Primer: "Personal Notice", "KYC Questionnaire"
        /// </summary>
        [Column("NAZIV")]
        [MaxLength(500)]
        public string? Naziv { get; set; }

        /// <summary>
        /// Broj dokumenata (koliko dokumenata koristi ovo mapiranje)
        /// </summary>
        [Column("BROJ_DOKUMENATA")]
        public int? BrojDokumenata { get; set; }

        /// <summary>
        /// Šifra dokumenta u starom sistemu
        /// Primer: "00253", "00130"
        /// </summary>
        [Column("sifraDokumenta")]
        [MaxLength(200)]
        public string? SifraDokumenta { get; set; }

        /// <summary>
        /// Naziv dokumenta na srpskom jeziku
        /// Primer: "GDPR saglasnost", "KYC upitnik"
        /// </summary>
        [Column("NazivDokumenta")]
        [MaxLength(500)]
        public string? NazivDokumenta { get; set; }

        /// <summary>
        /// Tip dosijea u koji dokument treba da ide
        /// Primer: "Dosije klijenta FL / PL", "Dosije paket racuna"
        /// </summary>
        [Column("TipDosijea")]
        [MaxLength(200)]
        public string? TipDosijea { get; set; }

        /// <summary>
        /// Tip proizvoda
        /// Primer: "00008", "00010"
        /// </summary>
        [Column("TipProizvoda")]
        [MaxLength(200)]
        public string? TipProizvoda { get; set; }

        /// <summary>
        /// Šifra dokumenta posle migracije
        /// Primer: "00849", "00841"
        /// </summary>
        [Column("sifraDokumenta_migracija")]
        [MaxLength(200)]
        public string? SifraDokumentaMigracija { get; set; }

        /// <summary>
        /// Naziv dokumenta posle migracije (može da sadrži sufiks "- migracija")
        /// Primer: "GDPR saglasnost - migracija", "KYC upitnik - migracija"
        /// </summary>
        [Column("NazivDokumenta_migracija")]
        [MaxLength(500)]
        public string? NazivDokumentaMigracija { get; set; }

        /// <summary>
        /// Naziv Excel fajla iz kojeg su podaci izvučeni
        /// </summary>
        [Column("ExcelFileName")]
        [MaxLength(300)]
        public string? ExcelFileName { get; set; }

        /// <summary>
        /// Naziv Excel sheet-a iz kojeg su podaci izvučeni
        /// </summary>
        [Column("ExcelFileSheet")]
        [MaxLength(300)]
        public string? ExcelFileSheet { get; set; }
    }
}
