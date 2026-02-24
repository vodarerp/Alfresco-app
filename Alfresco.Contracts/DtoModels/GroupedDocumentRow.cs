namespace Alfresco.Contracts.DtoModels
{
    /// <summary>
    /// DTO koji vraća GetGroupedViewAsync — objedinjuje GROUP i SINGLE redove iz SQL-a.
    /// GROUP red: dokument čiji NAZIV prati "BaseNaziv &lt;broj&gt;" pattern, 2+ varijanti.
    /// SINGLE red: svi ostali (bez numeričkog sufiksa, ili singleton).
    /// </summary>
    public class GroupedDocumentRow
    {
        /// <summary>"GROUP" ili "SINGLE"</summary>
        public string RowType { get; set; } = "";

        public string DisplayNaziv { get; set; } = "";
        public int VariantCount { get; set; }
        public long TotalDocuments { get; set; }
        public string? TipDosijea { get; set; }

        /// <summary>Null za GROUP redove.</summary>
        public string? SifraDokumenta { get; set; }

        /// <summary>Null za GROUP redove.</summary>
        public int? Id { get; set; }

        /// <summary>Ukupan broj redova u result setu (za paginaciju).</summary>
        public int TotalCount { get; set; }
    }
}
