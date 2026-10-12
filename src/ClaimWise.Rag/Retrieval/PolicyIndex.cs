using ClaimWise.Rag.Embeddings;

namespace ClaimWise.Rag.Retrieval;

/// <summary>An immutable snapshot of the searchable corpus. Rebuild it when documents change.</summary>
public sealed class PolicyIndex
{
    private readonly Dictionary<int, IndexedChunk> _byId;
    private readonly Dictionary<string, int> _foldedDocumentFrequency = new(StringComparer.Ordinal);

    public PolicyIndex(IReadOnlyList<IndexedChunk> chunks, string embeddingModelId)
    {
        Chunks = chunks;
        EmbeddingModelId = embeddingModelId;
        _byId = chunks.ToDictionary(c => c.Id);
        Bm25 = new Bm25Index(chunks.Select(c => (c.Id, c.IndexText)).ToList());

        // Reranking and sufficiency work on synonym-folded terms, so their idf must come from the folded vocabulary.
        foreach (var chunk in chunks)
            foreach (var term in TextAnalysis.Terms(chunk.IndexText).Select(DomainSynonyms.Fold).Distinct())
                _foldedDocumentFrequency[term] = _foldedDocumentFrequency.GetValueOrDefault(term) + 1;
    }

    public IReadOnlyList<IndexedChunk> Chunks { get; }
    public string EmbeddingModelId { get; }
    public Bm25Index Bm25 { get; }

    public IndexedChunk? Find(int chunkId) => _byId.GetValueOrDefault(chunkId);

    /// <summary>
    /// Inverse document frequency of a <b>synonym-folded</b> term. A term absent from the corpus is treated as maximally
    /// informative (and therefore maximally missing), so a question about something the policies never mention scores low.
    /// </summary>
    public double SmoothedIdf(string foldedTerm)
    {
        var n = Chunks.Count;
        var df = _foldedDocumentFrequency.GetValueOrDefault(foldedTerm);
        return df == 0 ? Math.Log(1 + (n + 0.5) / 0.5) : Math.Log(1 + (n - df + 0.5) / (df + 0.5));
    }
}
