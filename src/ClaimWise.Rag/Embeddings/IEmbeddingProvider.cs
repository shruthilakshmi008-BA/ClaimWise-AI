namespace ClaimWise.Rag.Embeddings;

public enum EmbeddingPurpose { Document, Query }

/// <summary>
/// Turns text into vectors. Implementations: <see cref="HashingEmbeddingProvider"/> (local, offline) and
/// <see cref="OpenAiCompatibleEmbeddingProvider"/> (any /v1/embeddings endpoint, e.g. OpenAI, Ollama, vLLM).
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Identifies the vector space. Stored with each chunk; vectors from different models are never compared.</summary>
    string ModelId { get; }

    int Dimensions { get; }

    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken ct);
}

/// <summary>A provider call failed. <see cref="IsTransient"/> tells callers whether a retry could help.</summary>
public sealed class ProviderException(string message, bool isTransient = false, Exception? inner = null)
    : Exception(message, inner)
{
    public bool IsTransient { get; } = isTransient;
}
