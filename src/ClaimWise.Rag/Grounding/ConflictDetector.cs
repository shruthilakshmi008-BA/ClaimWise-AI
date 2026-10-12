using System.Globalization;
using System.Text.RegularExpressions;
using ClaimWise.Rag.Embeddings;

namespace ClaimWise.Rag.Grounding;

/// <summary>
/// Finds passages that appear to disagree about the same topic, so the reviewer sees the disagreement instead of
/// the system silently choosing a side. Two checks, both only between passages of the same policy family:
/// (1) <b>value conflicts</b>: near-identical wording but different percentages, durations or amounts;
/// (2) <b>coverage conflicts</b>: one passage grants cover for a topic another excludes.
/// It is a heuristic: it flags candidates for a human, and it does not decide which passage is right.
/// </summary>
public sealed partial class ConflictDetector
{
    private const double TopicSimilarityForValues = 0.45;
    private const double TopicSimilarityForPolarity = 0.35;

    [GeneratedRegex(@"\b[A-Za-z]{1,6}-\d+\b")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"\b\d{1,2}\s+(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4}\b|\b(?:19|20)\d{2}\b|\bVersion\s+[\d.]+\b|\b\d+\.\d+\b(?=\s*[.)])", RegexOptions.IgnoreCase)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"(?<![\w.])(\d{1,3}(?:,\d{3})+|\d+(?:\.\d+)?)\s*(%|percent|business\s+days?|days?|months?|years?|hours?|sessions?|visits?)?", RegexOptions.IgnoreCase)]
    private static partial Regex QuantityPattern();

    [GeneratedRegex(@"\b(?:not\s+covered|is\s+excluded|are\s+excluded|excluded|exclusion|not\s+eligible|does\s+not\s+cover|not\s+payable|declined|not\s+reimbursed)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NegativeCue();

    [GeneratedRegex(@"\b(?:is\s+covered|are\s+covered|covered|reimbursed|payable|eligible|the\s+plan\s+pays)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PositiveCue();

    private static readonly HashSet<string> PolarityWords =
        ["covered","cover","exclud","exclusion","eligible","payable","reimburs","declined","plan","pay","member","policy","year"];

    public IReadOnlyList<EvidenceConflict> Detect(IReadOnlyList<EvidencePassage> passages)
    {
        var conflicts = new List<EvidenceConflict>();
        for (int i = 0; i < passages.Count; i++)
        for (int j = i + 1; j < passages.Count; j++)
        {
            var a = passages[i];
            var b = passages[j];
            if (!SameFamily(a, b)) continue;

            var termsA = ContentTerms(a);
            var termsB = ContentTerms(b);
            var similarity = Jaccard(termsA, termsB);

            if (similarity >= TopicSimilarityForValues)
            {
                var diff = DescribeValueDifference(a, b);
                if (diff is not null)
                {
                    conflicts.Add(new EvidenceConflict("ValueConflict", [a.Label, b.Label],
                        $"{a.Label} and {b.Label} address the same topic but give different values: {diff}." + VersionNote(a, b)));
                    continue;
                }
            }

            if (similarity >= TopicSimilarityForPolarity)
            {
                var shared = termsA.Intersect(termsB).Count(t => !PolarityWords.Contains(t));
                if (shared >= 2 && Polarity(a.Chunk.Text) * Polarity(b.Chunk.Text) < 0)
                {
                    conflicts.Add(new EvidenceConflict("CoverageConflict", [a.Label, b.Label],
                        $"{a.Label} and {b.Label} address the same topic, but one grants cover and the other excludes it." + VersionNote(a, b)));
                }
            }
        }
        return conflicts;
    }

    private static bool SameFamily(EvidencePassage a, EvidencePassage b)
    {
        if (a.Chunk.DocumentId == b.Chunk.DocumentId) return true;
        return !string.IsNullOrWhiteSpace(a.Chunk.PolicyFamily) &&
               string.Equals(a.Chunk.PolicyFamily, b.Chunk.PolicyFamily, StringComparison.OrdinalIgnoreCase);
    }

    private static string VersionNote(EvidencePassage a, EvidencePassage b) =>
        a.Chunk.DocumentId != b.Chunk.DocumentId
            ? $" They come from different documents of the same policy ({a.Chunk.DocumentTitle}; {b.Chunk.DocumentTitle}); check version and effective date."
            : "";

    private static HashSet<string> ContentTerms(EvidencePassage p) =>
        TextAnalysis.Terms(StripNumbers(p.Chunk.Text))
            .Select(DomainSynonyms.Fold)
            .Where(t => !char.IsDigit(t[0]))
            .ToHashSet();

    private static string StripNumbers(string text) => DatePattern().Replace(CodePattern().Replace(text, " "), " ");

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var inter = a.Intersect(b).Count();
        return inter / (double)(a.Count + b.Count - inter);
    }

    private static int Polarity(string text)
    {
        bool neg = NegativeCue().IsMatch(text);
        bool pos = PositiveCue().IsMatch(text);
        // A passage that both grants and excludes ("excluded unless ... covered surgery") is not a clean signal.
        if (neg == pos) return 0;
        return neg ? -1 : 1;
    }

    private static string? DescribeValueDifference(EvidencePassage a, EvidencePassage b)
    {
        var qa = Quantities(a.Chunk.Text);
        var qb = Quantities(b.Chunk.Text);
        var parts = new List<string>();
        foreach (var kind in qa.Keys.Intersect(qb.Keys))
        {
            if (qa[kind].Overlaps(qb[kind])) continue;
            parts.Add($"{a.Label} states {Format(qa[kind], kind)} while {b.Label} states {Format(qb[kind], kind)}");
        }
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private static Dictionary<string, HashSet<decimal>> Quantities(string text)
    {
        var clean = StripNumbers(text);
        var result = new Dictionary<string, HashSet<decimal>>();
        foreach (Match m in QuantityPattern().Matches(clean))
        {
            var value = decimal.Parse(m.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);
            var unit = m.Groups[2].Value.ToLowerInvariant();
            string kind = unit switch
            {
                "%" or "percent" => "percent",
                "" => value >= 100 ? "amount" : "",
                var u when u.StartsWith("business") || u.StartsWith("day") => "days",
                var u when u.StartsWith("month") => "months",
                var u when u.StartsWith("year") => "years",
                var u when u.StartsWith("hour") => "hours",
                _ => "count"
            };
            if (kind.Length == 0) continue;
            if (!result.TryGetValue(kind, out var set)) result[kind] = set = [];
            set.Add(value);
        }
        return result;
    }

    private static string Format(HashSet<decimal> values, string kind)
    {
        var text = string.Join(" / ", values.Order().Select(v => v.ToString("#,##0.##", CultureInfo.InvariantCulture)));
        return kind switch { "percent" => text + "%", "amount" => text, _ => $"{text} {kind}" };
    }
}
