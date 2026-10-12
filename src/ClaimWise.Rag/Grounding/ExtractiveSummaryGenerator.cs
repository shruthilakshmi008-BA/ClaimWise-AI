using System.Text.RegularExpressions;
using ClaimWise.Rag.Embeddings;

namespace ClaimWise.Rag.Grounding;

/// <summary>
/// A summary generator that uses no language model: for each top passage it selects the sentence that best matches
/// the question and quotes it verbatim. It cannot paraphrase, hallucinate or mis-cite, which makes it the offline
/// default and a safe baseline. It is deliberately modest: it shows the reviewer the most relevant policy sentences.
/// </summary>
public sealed partial class ExtractiveSummaryGenerator(int maxStatements = 4) : ISummaryGenerator
{
    public string Name => "extractive";
    public string? ModelId => "extractive-v1";

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[\p{Lu}\p{N}(""'])")]
    private static partial Regex SentenceSplit();

    public Task<DraftSummary> GenerateAsync(SummaryRequest request, CancellationToken ct)
    {
        var queryTerms = TextAnalysis.Terms($"{request.Question} {request.Claim.ClaimType} {request.Claim.ProcedureCode} {request.Claim.DiagnosisCode}")
            .Select(DomainSynonyms.Fold).ToHashSet();

        var statements = new List<DraftStatement>();
        foreach (var passage in request.Passages.Take(maxStatements))
        {
            var sentence = BestSentence(passage.Chunk.Text, queryTerms);
            if (sentence is null || TextAnalysis.WordCount(sentence) < 3) continue;
            statements.Add(new DraftStatement(sentence, [new DraftCitation(passage.Label, sentence)]));
        }

        var draft = statements.Count == 0
            ? new DraftSummary(true, "No passage contained a sentence relevant enough to quote.", [])
            : new DraftSummary(false, null, statements);
        return Task.FromResult(draft);
    }

    private static string? BestSentence(string text, HashSet<string> queryTerms)
    {
        var sentences = SentenceSplit().Split(text).Select(TextAnalysis.NormalizeWhitespace).Where(s => s.Length > 0).ToList();
        if (sentences.Count == 0) return null;

        int best = 0;
        double bestScore = -1;
        for (int i = 0; i < sentences.Count; i++)
        {
            var terms = TextAnalysis.Terms(sentences[i]).Select(DomainSynonyms.Fold).ToList();
            if (terms.Count == 0) continue;
            double overlap = terms.Distinct().Count(queryTerms.Contains);
            // Mild preference for sentences carrying figures: that is usually what a reviewer needs.
            double score = overlap + (sentences[i].Any(char.IsDigit) ? 0.5 : 0) - 0.02 * terms.Count;
            if (score > bestScore) { bestScore = score; best = i; }
        }

        var chosen = sentences[best];
        // A bare title such as "2.1 Annual outpatient limit." is not informative on its own.
        if (TextAnalysis.WordCount(chosen) < 6 && best + 1 < sentences.Count)
            chosen = chosen + " " + sentences[best + 1];
        return chosen;
    }
}
