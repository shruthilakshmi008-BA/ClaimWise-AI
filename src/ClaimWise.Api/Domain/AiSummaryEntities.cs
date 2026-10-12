namespace ClaimWise.Api.Domain;

public enum AiSummaryStatus { Answered, Abstained, Failed }

/// <summary>
/// Review state of an AI summary. Only <see cref="AiSummaryStatus.Answered"/> summaries need a human verdict;
/// abstentions and failures contain no findings and are <see cref="NotRequired"/>.
/// </summary>
public enum SummaryReviewState { NotRequired, PendingReview, Accepted, Rejected }

/// <summary>
/// One AI-assisted evidence summary for a claim, with everything needed to audit it later: the question, the exact
/// passages it saw (text snapshot), the validated statements with citations, the evidence assessment and the human review.
/// </summary>
public class AiSummary
{
    public int Id { get; set; }
    public int ClaimId { get; set; }
    public string Question { get; set; } = "";
    public string RetrievalQuery { get; set; } = "";
    public AiSummaryStatus Status { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string RequestedBy { get; set; } = "";
    public int ClaimVersionAtGeneration { get; set; }

    public string GeneratorName { get; set; } = "";
    public string? ModelId { get; set; }
    public string EmbeddingModel { get; set; } = "";

    public string AssessmentJson { get; set; } = "{}";
    public string ConflictsJson { get; set; } = "[]";
    public string WarningsJson { get; set; } = "[]";
    public string RejectedJson { get; set; } = "[]";
    public string? AbstainReason { get; set; }
    public string? FailureReason { get; set; }

    public SummaryReviewState ReviewState { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }

    public List<AiSummaryStatementRecord> Statements { get; set; } = new();
    public List<AiSummaryPassage> Passages { get; set; } = new();
}

public class AiSummaryStatementRecord
{
    public int Id { get; set; }
    public int SummaryId { get; set; }
    public int Ordinal { get; set; }
    public string Text { get; set; } = "";

    /// <summary>"Model" for sentences written by the generator, "System" for conflict notices assembled from verbatim excerpts.</summary>
    public string Origin { get; set; } = "Model";
    public string CitationsJson { get; set; } = "[]";
}

/// <summary>
/// The policy passage as it was when the summary was produced. It is a snapshot (not a foreign key) so the audit
/// record stays intact if the policy document is later deactivated or re-ingested.
/// </summary>
public class AiSummaryPassage
{
    public int Id { get; set; }
    public int SummaryId { get; set; }
    public string Label { get; set; } = "";
    public int Rank { get; set; }
    public int ChunkId { get; set; }
    public int DocumentId { get; set; }
    public string DocumentTitle { get; set; } = "";
    public string? DocumentVersion { get; set; }
    public string? ClauseRef { get; set; }
    public string? Heading { get; set; }
    public string SectionPath { get; set; } = "";
    public int PageNumber { get; set; }
    public string Text { get; set; } = "";
    public double RerankScore { get; set; }
    public double? KeywordScore { get; set; }
    public double? VectorScore { get; set; }
}
