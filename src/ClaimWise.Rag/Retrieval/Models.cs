namespace ClaimWise.Rag.Retrieval;

/// <summary>A chunk ready for search, with the source metadata needed to cite it.</summary>
public sealed record IndexedChunk(
    int Id,
    int DocumentId,
    string DocumentTitle,
    string? DocumentVersion,
    string? PolicyFamily,
    string Text,
    string? Heading,
    string? ClauseRef,
    string SectionPath,
    int PageNumber,
    string IndexText,
    float[] Embedding);

/// <summary>One retrieved passage with every score that contributed to its final position.</summary>
public sealed record RetrievedPassage(
    IndexedChunk Chunk,
    int Rank,
    double RerankScore,
    double FusedScore,
    double? KeywordScore,
    int? KeywordRank,
    double? VectorScore,
    int? VectorRank,
    double QueryCoverage);

public sealed record RetrievalResult(
    string Query,
    IReadOnlyList<RetrievedPassage> Passages,
    int KeywordCandidates,
    int VectorCandidates,
    RetrievalMode Mode);

public enum RetrievalMode { Hybrid, KeywordOnly, VectorOnly }

public sealed record RetrievalOptions
{
    public int KeywordTopK { get; init; } = 20;
    public int VectorTopK { get; init; } = 20;
    public int RerankCandidates { get; init; } = 15;
    public int FinalTopK { get; init; } = 6;
    public int RrfK { get; init; } = 60;
    /// <summary>Vector hits below this cosine similarity are noise and are not fused.</summary>
    public double MinVectorSimilarity { get; init; } = 0.08;
    public RetrievalMode Mode { get; init; } = RetrievalMode.Hybrid;
}
