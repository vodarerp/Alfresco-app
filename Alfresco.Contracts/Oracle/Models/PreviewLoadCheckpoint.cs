using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Alfresco.Contracts.Oracle.Models
{
    [Table("PreviewLoadCheckpoint")]
    public class PreviewLoadCheckpoint
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// Tip dosijea koji se učitava: "PI" ili "LE"
        /// </summary>
        public string FolderType { get; set; } = string.Empty;

        /// <summary>
        /// Ukupan broj dokumenata fetchovanih sa Alfresca za ovaj tip dosijea.
        /// Koristi se za resume - generiše skipValues od ove vrednosti.
        /// </summary>
        public long TotalFetched { get; set; }

        /// <summary>
        /// Vreme poslednjeg ažuriranja
        /// </summary>
        public DateTime UpdatedAt { get; set; }
    }
}
