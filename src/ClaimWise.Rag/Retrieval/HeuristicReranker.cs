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
        // One group per query word (a compound like "co-payment" is satisfied by any of its variants).
        var queryGroups = TextAnalysis.TermGroups(query)
            .Select(g => g.Select(DomainSynonyms.Fold).Distinct().ToArray())
            .DistinctBy(g => string.Join('|', g.Order()))
            .ToList();
        var queryBigrams = Bigrams(TextAnalysis.Terms(query).Select(DomainSynonyms.Fold).ToList());
        var queryCodes = queryGroups.SelectMany(g => g).Where(TextAnalysis.IsCode).Distinct().ToList();
        var maxFused = candidates.Count == 0 ? 1 : Math.Max(candidates.Max(c => c.FusedScore), double.Epsilon);

        double GroupWeight(string[] g) => g.Max(index.SmoothedIdf);
        var totalWeight = queryGroups.Sum(GroupWeight);
        var outputs = new List<RerankOutput>(candidates.Count);

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var docTerms = TextAnalysis.Terms(candidate.Chunk.IndexText).Select(DomainSynonyms.Fold).ToList();
            var docSet = docTerms.ToHashSet();
            // A clause opens with its own short title ("2.1 Annual outpatient limit. ..."), which is as informative as
            // the section heading, so both count towards the "title" signal.
            var titleSet = TextAnalysis.Terms(candidate.Chunk.Heading ?? "").Concat(TextAnalysis.Terms(ClauseTitle(candidate.Chunk.Text)))
                .Select(DomainSynonyms.Fold).ToHashSet();

            double coverage = totalWeight == 0 ? 0 : queryGroups.Where(g => g.Any(docSet.Contains)).Sum(GroupWeight) / totalWeight;
            double headingCoverage = totalWeight == 0 ? 0 : queryGroups.Where(g => g.Any(titleSet.Contains)).Sum(GroupWeight) / totalWeight;

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

    /// <summary>The first sentence of a passage when it is short enough to be a title, without any clause number.</summary>
    internal static string ClauseTitle(string text)
    {
        var end = text.IndexOfAny(['.', ':']);
        // skip the dots inside a clause number such as "2.1"
        while (end >= 0 && end + 1 < text.Length && char.IsDigit(text[end + 1]) && end > 0 && char.IsDigit(text[end - 1]))
            end = text.IndexOfAny(['.', ':'], end + 1);
        if (end < 0) return "";
        var first = text[..end].Trim();
        first = System.Text.RegularExpressions.Regex.Replace(first, @"^\d+(\.\d+)*\s*", "");
        return TextAnalysis.WordCount(first) is >= 1 and <= 8 ? first : "";
    }

    private static HashSet<string> Bigrams(IReadOnlyList<string> terms)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i + 1 < terms.Count; i++) set.Add(terms[i] + " " + terms[i + 1]);
        return set;
    }
}
