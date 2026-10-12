using ClaimWise.Rag.Embeddings;
using ClaimWise.Rag.Retrieval;

namespace ClaimWise.Rag.Grounding;

public sealed record EvidenceOptions
{
    /// <summary>The best passage must reach this rerank score.</summary>
    public double MinTopScore { get; init; } = 0.35;

    /// <summary>A passage at or above this score counts as supporting evidence.</summary>
    public double SupportThreshold { get; init; } = 0.30;

    public int MinSupportingPassages { get; init; } = 1;

    /// <summary>Share (idf-weighted) of the question's terms that the supporting passages must contain together.</summary>
    public double MinCoverage { get; init; } = 0.50;
}

/// <summary>
/// Decides whether the retrieved passages are enough to answer from. When they are not, the pipeline abstains
/// <em>before</em> any generator runs, so a weak retrieval can never be dressed up as a confident answer.
/// </summary>
public sealed class EvidenceAssessor(EvidenceOptions? options = null)
{
    private readonly EvidenceOptions _o = options ?? new EvidenceOptions();

    public EvidenceOptions Options => _o;

    public EvidenceAssessment Assess(string query, IReadOnlyList<EvidencePassage> passages, PolicyIndex index)
    {
        var reasons = new List<string>();
        if (passages.Count == 0)
        {
            return new EvidenceAssessment(EvidenceVerdict.Insufficient,
                ["No policy passages matched the question."], 0, 0, 0, []);
        }

        var top = passages.Max(p => p.Retrieved.RerankScore);
        var supporting = passages.Where(p => p.Retrieved.RerankScore >= _o.SupportThreshold).ToList();

        // Identifier-like terms (procedure/diagnosis codes) sharpen ranking but are not what the question is about:
        // a code missing from the policies must not, on its own, make a well-covered topic look unsupported.
        var queryTerms = TextAnalysis.Terms(query).Select(DomainSynonyms.Fold).Where(t => !TextAnalysis.IsCode(t)).Distinct().ToList();
        var union = supporting
            .SelectMany(p => TextAnalysis.Terms(p.Chunk.IndexText).Select(DomainSynonyms.Fold))
            .ToHashSet();
        var total = queryTerms.Sum(index.SmoothedIdf);
        var coverage = total == 0 ? 0 : queryTerms.Where(union.Contains).Sum(index.SmoothedIdf) / total;
        var missing = queryTerms.Where(t => !union.Contains(t)).OrderByDescending(index.SmoothedIdf).Take(6).ToList();

        if (top < _o.MinTopScore)
            reasons.Add($"The best passage scored {top:0.00} for relevance, below the {_o.MinTopScore:0.00} needed.");
        if (supporting.Count < _o.MinSupportingPassages)
            reasons.Add($"Only {supporting.Count} passage(s) reached the support threshold of {_o.SupportThreshold:0.00}; {_o.MinSupportingPassages} needed.");
        if (coverage < _o.MinCoverage)
            reasons.Add($"The supporting passages cover only {coverage:P0} of the question's key terms ({_o.MinCoverage:P0} needed)" +
                        (missing.Count > 0 ? $"; not found: {string.Join(", ", missing)}." : "."));

        var verdict = reasons.Count == 0 ? EvidenceVerdict.Sufficient : EvidenceVerdict.Insufficient;
        return new EvidenceAssessment(verdict, reasons, top, coverage, supporting.Count, missing);
    }
}
