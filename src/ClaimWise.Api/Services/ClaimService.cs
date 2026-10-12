using ClaimWise.Api.Data;
using ClaimWise.Api.Domain;
using ClaimWise.Api.Dtos;
using ClaimWise.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClaimWise.Api.Services;

public sealed class ClaimService(ClaimWiseDbContext db, TimeProvider clock, ILogger<ClaimService> logger) : IClaimService
{
    private static readonly ClaimStatus[] OpenStatuses =
        [ClaimStatus.Submitted, ClaimStatus.InReview, ClaimStatus.NeedsInformation];

    public async Task<ClaimDetailDto> CreateAsync(CreateClaimRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var claim = new Claim
        {
            // Temporary unique value. The real number (CW-000123) is built from the database ID below.
            ClaimNumber = $"TMP-{Guid.NewGuid():N}",
            PatientName = request.PatientName.Trim(),
            MemberId = request.MemberId.Trim().ToUpperInvariant(),
            PolicyNumber = request.PolicyNumber.Trim().ToUpperInvariant(),
            ProviderName = request.ProviderName.Trim(),
            ClaimType = request.ClaimType!.Value,
            DiagnosisCode = NullIfBlank(request.DiagnosisCode),
            ProcedureCode = NullIfBlank(request.ProcedureCode),
            AmountClaimed = request.AmountClaimed,
            ServiceDate = request.ServiceDate!.Value,
            Status = ClaimStatus.Submitted,
            SubmittedAt = now,
            UpdatedAt = now
        };

        // One transaction so we never end up with a claim that still has a TMP- number.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.Claims.Add(claim);
        await db.SaveChangesAsync(ct);

        claim.ClaimNumber = $"CW-{claim.Id:D6}";
        db.AuditEntries.Add(new AuditEntry
        {
            ClaimId = claim.Id,
            Action = "ClaimCreated",
            Details = $"{claim.ClaimType} claim submitted for {claim.AmountClaimed:0.00}.",
            Actor = "intake",
            Timestamp = now
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation("Claim {ClaimNumber} created ({ClaimType})", claim.ClaimNumber, claim.ClaimType);
        return ToDetail(claim);
    }

    public async Task<PagedResult<ClaimSummaryDto>> ListAsync(ClaimListQuery query, CancellationToken ct)
    {
        IQueryable<Claim> q = db.Claims.AsNoTracking();

        if (query.Status is { } status) q = q.Where(c => c.Status == status);
        if (query.ClaimType is { } type) q = q.Where(c => c.ClaimType == type);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Remove LIKE wildcard characters so users cannot accidentally change the search pattern.
            var term = query.Search.Trim().Replace("%", "").Replace("_", "");
            if (term.Length > 0)
            {
                var pattern = $"%{term}%";
                q = q.Where(c =>
                    EF.Functions.Like(c.ClaimNumber, pattern) ||
                    EF.Functions.Like(c.PatientName, pattern) ||
                    EF.Functions.Like(c.MemberId, pattern) ||
                    EF.Functions.Like(c.PolicyNumber, pattern) ||
                    EF.Functions.Like(c.ProviderName, pattern) ||
                    (c.DiagnosisCode != null && EF.Functions.Like(c.DiagnosisCode, pattern)) ||
                    (c.ProcedureCode != null && EF.Functions.Like(c.ProcedureCode, pattern)));
            }
        }

        var total = await q.CountAsync(ct);

        var descending = query.SortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);
        var items = await ApplySort(q, query.SortBy, descending)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new ClaimSummaryDto(
                c.Id, c.ClaimNumber, c.PatientName, c.ProviderName, c.ClaimType,
                c.AmountClaimed, c.ServiceDate, c.Status, c.SubmittedAt))
            .ToListAsync(ct);

        return new PagedResult<ClaimSummaryDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<ClaimDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var claim = await db.Claims.AsNoTracking()
            .Include(c => c.Documents)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"Claim {id} was not found.");

        return ToDetail(claim);
    }

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct)
    {
        var counts = await db.Claims
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Every status appears in the result, even with a zero count, so the UI never has gaps.
        var byStatus = Enum.GetValues<ClaimStatus>()
            .ToDictionary(s => s.ToString(), s => counts.FirstOrDefault(x => x.Status == s)?.Count ?? 0);

        // Summed in memory because SQLite cannot SUM a decimal column (see ClaimWiseDbContext).
        var amounts = await db.Claims.AsNoTracking().Select(c => c.AmountClaimed).ToListAsync(ct);

        var openClaims = await db.Claims.AsNoTracking()
            .Include(c => c.Documents)
            .Where(c => OpenStatuses.Contains(c.Status))
            .ToListAsync(ct);

        return new DashboardDto(
            TotalClaims: amounts.Count,
            ByStatus: byStatus,
            TotalAmountClaimed: amounts.Sum(),
            OpenClaimsWithIncompleteSubmission: openClaims.Count(c => !ClaimRules.Validate(c).IsComplete));
    }

    public async Task<ValidationDto> GetValidationAsync(int id, CancellationToken ct)
    {
        var claim = await db.Claims.AsNoTracking()
            .Include(c => c.Documents)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"Claim {id} was not found.");

        return ToValidationDto(ClaimRules.Validate(claim));
    }

    public async Task<DocumentDto> AddDocumentAsync(int id, AddDocumentRequest request, CancellationToken ct)
    {
        var claim = await db.Claims.Include(c => c.Documents)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"Claim {id} was not found.");

        if (ClaimWorkflow.IsFinal(claim.Status))
            throw new BusinessRuleException($"Claim {claim.ClaimNumber} is {claim.Status}, so documents can no longer be added.");

        var type = request.DocumentType!.Value;
        if (claim.Documents.Any(d => d.DocumentType == type))
            throw new BusinessRuleException($"Claim {claim.ClaimNumber} already has a {type} document.");

        var now = clock.GetUtcNow().UtcDateTime;
        var doc = new ClaimDocument { DocumentType = type, FileName = request.FileName.Trim(), ReceivedAt = now };

        claim.Documents.Add(doc);
        claim.UpdatedAt = now;
        claim.Version++;
        db.AuditEntries.Add(new AuditEntry
        {
            ClaimId = claim.Id,
            Action = "DocumentAdded",
            Details = $"{type}: {doc.FileName}",
            Actor = "intake",
            Timestamp = now
        });

        await SaveWithConcurrencyCheckAsync(claim.ClaimNumber, ct);
        return new DocumentDto(doc.Id, doc.DocumentType, doc.FileName, doc.ReceivedAt);
    }

    public async Task<ClaimDetailDto> ChangeStatusAsync(int id, ChangeStatusRequest request, CancellationToken ct)
    {
        var claim = await db.Claims.Include(c => c.Documents)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"Claim {id} was not found.");

        if (request.ExpectedVersion is { } expected && expected != claim.Version)
            throw new BusinessRuleException(
                $"Claim {claim.ClaimNumber} was changed by someone else after you opened it (version {claim.Version}, you had {expected}). Reload and review the latest state before deciding.");

        var from = claim.Status;
        var to = request.NewStatus!.Value;

        if (!ClaimWorkflow.CanTransition(from, to))
        {
            var allowed = ClaimWorkflow.AllowedNext(from);
            var allowedText = allowed.Count == 0 ? "none (this status is final)" : string.Join(", ", allowed);
            throw new BusinessRuleException($"A claim cannot move from {from} to {to}. Allowed next statuses: {allowedText}.");
        }

        // An AI summary with findings must be accepted or rejected by a person before the claim can be decided.
        if (to is ClaimStatus.Approved or ClaimStatus.Rejected)
        {
            var pending = await db.AiSummaries.AsNoTracking()
                .Where(s => s.ClaimId == claim.Id && s.ReviewState == SummaryReviewState.PendingReview)
                .Select(s => s.Id).ToListAsync(ct);
            if (pending.Count > 0)
                throw new BusinessRuleException(
                    $"AI summary {string.Join(", ", pending)} for claim {claim.ClaimNumber} has not been reviewed. " +
                    "Accept or reject it (POST /api/claims/{id}/summaries/{summaryId}/review) before deciding the claim.");
        }

        // A human reviewer must not approve a claim whose submission is incomplete.
        if (to == ClaimStatus.Approved)
        {
            var validation = ClaimRules.Validate(claim);
            if (!validation.IsComplete)
            {
                var problems = validation.MissingDocuments.Select(d => $"document {d}")
                    .Concat(validation.MissingFields.Select(f => $"field {f}"));
                throw new BusinessRuleException($"Cannot approve an incomplete claim. Missing: {string.Join(", ", problems)}.");
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var reviewer = request.ReviewerName.Trim();
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        claim.Status = to;
        claim.ReviewerName = reviewer;
        // The note belongs to the latest decision; keeping an older one would show it next to a different reviewer.
        claim.ReviewerNote = note;
        claim.UpdatedAt = now;
        claim.Version++;

        // Make AI involvement explicit in the audit trail of a decision: which summaries a person reviewed first.
        var reviewedSummaries = to is ClaimStatus.Approved or ClaimStatus.Rejected
            ? await db.AiSummaries.AsNoTracking()
                .Where(s => s.ClaimId == claim.Id && (s.ReviewState == SummaryReviewState.Accepted || s.ReviewState == SummaryReviewState.Rejected))
                .OrderBy(s => s.Id).Select(s => $"#{s.Id} {s.ReviewState}").ToListAsync(ct)
            : [];
        var aiNote = reviewedSummaries.Count > 0 ? $" [AI summaries reviewed by a person before this decision: {string.Join(", ", reviewedSummaries)}]" : "";

        db.AuditEntries.Add(new AuditEntry
        {
            ClaimId = claim.Id,
            Action = "StatusChanged",
            Details = (note is null ? $"{from} -> {to}" : $"{from} -> {to}. Note: {note}") + aiNote,
            Actor = reviewer,
            Timestamp = now
        });

        await SaveWithConcurrencyCheckAsync(claim.ClaimNumber, ct);
        logger.LogInformation("Claim {ClaimNumber} moved {From} -> {To} by {Reviewer}", claim.ClaimNumber, from, to, reviewer);
        return ToDetail(claim);
    }

    public async Task<IReadOnlyList<AuditEntryDto>> GetAuditAsync(int id, CancellationToken ct)
    {
        if (!await db.Claims.AnyAsync(c => c.Id == id, ct))
            throw new NotFoundException($"Claim {id} was not found.");

        return await db.AuditEntries.AsNoTracking()
            .Where(a => a.ClaimId == id)
            .OrderBy(a => a.Timestamp).ThenBy(a => a.Id)
            .Select(a => new AuditEntryDto(a.Id, a.Action, a.Details, a.Actor, a.Timestamp))
            .ToListAsync(ct);
    }

    // ---------- helpers ----------

    // A secondary sort on Id keeps page boundaries stable when many rows share the same sort value.
    private static IOrderedQueryable<Claim> ApplySort(IQueryable<Claim> q, string sortBy, bool desc) =>
        sortBy.ToLowerInvariant() switch
        {
            "servicedate" => desc ? q.OrderByDescending(c => c.ServiceDate).ThenBy(c => c.Id)
                                  : q.OrderBy(c => c.ServiceDate).ThenBy(c => c.Id),
            "amount" => desc ? q.OrderByDescending(c => c.AmountClaimed).ThenBy(c => c.Id)
                             : q.OrderBy(c => c.AmountClaimed).ThenBy(c => c.Id),
            "claimnumber" => desc ? q.OrderByDescending(c => c.ClaimNumber).ThenBy(c => c.Id)
                                  : q.OrderBy(c => c.ClaimNumber).ThenBy(c => c.Id),
            // Workflow order (Submitted first), not alphabetical, because that is what reviewers expect.
            "status" => desc ? q.OrderByDescending(StatusRank).ThenBy(c => c.Id)
                             : q.OrderBy(StatusRank).ThenBy(c => c.Id),
            _ => desc ? q.OrderByDescending(c => c.SubmittedAt).ThenBy(c => c.Id)
                      : q.OrderBy(c => c.SubmittedAt).ThenBy(c => c.Id)
        };

    // An expression (not a method) so EF Core translates it to a SQL CASE.
    private static readonly System.Linq.Expressions.Expression<Func<Claim, int>> StatusRank = c =>
        c.Status == ClaimStatus.Submitted ? 0 :
        c.Status == ClaimStatus.InReview ? 1 :
        c.Status == ClaimStatus.NeedsInformation ? 2 :
        c.Status == ClaimStatus.Approved ? 3 : 4;

    // Two reviewers saving the same claim at once: the second save fails instead of silently overwriting the first.
    private async Task SaveWithConcurrencyCheckAsync(string claimNumber, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException($"Claim {claimNumber} was changed by someone else while you were saving. Reload and try again.");
        }
    }

    internal static DocumentDto ToDocumentDto(ClaimDocument d) =>
        new(d.Id, d.DocumentType, d.FileName, d.ReceivedAt, d.SizeBytes, d.Sha256, d.StoredFileName is not null);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static ValidationDto ToValidationDto(ClaimValidationResult v) =>
        new(v.IsComplete, v.RequiredDocuments, v.MissingDocuments, v.MissingFields);

    private static ClaimDetailDto ToDetail(Claim c) => new(
        c.Id, c.ClaimNumber, c.PatientName, c.MemberId, c.PolicyNumber, c.ProviderName,
        c.ClaimType, c.DiagnosisCode, c.ProcedureCode, c.AmountClaimed, c.ServiceDate,
        c.Status, c.SubmittedAt, c.UpdatedAt, c.Version, c.ReviewerName, c.ReviewerNote,
        c.Documents.OrderBy(d => d.Id).Select(ToDocumentDto).ToList(),
        ToValidationDto(ClaimRules.Validate(c)),
        ClaimWorkflow.AllowedNext(c.Status));
}
