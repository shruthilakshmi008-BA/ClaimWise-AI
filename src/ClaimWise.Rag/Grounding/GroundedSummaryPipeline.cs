using ClaimWise.Rag.Embeddings;
using ClaimWise.Rag.Retrieval;
using Microsoft.Extensions.Logging;

namespace ClaimWise.Rag.Grounding;

public sealed record PipelineOptions
{
    /// <summary>At most this many supporting passages are shown to the generator.</summary>
    public int MaxPassagesForGeneration { get; init; } = 5;
}

/// <summary>
/// Orchestrates one grounded summary: assess the evidence (abstain if weak) → detect conflicts → generate a draft →
/// validate every citation → add system-written conflict notices. The only text that reaches the result is text that
/// passed validation or was assembled by the system from verbatim passage excerpts.
/// </summary>
public sealed class GroundedSummaryPipeline(
    ISummaryGenerator generator,
    EvidenceAssessor assessor,
    ConflictDetector conflictDetector,
    CitationValidator validator,
    PipelineOptions? options = null,
    ILogger<GroundedSummaryPipeline>? logger = null)
{
    private readonly PipelineOptions _o = options ?? new PipelineOptions();

    public async Task<GroundedSummaryResult> RunAsync(
        ClaimContext claim, string question, RetrievalResult retrieval, PolicyIndex index, CancellationToken ct)
    {
        var labelled = retrieval.Passages
            .Select((p, i) => new EvidencePassage($"P{i + 1}", p))
            .ToList();

        var assessment = assessor.Assess(retrieval.Query, labelled, index);
        if (!assessment.IsSufficient)
        {
            return Result(SummaryOutcome.Abstained, [], labelled, assessment, [], [], [],
                abstainReason: "Insufficient policy evidence: " + string.Join(" ", assessment.Reasons));
        }

        var supporting = labelled
            .Where(p => p.Retrieved.RerankScore >= assessor.Options.SupportThreshold)
            .Take(_o.MaxPassagesForGeneration)
            .ToList();

        var warnings = new List<string>();
        var families = supporting.Select(p => p.Chunk.PolicyFamily).Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (families.Count > 1)
            warnings.Add($"Evidence comes from {families.Count} different policies ({string.Join(", ", families)}). " +
                         $"Confirm which one applies to policy number {claim.PolicyNumber}.");

        var conflicts = conflictDetector.Detect(supporting);

        DraftSummary draft;
        try
        {
            draft = await generator.GenerateAsync(new SummaryRequest(claim, question, supporting, conflicts), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is ProviderException or SummaryGenerationException)
        {
            logger?.LogWarning(ex, "Summary generation failed for claim {Claim}", claim.ClaimNumber);
            return Result(SummaryOutcome.Failed, [], labelled, assessment, conflicts, [], warnings, failure: ex.Message);
        }

        if (draft.Abstain)
        {
            return Result(SummaryOutcome.Abstained, [], labelled, assessment, conflicts, [], warnings,
                abstainReason: string.IsNullOrWhiteSpace(draft.AbstainReason)
                    ? "The summariser judged the evidence insufficient."
                    : "The summariser abstained: " + draft.AbstainReason.Trim());
        }

        var report = validator.Validate(draft, supporting, claim);
        var statements = report.Accepted.ToList();

        if (statements.Count == 0)
        {
            return Result(SummaryOutcome.Abstained, [], labelled, assessment, conflicts, report.Rejected, warnings,
                abstainReason: "No statement could be verified against the policy text, so nothing is shown as a finding.");
        }

        if (report.Rejected.Count > 0)
            warnings.Add($"{report.Rejected.Count} generated statement(s) were removed because their citations could not be verified.");

        statements.AddRange(ConflictNotices(conflicts, supporting));
        return Result(SummaryOutcome.Answered, statements, labelled, assessment, conflicts, report.Rejected, warnings);
    }

    /// <summary>
    /// System-written notices that quote both sides verbatim. They are always present when a conflict was detected,
    /// regardless of whether the generator mentioned it.
    /// </summary>
    private static IEnumerable<SummaryStatement> ConflictNotices(IReadOnlyList<EvidenceConflict> conflicts, IReadOnlyList<EvidencePassage> passages)
    {
        foreach (var conflict in conflicts)
        {
            var citations = conflict.PassageLabels
                .Select(label => passages.First(p => p.Label == label))
                .Select(p => new Citation(p.Label, Excerpt(p.Chunk.Text)))
                .ToList();
            yield return new SummaryStatement("Conflicting policy wording: " + conflict.Description + " A reviewer must resolve this before relying on either passage.",
                citations, StatementOrigin.System);
        }
    }

    private static string Excerpt(string text)
    {
        var normalized = TextAnalysis.NormalizeWhitespace(text);
        if (normalized.Length <= 240) return normalized;
        var cut = normalized.LastIndexOf(' ', 240);
        return normalized[..(cut > 0 ? cut : 240)];
    }

    private GroundedSummaryResult Result(
        SummaryOutcome outcome, IReadOnlyList<SummaryStatement> statements, IReadOnlyList<EvidencePassage> passages,
        EvidenceAssessment assessment, IReadOnlyList<EvidenceConflict> conflicts, IReadOnlyList<RejectedStatement> rejected,
        IReadOnlyList<string> warnings, string? abstainReason = null, string? failure = null) =>
        new(outcome, statements, passages, assessment, conflicts, rejected, warnings, abstainReason, failure, generator.Name, generator.ModelId);
}
