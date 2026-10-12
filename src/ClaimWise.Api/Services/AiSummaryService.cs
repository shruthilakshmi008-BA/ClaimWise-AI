using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimWise.Api.Ai;
using ClaimWise.Api.Data;
using ClaimWise.Api.Domain;
using ClaimWise.Api.Dtos;
using ClaimWise.Api.Infrastructure;
using ClaimWise.Rag.Embeddings;
using ClaimWise.Rag.Grounding;
using ClaimWise.Rag.Retrieval;
using Microsoft.EntityFrameworkCore;

namespace ClaimWise.Api.Services;

public interface IAiSummaryService
{
    Task<AiSummaryDto> GenerateAsync(int claimId, GenerateSummaryRequest request, CancellationToken ct);
    Task<IReadOnlyList<AiSummaryDto>> ListAsync(int claimId, CancellationToken ct);
    Task<AiSummaryDto> GetAsync(int claimId, int summaryId, CancellationToken ct);
    Task<AiSummaryDto> ReviewAsync(int claimId, int summaryId, ReviewSummaryRequest request, CancellationToken ct);
}

/// <summary>
/// Generates an evidence-grounded summary for a claim and records it. The summary never changes the claim's status:
/// it is stored as <c>PendingReview</c> and a human must accept or reject it (both are audited) before the claim
/// can be approved or rejected.
/// </summary>
public sealed class AiSummaryService(
    ClaimWiseDbContext db,
    HybridRetriever retriever,
    PolicyIndexCache cache,
    GroundedSummaryPipeline pipeline,
    IEmbeddingProvider embeddings,
    Microsoft.Extensions.Options.IOptions<AiOptions> options,
    TimeProvider clock,
    ILogger<AiSummaryService> logger) : IAiSummaryService
{
    public const string Notice = "AI-assisted evidence summary for a human reviewer. It is not a coverage decision and does not change the claim status.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task<AiSummaryDto> GenerateAsync(int claimId, GenerateSummaryRequest request, CancellationToken ct)
    {
        var claim = await db.Claims.AsNoTracking().Include(c => c.Documents).FirstOrDefaultAsync(c => c.Id == claimId, ct)
                    ?? throw new NotFoundException($"Claim {claimId} was not found.");
        if (ClaimWorkflow.IsFinal(claim.Status))
            throw new BusinessRuleException($"Claim {claim.ClaimNumber} is {claim.Status}; new AI summaries can no longer be requested.");

        var validation = ClaimRules.Validate(claim);
        var missing = validation.MissingDocuments.Select(d => $"document {d}").Concat(validation.MissingFields.Select(f => $"field {f}")).ToList();
        var context = new ClaimContext(claim.ClaimNumber, claim.ClaimType.ToString(), claim.DiagnosisCode, claim.ProcedureCode,
            claim.AmountClaimed, claim.ServiceDate.ToString("yyyy-MM-dd"), claim.PolicyNumber, missing);

        var customQuestion = !string.IsNullOrWhiteSpace(request.Question);
        var question = customQuestion ? request.Question!.Trim() : DefaultQuestion(claim);
        var retrievalQuery = BuildRetrievalQuery(claim, question, customQuestion);

        var index = await cache.GetScopedAsync(request.DocumentIds, ct);
        var retrieval = await retriever.SearchAsync(index, retrievalQuery, ct, options.Value.Retrieval);
        var result = await pipeline.RunAsync(context, question, retrieval, index, ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var summary = ToEntity(claim, request, question, retrievalQuery, result, now);
        db.AiSummaries.Add(summary);
        db.AuditEntries.Add(new AuditEntry
        {
            ClaimId = claim.Id,
            Action = "AiSummaryGenerated",
            Details = $"{result.Outcome} via {result.GeneratorName}{(result.ModelId is null ? "" : " (" + result.ModelId + ")")}; " +
                      $"{summary.Statements.Count} statement(s), {summary.Passages.Count} passage(s). Awaiting human review: {(summary.ReviewState == SummaryReviewState.PendingReview ? "yes" : "no")}.",
            Actor = request.RequestedBy.Trim(),
            Timestamp = now
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("AI summary {SummaryId} for claim {Claim}: {Outcome}, generator {Generator}", summary.Id, claim.ClaimNumber, result.Outcome, result.GeneratorName);
        return ToDto(summary);
    }

    public async Task<IReadOnlyList<AiSummaryDto>> ListAsync(int claimId, CancellationToken ct)
    {
        await EnsureClaimAsync(claimId, ct);
        var rows = await Query().Where(s => s.ClaimId == claimId).OrderByDescending(s => s.GeneratedAt).ThenByDescending(s => s.Id).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<AiSummaryDto> GetAsync(int claimId, int summaryId, CancellationToken ct)
    {
        var summary = await Query().FirstOrDefaultAsync(s => s.ClaimId == claimId && s.Id == summaryId, ct)
                      ?? throw new NotFoundException($"AI summary {summaryId} was not found for claim {claimId}.");
        return ToDto(summary);
    }

    public async Task<AiSummaryDto> ReviewAsync(int claimId, int summaryId, ReviewSummaryRequest request, CancellationToken ct)
    {
        var summary = await db.AiSummaries.Include(s => s.Statements).Include(s => s.Passages)
                          .FirstOrDefaultAsync(s => s.ClaimId == claimId && s.Id == summaryId, ct)
                      ?? throw new NotFoundException($"AI summary {summaryId} was not found for claim {claimId}.");

        if (summary.ReviewState != SummaryReviewState.PendingReview)
            throw new BusinessRuleException(summary.ReviewState == SummaryReviewState.NotRequired
                ? "This summary contains no findings (it abstained or failed), so there is nothing to review."
                : $"This summary was already {summary.ReviewState.ToString().ToLowerInvariant()} by {summary.ReviewedBy}.");

        var now = clock.GetUtcNow().UtcDateTime;
        var reviewer = request.ReviewerName.Trim();
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        summary.ReviewState = request.Decision!.Value;
        summary.ReviewedBy = reviewer;
        summary.ReviewedAt = now;
        summary.ReviewNote = note;

        db.AuditEntries.Add(new AuditEntry
        {
            ClaimId = claimId,
            Action = "AiSummaryReviewed",
            Details = $"Summary {summary.Id} {summary.ReviewState}" + (note is null ? "." : $". Note: {note}"),
            Actor = reviewer,
            Timestamp = now
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("AI summary {SummaryId} {State} by {Reviewer}", summary.Id, summary.ReviewState, reviewer);
        return ToDto(summary);
    }

    // ---------- helpers ----------

    private IQueryable<AiSummary> Query() => db.AiSummaries.AsNoTracking().Include(s => s.Statements).Include(s => s.Passages).AsSplitQuery();

    private async Task EnsureClaimAsync(int claimId, CancellationToken ct)
    {
        if (!await db.Claims.AnyAsync(c => c.Id == claimId, ct))
            throw new NotFoundException($"Claim {claimId} was not found.");
    }

    internal static string DefaultQuestion(Claim claim) =>
        $"{claim.ClaimType} benefit annual limit and co-payment";

    // A reviewer's own question is searched as written (plus the claim type). The procedure code is added only to the
    // default claim-overview question: a rare code token otherwise dominates keyword ranking and pushes the passages
    // the reviewer actually asked about out of the results (seen in end-to-end testing). A reviewer who wants code
    // matching can type the code in the question. The assessor ignores codes for topical coverage.
    internal static string BuildRetrievalQuery(Claim claim, string question, bool customQuestion) =>
        string.Join(' ', new[] { question, claim.ClaimType.ToString(), customQuestion ? null : claim.ProcedureCode }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private AiSummary ToEntity(Claim claim, GenerateSummaryRequest request, string question, string retrievalQuery,
        GroundedSummaryResult result, DateTime now)
    {
        var status = result.Outcome switch
        {
            SummaryOutcome.Answered => AiSummaryStatus.Answered,
            SummaryOutcome.Abstained => AiSummaryStatus.Abstained,
            _ => AiSummaryStatus.Failed
        };

        var summary = new AiSummary
        {
            ClaimId = claim.Id,
            Question = question,
            RetrievalQuery = retrievalQuery.Length > 1000 ? retrievalQuery[..1000] : retrievalQuery,
            Status = status,
            GeneratedAt = now,
            RequestedBy = request.RequestedBy.Trim(),
            ClaimVersionAtGeneration = claim.Version,
            GeneratorName = result.GeneratorName,
            ModelId = result.ModelId,
            EmbeddingModel = embeddings.ModelId,
            AssessmentJson = JsonSerializer.Serialize(new EvidenceDto(result.Assessment.Verdict.ToString(), result.Assessment.Reasons,
                Math.Round(result.Assessment.TopScore, 4), Math.Round(result.Assessment.Coverage, 4), result.Assessment.SupportingPassages, result.Assessment.MissingTerms), Json),
            ConflictsJson = JsonSerializer.Serialize(result.Conflicts.Select(c => new ConflictDto(c.Kind, c.PassageLabels, c.Description)), Json),
            WarningsJson = JsonSerializer.Serialize(result.Warnings, Json),
            RejectedJson = JsonSerializer.Serialize(result.Rejected.Select(r => new RejectedStatementDto(r.Text, r.Reason)), Json),
            AbstainReason = Truncate(result.AbstainReason, 2000),
            FailureReason = Truncate(result.FailureReason, 1000),
            // Only summaries that contain findings need a human verdict before the claim can be decided.
            ReviewState = status == AiSummaryStatus.Answered ? SummaryReviewState.PendingReview : SummaryReviewState.NotRequired,
            Statements = result.Statements.Select((s, i) => new AiSummaryStatementRecord
            {
                Ordinal = i + 1,
                Text = Truncate(s.Text, 1500)!,
                Origin = s.Origin.ToString(),
                CitationsJson = JsonSerializer.Serialize(s.Citations.Select(c => new CitationDto(c.PassageLabel, c.Quote)), Json)
            }).ToList(),
            Passages = result.Passages.Select(p => new AiSummaryPassage
            {
                Label = p.Label, Rank = p.Retrieved.Rank, ChunkId = p.Chunk.Id, DocumentId = p.Chunk.DocumentId,
                DocumentTitle = p.Chunk.DocumentTitle, DocumentVersion = p.Chunk.DocumentVersion, ClauseRef = p.Chunk.ClauseRef,
                Heading = p.Chunk.Heading, SectionPath = p.Chunk.SectionPath, PageNumber = p.Chunk.PageNumber, Text = p.Chunk.Text,
                RerankScore = Math.Round(p.Retrieved.RerankScore, 4), KeywordScore = p.Retrieved.KeywordScore, VectorScore = p.Retrieved.VectorScore
            }).ToList()
        };
        return summary;
    }

    private static string? Truncate(string? v, int max) => v is null || v.Length <= max ? v : v[..max];

    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)!;

    internal static AiSummaryDto ToDto(AiSummary s) => new(
        s.Id, s.ClaimId, s.Question, s.Status, s.GeneratedAt, s.RequestedBy, s.GeneratorName, s.ModelId, s.EmbeddingModel,
        UsedLanguageModel: s.GeneratorName.StartsWith("llm:", StringComparison.Ordinal),
        s.ClaimVersionAtGeneration,
        s.Statements.OrderBy(x => x.Ordinal).Select(x => new SummaryStatementDto(x.Ordinal, x.Text, x.Origin, Read<List<CitationDto>>(x.CitationsJson))).ToList(),
        s.Passages.OrderBy(p => p.Rank).Select(p => new SummaryPassageDto(p.Label, p.Rank, p.ChunkId, p.DocumentId, p.DocumentTitle, p.DocumentVersion,
            p.ClauseRef, p.Heading, p.SectionPath, p.PageNumber, p.Text, p.RerankScore, p.KeywordScore, p.VectorScore)).ToList(),
        Read<EvidenceDto>(s.AssessmentJson),
        Read<List<ConflictDto>>(s.ConflictsJson),
        Read<List<string>>(s.WarningsJson),
        Read<List<RejectedStatementDto>>(s.RejectedJson),
        s.AbstainReason, s.FailureReason,
        new SummaryReviewDto(s.ReviewState, s.ReviewedBy, s.ReviewedAt, s.ReviewNote),
        Notice);
}
