using ClaimWise.Api.Data;
using ClaimWise.Rag.Embeddings;
using ClaimWise.Rag.Retrieval;
using Microsoft.EntityFrameworkCore;

namespace ClaimWise.Api.Ai;

public sealed record IndexSnapshot(PolicyIndex Index, int StaleChunks, int Documents);

/// <summary>
/// Holds the searchable corpus in memory (policy corpora are small) and rebuilds it lazily after any change.
/// Chunks embedded with a different model than the active one are excluded and counted as stale, so vector
/// spaces are never mixed; the status endpoint tells the operator to re-index.
/// </summary>
public sealed class PolicyIndexCache(IServiceScopeFactory scopes, IEmbeddingProvider embeddings)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task<IndexSnapshot>? _current;

    public void Invalidate() => Interlocked.Exchange(ref _current, null);

    public async Task<IndexSnapshot> GetAsync(CancellationToken ct)
    {
        var existing = _current;
        if (existing is not null) return await existing;

        await _gate.WaitAsync(ct);
        try
        {
            _current ??= BuildAsync(CancellationToken.None);
            return await _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>A snapshot restricted to some documents (chunks are reused; only BM25 statistics are recomputed).</summary>
    public async Task<PolicyIndex> GetScopedAsync(IReadOnlyCollection<int>? documentIds, CancellationToken ct)
    {
        var snapshot = await GetAsync(ct);
        if (documentIds is null || documentIds.Count == 0) return snapshot.Index;
        var chunks = snapshot.Index.Chunks.Where(c => documentIds.Contains(c.DocumentId)).ToList();
        return new PolicyIndex(chunks, snapshot.Index.EmbeddingModelId);
    }

    private async Task<IndexSnapshot> BuildAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimWiseDbContext>();
        var model = embeddings.ModelId;

        var rows = await db.PolicyChunks.AsNoTracking()
            .Where(c => db.PolicyDocuments.Any(d => d.Id == c.DocumentId && d.IsActive))
            .Join(db.PolicyDocuments, c => c.DocumentId, d => d.Id, (c, d) => new { Chunk = c, Doc = d })
            .OrderBy(x => x.Chunk.DocumentId).ThenBy(x => x.Chunk.Ordinal)
            .ToListAsync(ct);

        var usable = rows.Where(r => r.Chunk.EmbeddingModel == model).ToList();
        var chunks = usable.Select(r => new IndexedChunk(
            r.Chunk.Id, r.Doc.Id, r.Doc.Title, r.Doc.Version, r.Doc.PolicyFamily, r.Chunk.Text, r.Chunk.Heading,
            r.Chunk.ClauseRef, r.Chunk.SectionPath, r.Chunk.PageNumber,
            string.IsNullOrEmpty(r.Chunk.Heading) ? r.Chunk.Text : $"{r.Chunk.Heading}. {r.Chunk.Text}",
            VectorMath.FromBytes(r.Chunk.Embedding))).ToList();

        return new IndexSnapshot(new PolicyIndex(chunks, model), rows.Count - usable.Count, usable.Select(r => r.Doc.Id).Distinct().Count());
    }
}
