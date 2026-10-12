namespace ClaimWise.Rag.Retrieval;

public readonly record struct ScoredId(int Id, double Score);

/// <summary>
/// In-memory Okapi BM25 keyword index. Implemented in code (not as a database feature) so keyword search behaves
/// identically on SQLite and PostgreSQL; policy corpora are small enough to index in memory (see docs/rag.md).
/// </summary>
public sealed class Bm25Index
{
    private readonly double _k1;
    private readonly double _b;
    private readonly Dictionary<string, List<(int Doc, int Tf)>> _postings = new(StringComparer.Ordinal);
    private readonly int[] _docLengths;
    private readonly int[] _ids;
    private readonly double _avgLength;

    public Bm25Index(IReadOnlyList<(int Id, string Text)> documents, double k1 = 1.2, double b = 0.75)
    {
        _k1 = k1;
        _b = b;
        _ids = documents.Select(d => d.Id).ToArray();
        _docLengths = new int[documents.Count];

        for (int i = 0; i < documents.Count; i++)
        {
            var terms = TextAnalysis.Terms(documents[i].Text);
            _docLengths[i] = terms.Count;
            foreach (var group in terms.GroupBy(t => t))
            {
                if (!_postings.TryGetValue(group.Key, out var list))
                    _postings[group.Key] = list = [];
                list.Add((i, group.Count()));
            }
        }

        _avgLength = documents.Count == 0 ? 0 : _docLengths.Average();
    }

    public int DocumentCount => _ids.Length;

    /// <summary>Inverse document frequency; 0 for unseen terms.</summary>
    public double Idf(string term)
    {
        if (!_postings.TryGetValue(term, out var list)) return 0;
        return Math.Log(1 + (DocumentCount - list.Count + 0.5) / (list.Count + 0.5));
    }

    public bool Contains(string term) => _postings.ContainsKey(term);

    public IReadOnlyList<ScoredId> Search(string query, int topK)
    {
        if (DocumentCount == 0 || topK <= 0) return [];

        var scores = new Dictionary<int, double>();
        foreach (var term in TextAnalysis.Terms(query).Distinct())
        {
            if (!_postings.TryGetValue(term, out var list)) continue;
            var idf = Idf(term);
            foreach (var (doc, tf) in list)
            {
                var norm = 1 - _b + _b * (_avgLength == 0 ? 1 : _docLengths[doc] / _avgLength);
                var s = idf * (tf * (_k1 + 1)) / (tf + _k1 * norm);
                scores[doc] = scores.GetValueOrDefault(doc) + s;
            }
        }

        return scores
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
            .Take(topK)
            .Select(kv => new ScoredId(_ids[kv.Key], kv.Value))
            .ToList();
    }
}
