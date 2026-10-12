using ClaimWise.Rag.Grounding;
using ClaimWise.Rag.Llm;
using ClaimWise.Rag.Retrieval;

namespace ClaimWise.Api.Ai;

/// <summary>Bound from the "Ai" configuration section. Secrets (API keys) belong in environment variables or user-secrets, never in Git.</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public EmbeddingsSection Embeddings { get; init; } = new();
    public LlmSection Llm { get; init; } = new();
    public RetrievalOptions Retrieval { get; init; } = new();
    public EvidenceOptions Evidence { get; init; } = new();
    public ValidationOptions Validation { get; init; } = new();
    public UploadSection Uploads { get; init; } = new();
}

public sealed class EmbeddingsSection
{
    /// <summary>"Local" (offline hashing embedder) or "OpenAiCompatible".</summary>
    public string Provider { get; init; } = "Local";
    public int LocalDimensions { get; init; } = 512;
    public OpenAiCompatibleOptions OpenAiCompatible { get; init; } = new();
}

public sealed class LlmSection
{
    /// <summary>"Extractive" (no model, offline), "OpenAiCompatible" or "Anthropic".</summary>
    public string Provider { get; init; } = "Extractive";
    public OpenAiCompatibleOptions OpenAiCompatible { get; init; } = new();
    public AnthropicSection Anthropic { get; init; } = new();
}

public sealed class AnthropicSection
{
    /// <summary>If empty, the SDK reads ANTHROPIC_API_KEY.</summary>
    public string? ApiKey { get; init; }
    public string Model { get; init; } = "claude-opus-5-5";
    public int MaxOutputTokens { get; init; } = 4096;
    public int TimeoutSeconds { get; init; } = 120;
    public int MaxRetries { get; init; } = 2;
}

public sealed class UploadSection
{
    public long MaxBytes { get; init; } = 10 * 1024 * 1024;
    public int MaxChunksPerDocument { get; init; } = 2000;
}
