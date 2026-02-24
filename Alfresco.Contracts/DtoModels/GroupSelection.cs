namespace Alfresco.Contracts.DtoModels
{
    public class GroupSelection
    {
        public string BaseNaziv { get; set; } = "";

        /// <summary>
        /// null = uzmi sve varijante, "5" = matchuj "BaseNaziv 5*", "51" = matchuj "BaseNaziv 51*"
        /// </summary>
        public string? InvoiceFilter { get; set; }

        public int VariantCount { get; set; }

        /// <summary>
        /// Alfresco AFTS wildcard pattern (BEZ = prefiksa — podržava wildcard).
        /// </summary>
        public string ToAlfrescoPattern() =>
            InvoiceFilter == null
                ? $"{BaseNaziv} *"
                : $"{BaseNaziv} {InvoiceFilter}*";

        public string DisplayName =>
            InvoiceFilter == null
                ? $"{BaseNaziv} (sve ~{VariantCount:N0} var.)"
                : $"{BaseNaziv} {InvoiceFilter}* (filter)";
    }
}
