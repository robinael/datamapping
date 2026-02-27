namespace SnomedSearch.Core.Entities
{
    /// <summary>
    /// Represents the search results from 'v_searchable' view.
    /// </summary>
    public class SearchableDescription
    {
        public long DescriptionId { get; set; }
        public long ConceptId { get; set; }
        public string Term { get; set; }
        public long TypeId { get; set; }
        public bool ConceptActive { get; set; }
        public string SemanticTag { get; set; }
    }
}
