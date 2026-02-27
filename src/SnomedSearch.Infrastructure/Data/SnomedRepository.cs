using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using SnomedSearch.Core.Entities;
using SnomedSearch.Core.Interfaces;

namespace SnomedSearch.Infrastructure.Data
{
    public class SnomedRepository : ISnomedRepository
    {
        private readonly string _connectionString;
        private IDbConnection _connection;
        private readonly IAIService _aiService;

        public SnomedRepository(string connectionString, IAIService aiService = null)
        {
            _connectionString = connectionString;
            _aiService = aiService;
        }

        private IDbConnection GetConnection()
        {
            if (_connection == null || _connection.State == ConnectionState.Closed)
            {
                _connection = new NpgsqlConnection(_connectionString);
                _connection.Open();
            }
            return _connection;
        }

        public async Task<List<ConceptSummary>> SearchChiefComplaintsAsync(
            string query, 
            int limit = 20, 
            List<string> semanticTags = null, 
            bool useSemantic = true)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<ConceptSummary>();

            semanticTags ??= new List<string> { "finding" };
            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                             .Select(w => w.Trim())
                             .ToList();

            if (!words.Any()) return new List<ConceptSummary>();

            // 1. Exact/Word-order independent search
            var results = await SearchExactWordsAsync(query, words, semanticTags, limit);

            // 2. Fuzzy search if no results
            if (!results.Any())
            {
                results = await SearchFuzzyAsync(query, semanticTags, limit);
            }

            // 3. Semantic search if still no results and enabled
            if (!results.Any() && useSemantic && _aiService != null)
            {
                results = await SearchSemanticAsync(query, semanticTags, limit);
            }

            return results;
        }

        private async Task<List<ConceptSummary>> SearchExactWordsAsync(
            string query, 
            List<string> words, 
            List<string> semanticTags, 
            int limit)
        {
            var wordConditions = string.Join(" AND ", words.Select((w, i) => $"d.term ILIKE @word{i}"));
            var parameters = new DynamicParameters();
            parameters.Add("query", query);
            parameters.Add("queryPrefix", $"{query}%");
            parameters.Add("semanticTags", semanticTags);
            parameters.Add("limit", limit);

            for (int i = 0; i < words.Count; i++)
            {
                parameters.Add($"word{i}", $"%{words[i]}%");
            }

            string sql = $@"
            WITH matched_descriptions AS (
                SELECT
                    d.concept_id,
                    d.term AS matched_term,
                    st.semantic_tag,
                    st.fully_specified_name AS fsn,
                    CASE
                        WHEN d.term ILIKE @query THEN 0
                        WHEN d.term ILIKE @queryPrefix THEN 1
                        ELSE 2
                    END AS match_rank,
                    LENGTH(d.term) AS term_length
                FROM snomed.description d
                JOIN snomed.concept c ON d.concept_id = c.concept_id
                LEFT JOIN snomed.semantic_tag st ON d.concept_id = st.concept_id
                WHERE d.active = true
                  AND c.active = true
                  AND ({wordConditions})
                  AND st.semantic_tag = ANY(@semanticTags)
            ),
            ranked_concepts AS (
                SELECT DISTINCT ON (concept_id)
                    concept_id,
                    matched_term,
                    semantic_tag,
                    fsn,
                    match_rank,
                    term_length
                FROM matched_descriptions
                ORDER BY concept_id, match_rank, term_length
            )
            SELECT
                rc.concept_id AS ConceptId,
                rc.matched_term AS PreferredTerm,
                rc.semantic_tag AS SemanticTag,
                (SELECT COUNT(*)
                 FROM snomed.relationship r
                 WHERE r.destination_id = rc.concept_id
                   AND r.type_id = 116680003
                   AND r.active = true) AS ChildrenCount
            FROM ranked_concepts rc
            ORDER BY rc.match_rank, rc.term_length, rc.matched_term
            LIMIT @limit;";

            var conn = GetConnection();
            return (await conn.QueryAsync<ConceptSummary>(sql, parameters)).ToList();
        }

        private async Task<List<ConceptSummary>> SearchFuzzyAsync(
            string query, 
            List<string> semanticTags, 
            int limit)
        {
            float minSimilarity = 0.3f;
            var sql = @"
            WITH fuzzy_matches AS (
                SELECT
                    d.concept_id,
                    d.term AS matched_term,
                    st.semantic_tag,
                    st.fully_specified_name AS fsn,
                    similarity(d.term, @query) AS sim_score,
                    LENGTH(d.term) AS term_length
                FROM snomed.description d
                JOIN snomed.concept c ON d.concept_id = c.concept_id
                LEFT JOIN snomed.semantic_tag st ON d.concept_id = st.concept_id
                WHERE d.active = true
                  AND c.active = true
                  AND st.semantic_tag = ANY(@semanticTags)
                  AND d.term % @query
            ),
            ranked_concepts AS (
                SELECT DISTINCT ON (concept_id)
                    concept_id,
                    matched_term,
                    semantic_tag,
                    fsn,
                    sim_score,
                    term_length
                FROM fuzzy_matches
                WHERE sim_score >= @minSimilarity
                ORDER BY concept_id, sim_score DESC, term_length
            )
            SELECT
                rc.concept_id AS ConceptId,
                rc.matched_term AS PreferredTerm,
                rc.semantic_tag AS SemanticTag,
                (SELECT COUNT(*)
                 FROM snomed.relationship r
                 WHERE r.destination_id = rc.concept_id
                   AND r.type_id = 116680003
                   AND r.active = true) AS ChildrenCount
            FROM ranked_concepts rc
            ORDER BY rc.sim_score DESC, rc.term_length, rc.matched_term
            LIMIT @limit;";

            var parameters = new { query, semanticTags, minSimilarity, limit };
            var conn = GetConnection();
            return (await conn.QueryAsync<ConceptSummary>(sql, parameters)).ToList();
        }

        private async Task<List<ConceptSummary>> SearchSemanticAsync(
            string query, 
            List<string> semanticTags, 
            int limit)
        {
            if (_aiService == null) return new List<ConceptSummary>();

            var clinicalTerms = await _aiService.GetClinicalTermsAsync(query);
            var allResults = new List<ConceptSummary>();
            var seenConcepts = new HashSet<long>();

            foreach (var term in clinicalTerms)
            {
                var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                var termResults = await SearchExactWordsAsync(term, words, semanticTags, 10);
                foreach (var r in termResults)
                {
                    if (seenConcepts.Add(r.ConceptId))
                    {
                        allResults.Add(r);
                    }
                }
            }

            return allResults.Take(limit).ToList();
        }

        public async Task<Concept> GetConceptDetailsAsync(long conceptId)
        {
            var sql = @"
            SELECT
                c.concept_id AS ConceptId,
                c.active AS Active,
                st.fully_specified_name AS Fsn,
                st.semantic_tag AS SemanticTag
            FROM snomed.concept c
            LEFT JOIN snomed.semantic_tag st ON c.concept_id = st.concept_id
            WHERE c.concept_id = @conceptId AND c.active = true;";

            var conn = GetConnection();
            var concept = await conn.QueryFirstOrDefaultAsync<Concept>(sql, new { conceptId });

            if (concept == null) return null;

            concept.Synonyms = await GetSynonymsAsync(conceptId);
            concept.PreferredTerm = concept.Synonyms.FirstOrDefault() ?? concept.Fsn;

            var defSql = @"
            SELECT term FROM snomed.text_definition
            WHERE concept_id = @conceptId AND active = true
            LIMIT 1;";
            concept.Definition = await conn.QueryFirstOrDefaultAsync<string>(defSql, new { conceptId });

            var parentsResult = await GetParentsAsync(conceptId);
            concept.Parents = parentsResult.Items;

            var childrenSql = @"
            SELECT COUNT(*) FROM snomed.relationship
            WHERE destination_id = @conceptId AND type_id = 116680003 AND active = true;";
            concept.ChildrenCount = await conn.ExecuteScalarAsync<int>(childrenSql, new { conceptId });

            return concept;
        }

        public async Task<HierarchyResponse> GetChildrenAsync(long conceptId, int limit = 50)
        {
            var conn = GetConnection();
            var parentTermSql = @"
            SELECT d.term FROM snomed.description d
            WHERE d.concept_id = @conceptId
              AND d.type_id = 900000000000013009
              AND d.active = true
            LIMIT 1;";
            var parentTerm = await conn.QueryFirstOrDefaultAsync<string>(parentTermSql, new { conceptId }) ?? conceptId.ToString();

            var sql = @"
            SELECT
                c.concept_id AS ConceptId,
                (SELECT d.term FROM snomed.description d
                 WHERE d.concept_id = c.concept_id
                   AND d.type_id = 900000000000013009
                   AND d.active = true
                 LIMIT 1) AS PreferredTerm,
                st.semantic_tag AS SemanticTag,
                (SELECT COUNT(*) FROM snomed.relationship r2
                 WHERE r2.destination_id = c.concept_id
                   AND r2.type_id = 116680003
                   AND r2.active = true) AS ChildrenCount
            FROM snomed.relationship r
            JOIN snomed.concept c ON r.source_id = c.concept_id
            LEFT JOIN snomed.semantic_tag st ON c.concept_id = st.concept_id
            WHERE r.destination_id = @conceptId
              AND r.type_id = 116680003
              AND r.active = true
              AND c.active = true
            ORDER BY PreferredTerm
            LIMIT @limit;";

            var items = (await conn.QueryAsync<ConceptSummary>(sql, new { conceptId, limit })).ToList();
            return new HierarchyResponse { ConceptId = conceptId, PreferredTerm = parentTerm, Items = items };
        }

        public async Task<HierarchyResponse> GetParentsAsync(long conceptId)
        {
            var conn = GetConnection();
            var childTermSql = @"
            SELECT d.term FROM snomed.description d
            WHERE d.concept_id = @conceptId
              AND d.type_id = 900000000000013009
              AND d.active = true
            LIMIT 1;";
            var childTerm = await conn.QueryFirstOrDefaultAsync<string>(childTermSql, new { conceptId }) ?? conceptId.ToString();

            var sql = @"
            SELECT
                c.concept_id AS ConceptId,
                (SELECT d.term FROM snomed.description d
                 WHERE d.concept_id = c.concept_id
                   AND d.type_id = 900000000000013009
                   AND d.active = true
                 LIMIT 1) AS PreferredTerm,
                st.semantic_tag AS SemanticTag,
                (SELECT COUNT(*) FROM snomed.relationship r2
                 WHERE r2.destination_id = c.concept_id
                   AND r2.type_id = 116680003
                   AND r2.active = true) AS ChildrenCount
            FROM snomed.relationship r
            JOIN snomed.concept c ON r.destination_id = c.concept_id
            LEFT JOIN snomed.semantic_tag st ON c.concept_id = st.concept_id
            WHERE r.source_id = @conceptId
              AND r.type_id = 116680003
              AND r.active = true
              AND c.active = true
            ORDER BY PreferredTerm;";

            var items = (await conn.QueryAsync<ConceptSummary>(sql, new { conceptId })).ToList();
            return new HierarchyResponse { ConceptId = conceptId, PreferredTerm = childTerm, Items = items };
        }

        public async Task<List<string>> GetSynonymsAsync(long conceptId)
        {
            var sql = @"
            SELECT DISTINCT d.term
            FROM snomed.description d
            WHERE d.concept_id = @conceptId
              AND d.active = true
              AND d.type_id = 900000000000013009
            ORDER BY d.term;";
            var conn = GetConnection();
            return (await conn.QueryAsync<string>(sql, new { conceptId })).ToList();
        }

        public async Task<Dictionary<string, int>> GetSemanticTagStatsAsync()
        {
            var sql = @"
            SELECT semantic_tag, COUNT(*) as count
            FROM snomed.semantic_tag
            WHERE semantic_tag IN ('finding', 'disorder', 'situation', 'procedure',
                                   'body structure', 'substance', 'organism',
                                   'observable entity', 'physical object')
            GROUP BY semantic_tag
            ORDER BY count DESC;";
            var conn = GetConnection();
            var results = await conn.QueryAsync(sql);
            return results.ToDictionary(
                row => (string)row.semantic_tag, 
                row => (int)(long)row.count // PostgreSQL COUNT returns bigint
            );
        }

        public async Task<SnomedSearch.Core.Common.PagedResult<ChiefComplaint>> SearchChiefComplaintsPagedAsync(
            string searchTerm, 
            int page, 
            int pageSize, 
            System.Threading.CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) 
                return new SnomedSearch.Core.Common.PagedResult<ChiefComplaint>();

            var semanticTags = new List<string> { "finding" };
            var words = searchTerm.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(w => w.Trim())
                                  .ToList();

            if (!words.Any()) return new SnomedSearch.Core.Common.PagedResult<ChiefComplaint>();

            // For simplicity in this conversion, we'll reuse the logic but wrap it for paging.
            // In a production app, we'd need a separate 'count' query for true paging.
            // For now, we'll execute the search with a slightly larger limit to 'simulate' paging or just use the page/pageSize in the SQL.
            
            var offset = (page - 1) * pageSize;
            
            // We need a total count. For SNOMED, getting exact total counts for fuzzy searches can be expensive.
            // I'll implement a paged version of SearchExactWords.

            var wordConditions = string.Join(" AND ", words.Select((w, i) => $"d.term ILIKE @word{i}"));
            var parameters = new DynamicParameters();
            parameters.Add("query", searchTerm);
            parameters.Add("queryPrefix", $"{searchTerm}%");
            parameters.Add("semanticTags", semanticTags);
            parameters.Add("limit", pageSize);
            parameters.Add("offset", offset);

            for (int i = 0; i < words.Count; i++)
            {
                parameters.Add($"word{i}", $"%{words[i]}%");
            }

            string sql = $@"
            WITH matched_descriptions AS (
                SELECT
                    d.concept_id,
                    d.term AS matched_term,
                    st.semantic_tag
                FROM snomed.description d
                JOIN snomed.concept c ON d.concept_id = c.concept_id
                LEFT JOIN snomed.semantic_tag st ON d.concept_id = st.concept_id
                WHERE d.active = true
                  AND c.active = true
                  AND ({wordConditions})
                  AND st.semantic_tag = ANY(@semanticTags)
            ),
            ranked_concepts AS (
                SELECT DISTINCT ON (concept_id)
                    concept_id,
                    matched_term,
                    semantic_tag
                FROM matched_descriptions
            )
            SELECT
                concept_id AS ConceptId,
                matched_term AS PreferredTerm,
                semantic_tag AS SemanticTag
            FROM ranked_concepts
            ORDER BY concept_id
            LIMIT @limit OFFSET @offset;

            SELECT COUNT(*) FROM (
                SELECT DISTINCT d.concept_id 
                FROM snomed.description d
                JOIN snomed.concept c ON d.concept_id = c.concept_id
                LEFT JOIN snomed.semantic_tag st ON d.concept_id = st.concept_id
                WHERE d.active = true
                  AND c.active = true
                  AND ({wordConditions})
                  AND st.semantic_tag = ANY(@semanticTags)
            ) AS total;";

            var conn = GetConnection();
            using var multi = await conn.QueryMultipleAsync(sql, parameters);
            var items = (await multi.ReadAsync<ChiefComplaint>()).ToList();
            var totalCount = await multi.ReadFirstAsync<int>();

            // If no exact results, try fuzzy search
            if (!items.Any())
            {
                float minSimilarity = 0.3f;
                string fuzzySql = @"
                WITH fuzzy_matches AS (
                    SELECT
                        d.concept_id,
                        d.term AS matched_term,
                        st.semantic_tag,
                        similarity(d.term, @query) AS sim_score
                    FROM snomed.description d
                    JOIN snomed.concept c ON d.concept_id = c.concept_id
                    LEFT JOIN snomed.semantic_tag st ON d.concept_id = st.concept_id
                    WHERE d.active = true
                      AND c.active = true
                      AND st.semantic_tag = ANY(@semanticTags)
                      AND d.term % @query
                ),
                ranked_concepts AS (
                    SELECT DISTINCT ON (concept_id)
                        concept_id,
                        matched_term,
                        semantic_tag,
                        sim_score
                    FROM fuzzy_matches
                    WHERE sim_score >= @minSimilarity
                )
                SELECT
                    concept_id AS ConceptId,
                    matched_term AS PreferredTerm,
                    semantic_tag AS SemanticTag
                FROM ranked_concepts
                ORDER BY sim_score DESC, concept_id
                LIMIT @limit OFFSET @offset;

                SELECT COUNT(*) FROM (
                    SELECT DISTINCT d.concept_id
                    FROM snomed.description d
                    JOIN snomed.concept c ON d.concept_id = c.concept_id
                    LEFT JOIN snomed.semantic_tag st ON d.concept_id = st.concept_id
                    WHERE d.active = true
                      AND c.active = true
                      AND st.semantic_tag = ANY(@semanticTags)
                      AND d.term % @query
                ) AS total;";

                var fuzzyParams = new { query = searchTerm, semanticTags, minSimilarity, limit = pageSize, offset };
                using var fuzzyMulti = await conn.QueryMultipleAsync(fuzzySql, fuzzyParams);
                items = (await fuzzyMulti.ReadAsync<ChiefComplaint>()).ToList();
                totalCount = await fuzzyMulti.ReadFirstAsync<int>();
            }

            return new SnomedSearch.Core.Common.PagedResult<ChiefComplaint>(items, totalCount, page, pageSize);
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}
