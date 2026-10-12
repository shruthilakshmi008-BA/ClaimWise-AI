using System.Text.RegularExpressions;

namespace ClaimWise.Rag;

/// <summary>
/// Deterministic tokenisation shared by keyword search, the local embedder, the reranker and grounding checks,
/// so they all agree on what a "term" is. Codes such as <c>PRC-101</c> or <c>J06.9</c> stay whole tokens so they
/// can be matched exactly.
/// </summary>
public static partial class TextAnalysis
{
    [GeneratedRegex(@"[\p{L}\p{N}]+(?:[.\-][\p{L}\p{N}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a","an","and","are","as","at","be","been","but","by","can","do","does","for","from","had","has","have","if","in",
        "into","is","it","its","may","not","of","on","or","shall","such","than","that","the","their","then","there",
        "these","this","those","to","under","was","were","will","with","within","would","you","your","per","any","all",
        "which","who","whom","what","when","where","how","also","other","each","more","most","only","same","so","no"
    };

    public static bool IsStopWord(string token) => StopWords.Contains(token);

    /// <summary>Lower-cased, stop-word-free, lightly stemmed terms. Hyphenated words also yield their parts.</summary>
    public static List<string> Terms(string text, bool removeStopWords = true)
    {
        var terms = new List<string>();
        foreach (Match m in TokenPattern().Matches(text))
        {
            var token = m.Value.ToLowerInvariant();
            if (removeStopWords && StopWords.Contains(token)) continue;

            if (token.Contains('-') && !token.Any(char.IsDigit))
            {
                // "pre-authorization" should also match "authorization".
                terms.Add(Stem(token));
                foreach (var part in token.Split('-', StringSplitOptions.RemoveEmptyEntries))
                    if (part.Length > 2 && !(removeStopWords && StopWords.Contains(part))) terms.Add(Stem(part));
            }
            else
            {
                terms.Add(IsCode(token) ? token : Stem(token));
            }
        }
        return terms;
    }

    /// <summary>
    /// Like <see cref="Terms"/> but one entry per source word: a hyphenated word such as "co-payment" is a single group
    /// whose alternatives are the whole word and its parts. Coverage scoring counts a group once, satisfied by any
    /// alternative, so compound words do not count twice.
    /// </summary>
    public static List<string[]> TermGroups(string text, bool removeStopWords = true)
    {
        var groups = new List<string[]>();
        foreach (Match m in TokenPattern().Matches(text))
        {
            var variants = Terms(m.Value, removeStopWords);
            if (variants.Count > 0) groups.Add(variants.Distinct().ToArray());
        }
        return groups;
    }

    /// <summary>True for identifier-like tokens (contain a digit and a letter, or a digit with - or .).</summary>
    public static bool IsCode(string token) =>
        token.Any(char.IsDigit) && (token.Any(char.IsLetter) || token.Contains('-') || token.Contains('.')) && token.Length >= 3;

    /// <summary>A small suffix stripper. It only needs to be consistent between queries and documents.</summary>
    public static string Stem(string word)
    {
        if (word.Length <= 3) return word;
        if (word.EndsWith("ies", StringComparison.Ordinal) && word.Length > 4) return word[..^3] + "y";
        if (word.EndsWith("sses", StringComparison.Ordinal)) return word[..^2];
        if (word.EndsWith("ing", StringComparison.Ordinal) && word.Length > 5) return UndoubleConsonant(word[..^3]);
        if (word.EndsWith("ed", StringComparison.Ordinal) && word.Length > 4) return UndoubleConsonant(word[..^2]);
        if (word.EndsWith("es", StringComparison.Ordinal) && word.Length > 4 &&
            (word.EndsWith("xes", StringComparison.Ordinal) || word.EndsWith("ches", StringComparison.Ordinal) ||
             word.EndsWith("shes", StringComparison.Ordinal) || word.EndsWith("zes", StringComparison.Ordinal)))
            return word[..^2];
        if (word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) && !word.EndsWith("us", StringComparison.Ordinal))
            return word[..^1];
        return word;
    }

    // "submitted" -> "submitt" -> "submit", so it matches "submit"/"submission"-style forms consistently.
    private static string UndoubleConsonant(string stem) =>
        stem.Length > 3 && stem[^1] == stem[^2] && "bdgmnprt".Contains(stem[^1]) ? stem[..^1] : stem;

    public static int WordCount(string text)
    {
        int count = 0;
        bool inWord = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch)) inWord = false;
            else if (!inWord) { inWord = true; count++; }
        }
        return count;
    }

    /// <summary>Collapse all whitespace runs to single spaces.</summary>
    public static string NormalizeWhitespace(string text) => WhitespacePattern().Replace(text, " ").Trim();

    /// <summary>Lower-case, strip punctuation and collapse whitespace; used to compare quotes with source text.</summary>
    public static string NormalizeForComparison(string text) =>
        NormalizeWhitespace(NonAlnumPattern().Replace(text.ToLowerInvariant(), " "));

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlnumPattern();
}
