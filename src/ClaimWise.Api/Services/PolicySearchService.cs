using ClaimWise.Api.Ai;
using ClaimWise.Api.Dtos;
using ClaimWise.Rag.Embeddings;
using ClaimWise.Rag.Grounding;
using ClaimWise.Rag.Retrieval;
using Microsoft.Extensions.Options;

namespace ClaimWise.Api.Services;

public interface IPolicySearchService
{
    Task<PolicySearchResponse> SearchAsync(PolicySearchRequest request, CancellationToken ct);
    Task<AiStatusDto> GetStatusAsync(CancellationToken ct);
}

public sealed class PolicySearchService(
    HybridRetriever retriever,
    PolicyIndexCache cache,
    IEmbeddingProvider embeddings,
    ISummaryGenerator generator,
    IOptions<AiOptions> options) : IPolicySearchService
{
    public async Task<PolicySearchResponse> SearchAsync(PolicySearchRequest request, CancellationToken ct)
    {
        var index = await cache.GetScopedAsync(request.DocumentIds, ct);
        var result = await retriever.SearchAsync(index, request.Query.Trim(), ct,
            options.Value.Retrieval with { FinalTopK = request.TopK, Mode = request.Mode });

        return new PolicySearchResponse(result.Query, result.Mode, result.KeywordCandidates, result.VectorCandidates,
            result.Passages.Select(ToDto).ToList());
    }

    public async Task<AiStatusDto> GetStatusAsync(CancellationToken ct)
    {
        var snapshot = await cache.GetAsync(ct);
        return new AiStatusDto(
            options.Value.Embeddings.Provider, embeddings.ModelId, generator.Name, generator.ModelId,
            snapshot.Documents, snapshot.Index.Chunks.Count, snapshot.StaleChunks,
            SummariesUseLanguageModel: generator.Name != "extractive");
    }

    internal static PassageDto ToDto(RetrievedPassage p) => new(
        p.Rank, p.Chunk.Id, p.Chunk.DocumentId, p.Chunk.DocumentTitle, p.Chunk.DocumentVersion, p.Chunk.ClauseRef,
        p.Chunk.Heading, p.Chunk.SectionPath, p.Chunk.PageNumber, p.Chunk.Text,
        Math.Round(p.RerankScore, 4), Math.Round(p.FusedScore, 5), p.KeywordScore is { } k ? Math.Round(k, 4) : null,
        p.KeywordRank, p.VectorScore is { } v ? Math.Round(v, 4) : null, p.VectorRank);
}
