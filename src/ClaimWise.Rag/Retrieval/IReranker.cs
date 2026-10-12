namespace ClaimWise.Rag.Retrieval;

public sealed record RerankInput(IndexedChunk Chunk, double FusedScore);

public sealed record RerankOutput(IndexedChunk Chunk, double FusedScore, double Score, double QueryCoverage);

/// <summary>Reorders fused candidates using a more expensive relevance signal than the first-stage rankers.</summary>
public interface IReranker
{
    Task<IReadOnlyList<RerankOutput>> RerankAsync(string query, IReadOnlyList<RerankInput> candidates, PolicyIndex index, CancellationToken ct);
}
