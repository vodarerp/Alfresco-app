namespace Alfresco.Contracts.DtoModels
{
    public class DocumentGroupType
    {
        public string BaseNaziv { get; set; } = "";
        public int VariantCount { get; set; }
        public long TotalDocuments { get; set; }
        public string? TipDosijea { get; set; }
    }
}
