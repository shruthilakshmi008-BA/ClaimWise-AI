namespace ClaimWise.Api.Domain;

/// <summary>An uploaded (fictional) policy document. Only extracted text is stored, never the original file.</summary>
public class PolicyDocument
{
    public int Id { get; set; }
    public string Title { get; set; } = "";

    /// <summary>Groups versions of the same policy (e.g. "gold-plan"). Passages are only compared for conflicts within a family.</summary>
    public string? PolicyFamily { get; set; }
    public string? Version { get; set; }
    public DateOnly? EffectiveDate { get; set; }

    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }

    /// <summary>SHA-256 of the uploaded bytes (hex). Used to refuse duplicate uploads.</summary>
    public string Sha256 { get; set; } = "";
    public int PageCount { get; set; }
    public int ChunkCount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime UploadedAt { get; set; }
    public string UploadedBy { get; set; } = "";

    public List<PolicyChunk> Chunks { get; set; } = new();
}

/// <summary>A retrievable passage with its source metadata and embedding.</summary>
public class PolicyChunk
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int Ordinal { get; set; }
    public string Text { get; set; } = "";
    public string? Heading { get; set; }
    public string? ClauseRef { get; set; }
    public string SectionPath { get; set; } = "";
    public int PageNumber { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public int WordCount { get; set; }

    /// <summary>Identifies the embedding model that produced <see cref="Embedding"/>; vectors of different models are never mixed.</summary>
    public string EmbeddingModel { get; set; } = "";
    public byte[] Embedding { get; set; } = [];
}
