using ClaimWise.Rag.Embeddings;

namespace ClaimWise.Rag.Retrieval;

/// <summary>
/// Feature-based reranker (not a cross-encoder). Scores each candidate in [0,1] from:
/// idf-weighted coverage of query terms, matched adjacent term pairs (phrases), exact code matches, how much of the
/// query the section heading explains, and the first-stage fused score as a weak prior. Coverage is also what the
/// evidence-sufficiency check uses, so a passage that only matches generic words cannot look relevant.
/// </summary>
public sealed class HeuristicReranker : IReranker
{
    public Task<IReadOnlyList<RerankOutput>> RerankAsync(
        string query, IReadOnlyList<RerankInput> candidates, PolicyIndex index, CancellationToken ct)
    {
        var queryTerms = TextAnalysis.Terms(query).Select(DomainSynonyms.Fold).Distinct().ToList();
        var queryBigrams = Bigrams(TextAnalysis.Terms(query).Select(DomainSynonyms.Fold).ToList());
        var queryCodes = queryTerms.Where(TextAnalysis.IsCode).ToList();
        var maxFused = candidates.Count == 0 ? 1 : Math.Max(candidates.Max(c => c.FusedScore), double.Epsilon);

        var totalWeight = queryTerms.Sum(index.SmoothedIdf);
        var outputs = new List<RerankOutput>(candidates.Count);

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var docTerms = TextAnalysis.Terms(candidate.Chunk.IndexText).Select(DomainSynonyms.Fold).ToList();
            var docSet = docTerms.ToHashSet();
            var headingSet = TextAnalysis.Terms(candidate.Chunk.Heading ?? "").Select(DomainSynonyms.Fold).ToHashSet();

            double coverage = totalWeight == 0 ? 0 : queryTerms.Where(docSet.Contains).Sum(index.SmoothedIdf) / totalWeight;
            double headingCoverage = totalWeight == 0 ? 0 : queryTerms.Where(headingSet.Contains).Sum(index.SmoothedIdf) / totalWeight;

            double phrase = 0;
            if (queryBigrams.Count > 0)
            {
                var docBigrams = Bigrams(docTerms);
                phrase = queryBigrams.Count(docBigrams.Contains) / (double)queryBigrams.Count;
            }

            double prior = candidate.FusedScore / maxFused;

            double score;
            if (queryCodes.Count > 0)
            {
                double code = queryCodes.Count(docSet.Contains) / (double)queryCodes.Count;
                score = 0.40 * coverage + 0.15 * phrase + 0.10 * headingCoverage + 0.10 * prior + 0.25 * code;
            }
            else
            {
                score = 0.50 * coverage + 0.18 * phrase + 0.12 * headingCoverage + 0.20 * prior;
            }

            outputs.Add(new RerankOutput(candidate.Chunk, candidate.FusedScore, Math.Clamp(score, 0, 1), coverage));
        }

        IReadOnlyList<RerankOutput> ordered = outputs
            .OrderByDescending(o => o.Score).ThenByDescending(o => o.FusedScore).ThenBy(o => o.Chunk.Id)
            .ToList();
        return Task.FromResult(ordered);
    }

    private static HashSet<string> Bigrams(IReadOnlyList<string> terms)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i + 1 < terms.Count; i++) set.Add(terms[i] + " " + terms[i + 1]);
        return set;
    }
}
