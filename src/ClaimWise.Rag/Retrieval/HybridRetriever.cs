using ClaimWise.Rag.Embeddings;

namespace ClaimWise.Rag.Retrieval;

/// <summary>
/// Hybrid retrieval: BM25 keyword search and cosine vector search run independently, are merged with Reciprocal Rank
/// Fusion, and the merged candidates are reranked. Every returned passage carries the scores and ranks that put it there.
/// </summary>
public sealed class HybridRetriever(IEmbeddingProvider embeddings, IReranker reranker, RetrievalOptions? defaults = null)
{
    private readonly RetrievalOptions _defaults = defaults ?? new RetrievalOptions();

    public async Task<RetrievalResult> SearchAsync(PolicyIndex index, string query, CancellationToken ct, RetrievalOptions? options = null)
    {
        var o = options ?? _defaults;
        if (index.Chunks.Count == 0 || string.IsNullOrWhiteSpace(query))
            return new RetrievalResult(query, [], 0, 0, o.Mode);

        IReadOnlyList<ScoredId> keyword = [];
        IReadOnlyList<ScoredId> vector = [];

        if (o.Mode is RetrievalMode.Hybrid or RetrievalMode.KeywordOnly)
            keyword = index.Bm25.Search(query, o.KeywordTopK);

        if (o.Mode is RetrievalMode.Hybrid or RetrievalMode.VectorOnly)
        {
            if (index.EmbeddingModelId != embeddings.ModelId)
                throw new InvalidOperationException(
                    $"The index was built with embedding model '{index.EmbeddingModelId}' but the active model is '{embeddings.ModelId}'. Re-index the policy documents.");

            var queryVector = (await embeddings.EmbedAsync([query], EmbeddingPurpose.Query, ct))[0];
            vector = index.Chunks
                .Select(c => new ScoredId(c.Id, VectorMath.Cosine(queryVector, c.Embedding)))
                .Where(s => s.Score >= o.MinVectorSimilarity)
                .OrderByDescending(s => s.Score).ThenBy(s => s.Id)
                .Take(o.VectorTopK)
                .ToList();
        }

        var keywordRank = keyword.Select((s, i) => (s.Id, Rank: i + 1, s.Score)).ToDictionary(x => x.Id);
        var vectorRank = vector.Select((s, i) => (s.Id, Rank: i + 1, s.Score)).ToDictionary(x => x.Id);

        var fused = ReciprocalRankFusion.Fuse(
            [keyword.Select(s => s.Id).ToList(), vector.Select(s => s.Id).ToList()], o.RrfK);

        var candidates = fused
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
            .Take(o.RerankCandidates)
            .Select(kv => new RerankInput(index.Find(kv.Key)!, kv.Value))
            .ToList();

        var reranked = await reranker.RerankAsync(query, candidates, index, ct);

        var passages = reranked.Take(o.FinalTopK).Select((r, i) => new RetrievedPassage(
            r.Chunk, i + 1, r.Score, r.FusedScore,
            keywordRank.TryGetValue(r.Chunk.Id, out var k) ? k.Score : null,
            keywordRank.TryGetValue(r.Chunk.Id, out k) ? k.Rank : null,
            vectorRank.TryGetValue(r.Chunk.Id, out var v) ? v.Score : null,
            vectorRank.TryGetValue(r.Chunk.Id, out v) ? v.Rank : null,
            r.QueryCoverage)).ToList();

        return new RetrievalResult(query, passages, keyword.Count, vector.Count, o.Mode);
    }
}
