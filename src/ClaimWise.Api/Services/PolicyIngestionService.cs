using System.Security.Cryptography;
using ClaimWise.Api.Ai;
using ClaimWise.Api.Data;
using ClaimWise.Api.Domain;
using ClaimWise.Api.Dtos;
using ClaimWise.Api.Infrastructure;
using ClaimWise.Rag.Chunking;
using ClaimWise.Rag.Embeddings;
using ClaimWise.Rag.Extraction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaimWise.Api.Services;

public interface IPolicyService
{
    Task<PolicyDocumentDto> UploadAsync(UploadPolicyRequest request, CancellationToken ct);
    Task<IReadOnlyList<PolicyDocumentDto>> ListAsync(CancellationToken ct);
    Task<PolicyDocumentDto> GetAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<PolicyChunkDto>> GetChunksAsync(int id, CancellationToken ct);
    Task<PolicyDocumentDto> SetActiveAsync(int id, bool active, CancellationToken ct);
    Task<int> ReindexAsync(CancellationToken ct);
}

/// <summary>
/// Policy document ingestion: validate → hash (refuse duplicates) → extract text → chunk with metadata → embed →
/// persist document and chunks in one transaction. Nothing is saved if any step fails.
/// </summary>
public sealed class PolicyService(
    ClaimWiseDbContext db,
    TextExtractorRegistry extractors,
    PolicyChunker chunker,
    IEmbeddingProvider embeddings,
    PolicyIndexCache cache,
    IOptions<AiOptions> options,
    TimeProvider clock,
    ILogger<PolicyService> logger) : IPolicyService
{
    private const int EmbedBatch = 64;

    public async Task<PolicyDocumentDto> UploadAsync(UploadPolicyRequest request, CancellationToken ct)
    {
        var limits = options.Value.Uploads;
        var file = request.File!;
        var fileName = SanitizeFileName(file.FileName);

        if (file.Length > limits.MaxBytes)
            throw new PayloadTooLargeException($"The file is larger than the {limits.MaxBytes / (1024 * 1024)} MB limit.");
        if (!extractors.IsSupported(fileName))
            throw new UnsupportedFileException($"Unsupported file type. Supported: {string.Join(", ", extractors.SupportedExtensions.Order())}.");

        // Read at most MaxBytes+1 so a lying Content-Length cannot make us buffer an unbounded body.
        await using var buffer = new MemoryStream();
        await using (var input = file.OpenReadStream())
        {
            var chunkBuffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(chunkBuffer, ct)) > 0)
            {
                if (buffer.Length + read > limits.MaxBytes)
                    throw new PayloadTooLargeException($"The file is larger than the {limits.MaxBytes / (1024 * 1024)} MB limit.");
                buffer.Write(chunkBuffer, 0, read);
            }
        }

        var hash = Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))).ToLowerInvariant();
        var duplicate = await db.PolicyDocuments.AsNoTracking().Where(d => d.Sha256 == hash).Select(d => new { d.Id, d.Title }).FirstOrDefaultAsync(ct);
        if (duplicate is not null)
            throw new BusinessRuleException($"This exact file was already uploaded as '{duplicate.Title}' (document {duplicate.Id}).");

        buffer.Position = 0;
        var extracted = extractors.Extract(fileName, buffer); // ExtractionException -> 422
        var chunks = chunker.Chunk(extracted);
        if (chunks.Count == 0)
            throw new ExtractionException("No passages could be created from this document.");
        if (chunks.Count > limits.MaxChunksPerDocument)
            throw new PayloadTooLargeException($"The document produced {chunks.Count} passages; the limit is {limits.MaxChunksPerDocument}.");

        var vectors = new List<float[]>(chunks.Count);
        for (int i = 0; i < chunks.Count; i += EmbedBatch)
        {
            var batch = chunks.Skip(i).Take(EmbedBatch).Select(c => c.IndexText).ToList();
            vectors.AddRange(await embeddings.EmbedAsync(batch, EmbeddingPurpose.Document, ct)); // ProviderException -> 502/503
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var doc = new PolicyDocument
        {
            Title = !string.IsNullOrWhiteSpace(request.Title) ? request.Title.Trim() : GuessTitle(extracted, fileName),
            PolicyFamily = NullIfBlank(request.PolicyFamily)?.ToLowerInvariant().Replace(' ', '-'),
            Version = NullIfBlank(request.Version),
            EffectiveDate = request.EffectiveDate,
            FileName = fileName,
            SizeBytes = buffer.Length,
            Sha256 = hash,
            PageCount = extracted.Pages.Count,
            ChunkCount = chunks.Count,
            UploadedAt = now,
            UploadedBy = request.UploadedBy.Trim(),
            Chunks = chunks.Select((c, i) => new PolicyChunk
            {
                Ordinal = c.Ordinal, Text = c.Text, Heading = Truncate(c.Heading, 300), ClauseRef = c.ClauseRef,
                SectionPath = Truncate(c.SectionPath, 600)!, PageNumber = c.PageNumber, StartOffset = c.StartOffset,
                EndOffset = c.EndOffset, WordCount = c.WordCount,
                EmbeddingModel = embeddings.ModelId, Embedding = VectorMath.ToBytes(vectors[i])
            }).ToList()
        };

        db.PolicyDocuments.Add(doc);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            if (await db.PolicyDocuments.AnyAsync(d => d.Sha256 == hash, ct))
                // Two identical uploads raced past the check above; the unique index caught the loser.
                throw new BusinessRuleException("This exact file was already uploaded.");
            logger.LogError(ex, "Saving policy document failed");
            throw;
        }

        cache.Invalidate();
        logger.LogInformation("Policy document {Id} '{Title}' ingested: {Chunks} passages, {Pages} page(s), embedding model {Model}",
            doc.Id, doc.Title, doc.ChunkCount, doc.PageCount, embeddings.ModelId);
        return ToDto(doc);
    }

    public async Task<IReadOnlyList<PolicyDocumentDto>> ListAsync(CancellationToken ct) =>
        (await db.PolicyDocuments.AsNoTracking().OrderByDescending(d => d.UploadedAt).ThenByDescending(d => d.Id).ToListAsync(ct))
        .Select(ToDto).ToList();

    public async Task<PolicyDocumentDto> GetAsync(int id, CancellationToken ct) =>
        ToDto(await db.PolicyDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct)
              ?? throw new NotFoundException($"Policy document {id} was not found."));

    public async Task<IReadOnlyList<PolicyChunkDto>> GetChunksAsync(int id, CancellationToken ct)
    {
        if (!await db.PolicyDocuments.AnyAsync(d => d.Id == id, ct))
            throw new NotFoundException($"Policy document {id} was not found.");

        return await db.PolicyChunks.AsNoTracking().Where(c => c.DocumentId == id).OrderBy(c => c.Ordinal)
            .Select(c => new PolicyChunkDto(c.Id, c.Ordinal, c.ClauseRef, c.Heading, c.SectionPath, c.PageNumber,
                c.StartOffset, c.EndOffset, c.WordCount, c.Text))
            .ToListAsync(ct);
    }

    public async Task<PolicyDocumentDto> SetActiveAsync(int id, bool active, CancellationToken ct)
    {
        var doc = await db.PolicyDocuments.FirstOrDefaultAsync(d => d.Id == id, ct)
                  ?? throw new NotFoundException($"Policy document {id} was not found.");
        doc.IsActive = active;
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        logger.LogInformation("Policy document {Id} set active={Active}", id, active);
        return ToDto(doc);
    }

    /// <summary>Re-embeds every chunk with the active embedding model (needed after changing the embedding provider).</summary>
    public async Task<int> ReindexAsync(CancellationToken ct)
    {
        var chunks = await db.PolicyChunks.OrderBy(c => c.Id).ToListAsync(ct);

        for (int i = 0; i < chunks.Count; i += EmbedBatch)
        {
            var batch = chunks.Skip(i).Take(EmbedBatch).ToList();
            var texts = batch.Select(c => string.IsNullOrEmpty(c.Heading) ? c.Text : $"{c.Heading}. {c.Text}").ToList();
            var vectors = await embeddings.EmbedAsync(texts, EmbeddingPurpose.Document, ct);
            for (int j = 0; j < batch.Count; j++)
            {
                batch[j].Embedding = VectorMath.ToBytes(vectors[j]);
                batch[j].EmbeddingModel = embeddings.ModelId;
            }
        }

        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        logger.LogInformation("Re-indexed {Count} passages with embedding model {Model}", chunks.Count, embeddings.ModelId);
        return chunks.Count;
    }

    // ---------- helpers ----------

    private static string SanitizeFileName(string? name)
    {
        // Only used for display and for choosing an extractor; it is never used as a file-system path.
        var leaf = Path.GetFileName((name ?? "").Replace('\\', '/'));
        leaf = new string(leaf.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (leaf.Length == 0) throw new UnsupportedFileException("The file needs a name with an extension.");
        return leaf.Length > 200 ? leaf[^200..] : leaf;
    }

    private static string GuessTitle(ExtractedDocument doc, string fileName)
    {
        var first = doc.Pages[0].Text.Split('\n').Select(l => l.Trim().TrimStart('#', ' ')).FirstOrDefault(l => l.Length > 0);
        if (first is { Length: >= 3 and <= 150 }) return first;
        return Path.GetFileNameWithoutExtension(fileName);
    }

    private static string? NullIfBlank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string? Truncate(string? v, int max) => v is null || v.Length <= max ? v : v[..max];

    private static PolicyDocumentDto ToDto(PolicyDocument d) => new(
        d.Id, d.Title, d.PolicyFamily, d.Version, d.EffectiveDate, d.FileName, d.SizeBytes, d.PageCount,
        d.ChunkCount, d.IsActive, d.UploadedAt, d.UploadedBy, d.Sha256);
}
