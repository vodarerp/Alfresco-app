namespace Alfresco.Contracts.DtoModels
{
    public class DocumentSelectionResult
    {
        /// <summary>
        /// Individualni dokumenti → exact match u Alfresco (=ecm:docDesc:"...").
        /// </summary>
        public List<string> ExactDescriptions { get; set; } = new();

        /// <summary>
        /// Grupne selekcije → wildcard match u Alfresco (ecm:docDesc:"BaseNaziv *").
        /// </summary>
        public List<GroupSelection> GroupSelections { get; set; } = new();

        public bool HasAny => ExactDescriptions.Any() || GroupSelections.Any();
        public int TotalSelectionCount => ExactDescriptions.Count + GroupSelections.Count;
    }
}
