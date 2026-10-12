namespace ClaimWise.Rag.Llm;

public sealed record LlmRequest(string System, string User, int MaxOutputTokens = 1500, double Temperature = 0.0);

public sealed record LlmResponse(string Text, int? InputTokens, int? OutputTokens, string? FinishReason);

/// <summary>
/// A chat/completions model. Implementations must throw <see cref="Embeddings.ProviderException"/> for provider
/// failures (timeouts, rate limits, auth, bad responses) and must never log prompts or API keys.
/// </summary>
public interface ILlmProvider
{
    string ProviderName { get; }
    string ModelId { get; }
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}
