using System.Collections.Generic;
using System.Threading.Tasks;

namespace SnomedSearch.Core.Interfaces
{
    public interface IAIService
    {
        Task<List<string>> GetClinicalTermsAsync(string query, int maxSuggestions = 5);
    }
}
