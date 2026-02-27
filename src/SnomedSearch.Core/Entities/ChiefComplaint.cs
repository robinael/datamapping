using System.Collections.Generic;

namespace SnomedSearch.Core.Entities
{
    public class ChiefComplaint
    {
        public long ConceptId { get; set; }
        public string PreferredTerm { get; set; }
        public string SemanticTag { get; set; }
        // Add other fields if necessary, but these are the ones typically used in search results.
    }
}
