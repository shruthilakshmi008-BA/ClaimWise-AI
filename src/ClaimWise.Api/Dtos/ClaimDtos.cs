using System.ComponentModel.DataAnnotations;
using ClaimWise.Api.Domain;

namespace ClaimWise.Api.Dtos;

// ---------- Requests (what the client sends us) ----------

public sealed class CreateClaimRequest : IValidatableObject
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string PatientName { get; init; } = "";

    [Required, RegularExpression(@"^[A-Za-z0-9\-]{5,20}$",
        ErrorMessage = "MemberId must be 5-20 letters, digits or hyphens.")]
    public string MemberId { get; init; } = "";

    [Required, RegularExpression(@"^[A-Za-z0-9\-]{5,20}$",
        ErrorMessage = "PolicyNumber must be 5-20 letters, digits or hyphens.")]
    public string PolicyNumber { get; init; } = "";

    [Required, StringLength(150, MinimumLength = 2)]
    public string ProviderName { get; init; } = "";

    [Required]
    public ClaimType? ClaimType { get; init; }

    [StringLength(10)]
    public string? DiagnosisCode { get; init; }

    [StringLength(10)]
    public string? ProcedureCode { get; init; }

    [Range(0.01, 10_000_000)]
    public decimal AmountClaimed { get; init; }

    [Required]
    public DateOnly? ServiceDate { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Use the injected clock (when MVC provides one) so the rule is testable and consistent with the services.
        var clock = (TimeProvider?)validationContext.GetService(typeof(TimeProvider)) ?? TimeProvider.System;
        if (ServiceDate is { } date && date > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            yield return new ValidationResult("ServiceDate cannot be in the future.", [nameof(ServiceDate)]);

        if (decimal.Round(AmountClaimed, 2) != AmountClaimed)
            yield return new ValidationResult("AmountClaimed can have at most 2 decimal places.", [nameof(AmountClaimed)]);
    }
}

public sealed class AddDocumentRequest
{
    [Required]
    public DocumentType? DocumentType { get; init; }

    [Required, StringLength(200, MinimumLength = 1),
     RegularExpression(@"^[^\\/:*?""<>|]+$", ErrorMessage = "FileName contains characters that are not allowed.")]
    public string FileName { get; init; } = "";
}

public sealed class ChangeStatusRequest : IValidatableObject
{
    [Required]
    public ClaimStatus? NewStatus { get; init; }

    [Required, StringLength(100, MinimumLength = 2)]
    public string ReviewerName { get; init; } = "";

    [StringLength(1000)]
    public string? Note { get; init; }

    /// <summary>
    /// Optional. The claim <c>version</c> the reviewer was looking at. If the claim has changed since,
    /// the request is rejected with 409 so a decision is never made on stale information.
    /// </summary>
    public int? ExpectedVersion { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (NewStatus is { } status && ClaimWorkflow.RequiresNote(status) && string.IsNullOrWhiteSpace(Note))
            yield return new ValidationResult($"A note is required when moving a claim to {status}.", [nameof(Note)]);
    }
}

public sealed class ClaimListQuery
{
    public ClaimStatus? Status { get; init; }
    public ClaimType? ClaimType { get; init; }

    [StringLength(100)]
    public string? Search { get; init; }

    [RegularExpression("^(?i)(submittedAt|serviceDate|amount|claimNumber|status)$",
        ErrorMessage = "SortBy must be one of: submittedAt, serviceDate, amount, claimNumber, status.")]
    public string SortBy { get; init; } = "submittedAt";

    [RegularExpression("^(?i)(asc|desc)$", ErrorMessage = "SortDir must be asc or desc.")]
    public string SortDir { get; init; } = "desc";

    [Range(1, 100_000)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

// ---------- Responses (what we send back) ----------

public sealed record ClaimSummaryDto(
    int Id,
    string ClaimNumber,
    string PatientName,
    string ProviderName,
    ClaimType ClaimType,
    decimal AmountClaimed,
    DateOnly ServiceDate,
    ClaimStatus Status,
    DateTime SubmittedAt);

public sealed record DocumentDto(int Id, DocumentType DocumentType, string FileName, DateTime ReceivedAt);

public sealed record ValidationDto(
    bool IsComplete,
    IReadOnlyList<DocumentType> RequiredDocuments,
    IReadOnlyList<DocumentType> MissingDocuments,
    IReadOnlyList<string> MissingFields);

public sealed record ClaimDetailDto(
    int Id,
    string ClaimNumber,
    string PatientName,
    string MemberId,
    string PolicyNumber,
    string ProviderName,
    ClaimType ClaimType,
    string? DiagnosisCode,
    string? ProcedureCode,
    decimal AmountClaimed,
    DateOnly ServiceDate,
    ClaimStatus Status,
    DateTime SubmittedAt,
    DateTime UpdatedAt,
    int Version,
    string? ReviewerName,
    string? ReviewerNote,
    IReadOnlyList<DocumentDto> Documents,
    ValidationDto Validation,
    IReadOnlyList<ClaimStatus> AllowedNextStatuses);

public sealed record AuditEntryDto(int Id, string Action, string Details, string Actor, DateTime Timestamp);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record DashboardDto(
    int TotalClaims,
    IReadOnlyDictionary<string, int> ByStatus,
    decimal TotalAmountClaimed,
    int OpenClaimsWithIncompleteSubmission);
