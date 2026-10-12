using System.ComponentModel.DataAnnotations;
using ClaimWise.Rag.Retrieval;

namespace ClaimWise.Api.Dtos;

public sealed class UploadPolicyRequest : IValidatableObject
{
    [Required]
    public IFormFile? File { get; init; }

    [StringLength(200)]
    public string? Title { get; init; }

    /// <summary>Documents sharing a family are versions of one policy, e.g. "gold-plan".</summary>
    [StringLength(100), RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9 _\-]*$", ErrorMessage = "PolicyFamily may contain letters, digits, spaces, hyphens and underscores.")]
    public string? PolicyFamily { get; init; }

    [StringLength(50)]
    public string? Version { get; init; }

    public DateOnly? EffectiveDate { get; init; }

    [Required, StringLength(100, MinimumLength = 2)]
    public string UploadedBy { get; init; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (File is { Length: 0 })
            yield return new ValidationResult("The file is empty.", [nameof(File)]);
    }
}

public sealed record PolicyDocumentDto(
    int Id, string Title, string? PolicyFamily, string? Version, DateOnly? EffectiveDate, string FileName,
    long SizeBytes, int PageCount, int ChunkCount, bool IsActive, DateTime UploadedAt, string UploadedBy, string Sha256);

public sealed record PolicyChunkDto(
    int Id, int Ordinal, string? ClauseRef, string? Heading, string SectionPath, int PageNumber,
    int StartOffset, int EndOffset, int WordCount, string Text);

public sealed class SetPolicyActiveRequest
{
    [Required]
    public bool? IsActive { get; init; }
}

public sealed class PolicySearchRequest
{
    [Required, StringLength(500, MinimumLength = 3)]
    public string Query { get; init; } = "";

    [Range(1, 20)]
    public int TopK { get; init; } = 6;

    public RetrievalMode Mode { get; init; } = RetrievalMode.Hybrid;

    /// <summary>Restrict the search to these documents. Omit to search all active documents.</summary>
    public int[]? DocumentIds { get; init; }
}

public sealed record PassageDto(
    int Rank, int ChunkId, int DocumentId, string DocumentTitle, string? DocumentVersion, string? ClauseRef,
    string? Heading, string SectionPath, int PageNumber, string Text,
    double RerankScore, double FusedScore, double? KeywordScore, int? KeywordRank, double? VectorScore, int? VectorRank);

public sealed record PolicySearchResponse(
    string Query, RetrievalMode Mode, int KeywordCandidates, int VectorCandidates, IReadOnlyList<PassageDto> Passages);

public sealed record AiStatusDto(
    string EmbeddingProvider, string EmbeddingModel, string SummaryGenerator, string? SummaryModel,
    int ActiveDocuments, int IndexedChunks, int StaleChunks, bool SummariesUseLanguageModel);
