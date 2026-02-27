using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SnomedSearch.Core.Entities;

namespace SnomedSearch.Core.Interfaces
{
    public interface ISnomedRepository : IDisposable
    {
        Task<List<ConceptSummary>> SearchChiefComplaintsAsync(
            string query, 
            int limit = 20, 
            List<string> semanticTags = null, 
            bool useSemantic = true);

        Task<SnomedSearch.Core.Common.PagedResult<ChiefComplaint>> SearchChiefComplaintsPagedAsync(
            string searchTerm, 
            int page, 
            int pageSize, 
            System.Threading.CancellationToken cancellationToken = default);

        Task<Concept> GetConceptDetailsAsync(long conceptId);
        Task<HierarchyResponse> GetChildrenAsync(long conceptId, int limit = 50);
        Task<HierarchyResponse> GetParentsAsync(long conceptId);
        Task<List<string>> GetSynonymsAsync(long conceptId);
        Task<Dictionary<string, int>> GetSemanticTagStatsAsync();
    }

    public class HierarchyResponse
    {
        public long ConceptId { get; set; }
        public string PreferredTerm { get; set; }
        public List<ConceptSummary> Items { get; set; } = new List<ConceptSummary>();
        public int Total => Items.Count;
    }
}
