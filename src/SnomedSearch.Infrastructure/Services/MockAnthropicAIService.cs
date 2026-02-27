using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using SnomedSearch.Core.Interfaces;

namespace SnomedSearch.Infrastructure.Services
{
    public class MockAnthropicAIService : IAIService
    {
        // In a real scenario, this would call AWS Bedrock or Anthropic API.
        // For this implementation, we return some hardcoded clinical terms based on common patient descriptions.
        public Task<List<string>> GetClinicalTermsAsync(string query, int maxSuggestions = 5)
        {
            var mapping = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "stomach ache", new List<string> { "Abdominal pain", "Epigastric pain", "Stomach ache" } },
                { "can't speak", new List<string> { "Aphasia", "Dysarthria", "Difficulty speaking" } },
                { "head pain", new List<string> { "Headache", "Cranial pain" } },
                { "hedake", new List<string> { "Headache" } }
            };

            if (mapping.TryGetValue(query.Trim(), out var clinicalTerms))
            {
                return Task.FromResult(clinicalTerms);
            }

            // Default to returning the query itself as a suggestion if no mapping found
            return Task.FromResult(new List<string> { query });
        }
    }
}
