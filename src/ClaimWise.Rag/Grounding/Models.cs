using ClaimWise.Rag.Retrieval;

namespace ClaimWise.Rag.Grounding;

/// <summary>
/// The claim facts shown to the summariser. All values are plain strings/numbers copied from the claim record and are
/// treated as untrusted data in prompts.
/// </summary>
public sealed record ClaimContext(
    string ClaimNumber,
    string ClaimType,
    string? DiagnosisCode,
    string? ProcedureCode,
    decimal AmountClaimed,
    string ServiceDate,
    string PolicyNumber,
    IReadOnlyList<string> MissingItems);

/// <summary>A retrieved passage with the label ("P1") the summary uses to cite it.</summary>
public sealed record EvidencePassage(string Label, RetrievedPassage Retrieved)
{
    public IndexedChunk Chunk => Retrieved.Chunk;
}

public sealed record Citation(string PassageLabel, string Quote);

public enum StatementOrigin { Model, System }

/// <summary>A summary sentence that passed validation. <see cref="Origin"/> tells the UI whether the model or the system wrote it.</summary>
public sealed record SummaryStatement(string Text, IReadOnlyList<Citation> Citations, StatementOrigin Origin);

public sealed record DraftCitation(string PassageLabel, string Quote);
public sealed record DraftStatement(string Text, IReadOnlyList<DraftCitation> Citations);

/// <summary>What a generator proposes, before validation. Nothing in a draft is trusted.</summary>
public sealed record DraftSummary(bool Abstain, string? AbstainReason, IReadOnlyList<DraftStatement> Statements);

public sealed record SummaryRequest(
    ClaimContext Claim,
    string Question,
    IReadOnlyList<EvidencePassage> Passages,
    IReadOnlyList<EvidenceConflict> Conflicts);

public sealed record EvidenceConflict(string Kind, IReadOnlyList<string> PassageLabels, string Description);

public enum EvidenceVerdict { Sufficient, Insufficient }

public sealed record EvidenceAssessment(
    EvidenceVerdict Verdict,
    IReadOnlyList<string> Reasons,
    double TopScore,
    double Coverage,
    int SupportingPassages,
    IReadOnlyList<string> MissingTerms)
{
    public bool IsSufficient => Verdict == EvidenceVerdict.Sufficient;
}

public sealed record RejectedStatement(string Text, string Reason);

public enum SummaryOutcome { Answered, Abstained, Failed }

public sealed record GroundedSummaryResult(
    SummaryOutcome Outcome,
    IReadOnlyList<SummaryStatement> Statements,
    IReadOnlyList<EvidencePassage> Passages,
    EvidenceAssessment Assessment,
    IReadOnlyList<EvidenceConflict> Conflicts,
    IReadOnlyList<RejectedStatement> Rejected,
    IReadOnlyList<string> Warnings,
    string? AbstainReason,
    string? FailureReason,
    string GeneratorName,
    string? ModelId);

/// <summary>Produces a draft summary from claim facts and numbered passages (LLM-backed or extractive).</summary>
public interface ISummaryGenerator
{
    string Name { get; }
    string? ModelId { get; }
    Task<DraftSummary> GenerateAsync(SummaryRequest request, CancellationToken ct);
}

/// <summary>The generator returned something unusable (not JSON, wrong shape, empty response).</summary>
public sealed class SummaryGenerationException(string message, Exception? inner = null) : Exception(message, inner);
