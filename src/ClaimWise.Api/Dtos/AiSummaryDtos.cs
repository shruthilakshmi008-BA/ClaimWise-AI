using System.ComponentModel.DataAnnotations;
using ClaimWise.Api.Domain;

namespace ClaimWise.Api.Dtos;

public sealed class GenerateSummaryRequest
{
    /// <summary>What the reviewer wants to know. If omitted, a default question about benefits, limits and exclusions for the claim type is used.</summary>
    [StringLength(500)]
    public string? Question { get; init; }

    [Required, StringLength(100, MinimumLength = 2)]
    public string RequestedBy { get; init; } = "";

    /// <summary>Restrict evidence to these policy documents (for example the policy that applies to this claim's member). Omit for all active documents.</summary>
    public int[]? DocumentIds { get; init; }
}

public sealed class ReviewSummaryRequest : IValidatableObject
{
    [Required]
    public SummaryReviewState? Decision { get; init; }

    [Required, StringLength(100, MinimumLength = 2)]
    public string ReviewerName { get; init; } = "";

    [StringLength(1000)]
    public string? Note { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Decision is not (SummaryReviewState.Accepted or SummaryReviewState.Rejected))
            yield return new ValidationResult("Decision must be Accepted or Rejected.", [nameof(Decision)]);
        else if (Decision == SummaryReviewState.Rejected && string.IsNullOrWhiteSpace(Note))
            yield return new ValidationResult("A note is required when rejecting an AI summary.", [nameof(Note)]);
    }
}

public sealed record CitationDto(string Passage, string Quote);

public sealed record SummaryStatementDto(int Ordinal, string Text, string Origin, IReadOnlyList<CitationDto> Citations);

public sealed record SummaryPassageDto(
    string Label, int Rank, int ChunkId, int DocumentId, string DocumentTitle, string? DocumentVersion, string? ClauseRef,
    string? Heading, string SectionPath, int PageNumber, string Text, double RerankScore, double? KeywordScore, double? VectorScore);

public sealed record EvidenceDto(
    string Verdict, IReadOnlyList<string> Reasons, double TopScore, double Coverage, int SupportingPassages, IReadOnlyList<string> MissingTerms);

public sealed record ConflictDto(string Kind, IReadOnlyList<string> Passages, string Description);

public sealed record RejectedStatementDto(string Text, string Reason);

public sealed record SummaryReviewDto(SummaryReviewState State, string? ReviewedBy, DateTime? ReviewedAt, string? Note);

public sealed record AiSummaryDto(
    int Id,
    int ClaimId,
    string Question,
    AiSummaryStatus Status,
    DateTime GeneratedAt,
    string RequestedBy,
    string Generator,
    string? Model,
    string EmbeddingModel,
    bool UsedLanguageModel,
    int ClaimVersionAtGeneration,
    IReadOnlyList<SummaryStatementDto> Statements,
    IReadOnlyList<SummaryPassageDto> Passages,
    EvidenceDto Evidence,
    IReadOnlyList<ConflictDto> Conflicts,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<RejectedStatementDto> RejectedStatements,
    string? AbstainReason,
    string? FailureReason,
    SummaryReviewDto Review,
    string Notice);
