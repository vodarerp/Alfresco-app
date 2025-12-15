using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Alfresco.Contracts.Oracle.Models
{
    /// <summary>
    /// Mapiranje kategorija dokumenata iz tabele CategoryMapping u SQL Server-u.
    /// Povezuje se sa DocumentMapping preko OznakaTipa = SifraDokumentaMigracija.
    /// Koristi se za popunjavanje ecm:docCategory i ecm:docCategoryName properties.
    /// </summary>
    [Table("CategoryMapping")]
    public class CategoryMapping
    {
        /// <summary>
        /// Oznaka tipa dokumenta (jednaka SifraDokumentaMigracija iz DocumentMappings)
        /// Primer: "00849", "00841"
        /// </summary>
        [Column("OznakaTipa")]
        [MaxLength(255)]
        public string? OznakaTipa { get; set; }

        /// <summary>
        /// Naziv tipa dokumenta
        /// </summary>
        [Column("NazivTipa")]
        [MaxLength(255)]
        public string? NazivTipa { get; set; }

        /// <summary>
        /// Oznaka kategorije dokumenta
        /// Koristi se za ecm:docCategory property
        /// </summary>
        [Column("OznakaKategorije")]
        [MaxLength(255)]
        public string? OznakaKategorije { get; set; }

        /// <summary>
        /// Naziv kategorije dokumenta
        /// Koristi se za ecm:docCategoryName property
        /// </summary>
        [Column("NazivKategorije")]
        [MaxLength(255)]
        public string? NazivKategorije { get; set; }

        /// <summary>
        /// Politika čuvanja dokumenta
        /// </summary>
        [Column("PolitikaCuvanja")]
        [MaxLength(255)]
        public string? PolitikaCuvanja { get; set; }

        /// <summary>
        /// Da li je datum isteka obavezan
        /// </summary>
        [Column("DatumIstekaObavezan")]
        [MaxLength(50)]
        public string? DatumIstekaObavezan { get; set; }

        /// <summary>
        /// Period isteka u mesecima
        /// </summary>
        [Column("PeriodIstekaMeseci")]
        [MaxLength(50)]
        public string? PeriodIstekaMeseci { get; set; }

        /// <summary>
        /// Period obnove u mesecima
        /// </summary>
        [Column("PeriodObnoveMeseci")]
        [MaxLength(50)]
        public string? PeriodObnoveMeseci { get; set; }

        /// <summary>
        /// Period čuvanja u mesecima
        /// </summary>
        [Column("PeriodCuvanjaMeseci")]
        [MaxLength(50)]
        public string? PeriodCuvanjaMeseci { get; set; }

        /// <summary>
        /// Kreator zapisa
        /// </summary>
        [Column("Kreator")]
        [MaxLength(255)]
        public string? Kreator { get; set; }

        /// <summary>
        /// Datum kreiranja zapisa
        /// </summary>
        [Column("DatumKreiranja")]
        public DateTime? DatumKreiranja { get; set; }

        /// <summary>
        /// Datum izmene zapisa
        /// </summary>
        [Column("DatumIzmene")]
        public DateTime? DatumIzmene { get; set; }

        /// <summary>
        /// Da li je zapis aktivan
        /// </summary>
        [Column("Aktivan")]
        [MaxLength(50)]
        public string? Aktivan { get; set; }
    }
}
