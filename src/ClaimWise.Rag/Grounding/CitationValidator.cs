using System.Text.RegularExpressions;

namespace ClaimWise.Rag.Grounding;

public sealed record ValidationOptions
{
    public int MinQuoteWords { get; init; } = 3;
    public int MaxStatementChars { get; init; } = 700;

    /// <summary>Share of the statement's content terms that must appear in the passages it cites.</summary>
    public double MinSupportOverlap { get; init; } = 0.30;
}

public sealed record ValidationReport(IReadOnlyList<SummaryStatement> Accepted, IReadOnlyList<RejectedStatement> Rejected);

/// <summary>
/// Verifies a draft against the passages that were actually retrieved. A statement is kept only if every citation
/// names a real passage and quotes it verbatim, every figure in the statement appears in the cited text (or the claim
/// facts), the statement shares real content with its sources, and it does not read as a decision. Citations are never
/// repaired or invented: a failing statement is dropped and reported.
/// </summary>
public sealed partial class CitationValidator(ValidationOptions? options = null)
{
    private readonly ValidationOptions _o = options ?? new ValidationOptions();

    [GeneratedRegex(@"\d[\d,]*(?:\.\d+)?")]
    private static partial Regex NumberPattern();

    // The summary is evidence for a human. Recommending or announcing a decision is outside its remit.
    [GeneratedRegex(
        @"\b(?:claim|request|application)\b[^.]{0,30}\b(?:should|must|can|will|could|ought\s+to)\s+be\s+(?:approved|denied|rejected|declined|paid)\b" +
        @"|\b(?:claim|request|application)\s+(?:is|was|has\s+been|is\s+hereby)\s+(?:approved|denied|rejected|declined)\b" +
        @"|\b(?:approve|deny|reject|decline)\s+(?:this|the|that)\s+(?:claim|request)\b" +
        @"|\b(?:should|must|recommend(?:ed|s|ing)?\s+(?:to|that\s+\w+\s+)?)\s*(?:approve|deny|reject|decline)\b" +
        @"|\bI\s+recommend\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex DecisionLanguage();

    public ValidationReport Validate(DraftSummary draft, IReadOnlyList<EvidencePassage> passages, ClaimContext claim)
    {
        var byLabel = passages.ToDictionary(p => p.Label, StringComparer.OrdinalIgnoreCase);
        var claimNumbers = ClaimNumbers(claim);
        var accepted = new List<SummaryStatement>();
        var rejected = new List<RejectedStatement>();

        foreach (var statement in draft.Statements)
        {
            var reason = Check(statement, byLabel, claimNumbers, out var citations);
            if (reason is null)
                accepted.Add(new SummaryStatement(TextAnalysis.NormalizeWhitespace(statement.Text), citations, StatementOrigin.Model));
            else
                rejected.Add(new RejectedStatement(statement.Text, reason));
        }

        return new ValidationReport(accepted, rejected);
    }

    private string? Check(
        DraftStatement s,
        Dictionary<string, EvidencePassage> byLabel,
        HashSet<string> claimNumbers,
        out IReadOnlyList<Citation> citations)
    {
        citations = [];
        var text = TextAnalysis.NormalizeWhitespace(s.Text ?? "");
        if (text.Length == 0) return "Empty statement.";
        if (text.Length > _o.MaxStatementChars) return "Statement is too long.";
        if (DecisionLanguage().IsMatch(text)) return "Statement recommends or announces a claim decision, which only a human reviewer may do.";
        if (s.Citations is null || s.Citations.Count == 0) return "No citation.";

        var verified = new List<Citation>();
        var citedText = new List<string>();
        foreach (var c in s.Citations)
        {
            if (!byLabel.TryGetValue((c.PassageLabel ?? "").Trim(), out var passage))
                return $"Cites unknown passage '{c.PassageLabel}'.";

            var quote = TextAnalysis.NormalizeForComparison(c.Quote ?? "");
            if (TextAnalysis.WordCount(quote) < _o.MinQuoteWords)
                return $"Quote for {passage.Label} is missing or too short to verify.";

            var source = TextAnalysis.NormalizeForComparison(passage.Chunk.Text);
            if (!source.Contains(quote, StringComparison.Ordinal))
                return $"Quote for {passage.Label} does not appear verbatim in that passage.";

            verified.Add(new Citation(passage.Label, TextAnalysis.NormalizeWhitespace(c.Quote!)));
            citedText.Add(passage.Chunk.Text);
        }

        var sourceNumbers = citedText.SelectMany(Numbers).ToHashSet();
        foreach (var n in Numbers(text))
            if (!sourceNumbers.Contains(n) && !claimNumbers.Contains(n))
                return $"The figure '{n}' does not appear in the cited passages or the claim facts.";

        var statementTerms = TextAnalysis.Terms(text).Where(t => !char.IsDigit(t[0])).Distinct().ToList();
        if (statementTerms.Count > 0)
        {
            var sourceTerms = citedText.SelectMany(t => TextAnalysis.Terms(t)).ToHashSet();
            var overlap = statementTerms.Count(sourceTerms.Contains) / (double)statementTerms.Count;
            if (overlap < _o.MinSupportOverlap)
                return $"Only {overlap:P0} of the statement's wording is found in the cited passages.";
        }

        citations = verified;
        return null;
    }

    // Compared numerically ("1850.00" == "1850" == "1,850"), not as text.
    private static IEnumerable<string> Numbers(string text)
    {
        foreach (Match m in NumberPattern().Matches(text))
        {
            var raw = m.Value.Replace(",", "").TrimEnd('.');
            if (decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value))
                yield return value.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static HashSet<string> ClaimNumbers(ClaimContext claim)
    {
        var set = Numbers(claim.AmountClaimed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
        foreach (var field in new[] { claim.ServiceDate, claim.ProcedureCode, claim.DiagnosisCode, claim.PolicyNumber, claim.ClaimNumber })
            if (field is not null)
                foreach (var n in Numbers(field)) set.Add(n);
        return set;
    }
}
