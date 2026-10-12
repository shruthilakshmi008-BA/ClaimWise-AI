namespace ClaimWise.Rag.Retrieval;

/// <summary>An immutable snapshot of the searchable corpus. Rebuild it when documents change.</summary>
public sealed class PolicyIndex
{
    private readonly Dictionary<int, IndexedChunk> _byId;

    public PolicyIndex(IReadOnlyList<IndexedChunk> chunks, string embeddingModelId)
    {
        Chunks = chunks;
        EmbeddingModelId = embeddingModelId;
        _byId = chunks.ToDictionary(c => c.Id);
        Bm25 = new Bm25Index(chunks.Select(c => (c.Id, c.IndexText)).ToList());
    }

    public IReadOnlyList<IndexedChunk> Chunks { get; }
    public string EmbeddingModelId { get; }
    public Bm25Index Bm25 { get; }

    public IndexedChunk? Find(int chunkId) => _byId.GetValueOrDefault(chunkId);

    /// <summary>Idf that treats a term absent from the corpus as maximally informative (and therefore maximally missing).</summary>
    public double SmoothedIdf(string term)
    {
        var idf = Bm25.Idf(term);
        return idf > 0 ? idf : Math.Log(1 + (Chunks.Count + 0.5) / 0.5);
    }
}
