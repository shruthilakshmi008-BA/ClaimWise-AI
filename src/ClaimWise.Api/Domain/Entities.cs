namespace ClaimWise.Api.Domain;

public class Claim
{
    public int Id { get; set; }
    public string ClaimNumber { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string MemberId { get; set; } = "";
    public string PolicyNumber { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public ClaimType ClaimType { get; set; }
    public string? DiagnosisCode { get; set; }
    public string? ProcedureCode { get; set; }
    public decimal AmountClaimed { get; set; }
    public DateOnly ServiceDate { get; set; }
    public ClaimStatus Status { get; set; } = ClaimStatus.Submitted;
    public DateTime SubmittedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? ReviewerName { get; set; }
    public string? ReviewerNote { get; set; }

    /// <summary>Optimistic-concurrency token. Incremented on every update so concurrent edits are detected.</summary>
    public int Version { get; set; }

    public List<ClaimDocument> Documents { get; set; } = new();
    public List<AuditEntry> AuditEntries { get; set; } = new();
}

public class ClaimDocument
{
    public int Id { get; set; }
    public int ClaimId { get; set; }
    public DocumentType DocumentType { get; set; }
    public string FileName { get; set; } = "";
    public DateTime ReceivedAt { get; set; }

    // Set only when the file itself was uploaded (as opposed to just recording that a document was received).
    public long? SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string? ContentType { get; set; }

    /// <summary>Server-generated storage name (GUID + extension). Never derived from the uploaded file name.</summary>
    public string? StoredFileName { get; set; }
}

public class AuditEntry
{
    public int Id { get; set; }
    public int ClaimId { get; set; }
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
    public string Actor { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
