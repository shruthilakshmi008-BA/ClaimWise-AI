namespace ClaimWise.Rag.Embeddings;

/// <summary>
/// Local, deterministic, dependency-free embedder based on feature hashing. It is NOT a neural model: it maps
/// stemmed words, adjacent word pairs and character trigrams into a fixed-size signed vector, after folding a small
/// set of domain synonyms (e.g. "hospitalization" and "admission" both fold to "inpatient"). That gives vector search
/// tolerance for word-form variation, typos and a few synonyms, and lets the whole pipeline run offline and in tests.
/// For genuine semantic similarity configure <see cref="OpenAiCompatibleEmbeddingProvider"/>.
/// </summary>
public sealed class HashingEmbeddingProvider(int dimensions = 512) : IEmbeddingProvider
{
    public string ModelId => $"local-hashing-v1-{dimensions}";

    public int Dimensions => dimensions;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken ct)
    {
        IReadOnlyList<float[]> result = texts.Select(Embed).ToList();
        return Task.FromResult(result);
    }

    public float[] Embed(string text)
    {
        var vector = new float[dimensions];
        var terms = TextAnalysis.Terms(text).Select(Fold).ToList();

        var counts = new Dictionary<string, (double Weight, int Count)>();
        void Add(string feature, double weight)
        {
            counts[feature] = counts.TryGetValue(feature, out var c) ? (weight, c.Count + 1) : (weight, 1);
        }

        for (int i = 0; i < terms.Count; i++)
        {
            Add("u:" + terms[i], 1.0);
            if (i + 1 < terms.Count) Add("b:" + terms[i] + "_" + terms[i + 1], 0.6);
            if (terms[i].Length >= 5 && !TextAnalysis.IsCode(terms[i]))
                for (int j = 0; j + 3 <= terms[i].Length; j++)
                    Add("c:" + terms[i].Substring(j, 3), 0.2);
        }

        foreach (var (feature, (weight, count)) in counts)
        {
            var hash = Fnv1a(feature);
            var index = (int)(hash % (ulong)dimensions);
            var sign = ((hash >> 63) & 1) == 0 ? 1f : -1f;
            vector[index] += sign * (float)(weight * (1 + Math.Log(count)));
        }

        VectorMath.NormalizeInPlace(vector);
        return vector;
    }

    private static string Fold(string term) => DomainSynonyms.Fold(term);

    private static ulong Fnv1a(string s)
    {
        ulong hash = 14695981039346656037UL;
        foreach (var ch in s)
        {
            hash ^= ch;
            hash *= 1099511628211UL;
        }
        return hash;
    }
}
