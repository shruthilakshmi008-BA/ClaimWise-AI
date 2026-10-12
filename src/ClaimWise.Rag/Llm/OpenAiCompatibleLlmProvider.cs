using System.Net.Http.Headers;
using System.Text.Json;
using ClaimWise.Rag.Embeddings;
using Microsoft.Extensions.Logging;

namespace ClaimWise.Rag.Llm;

/// <summary>
/// Chat Completions client for any OpenAI-compatible endpoint (OpenAI, Azure OpenAI-compatible gateways, Ollama,
/// vLLM, LM Studio...). Prompts and responses are never logged.
/// </summary>
public sealed class OpenAiCompatibleLlmProvider(HttpClient http, OpenAiCompatibleOptions options, ILogger<OpenAiCompatibleLlmProvider>? logger = null)
    : ILlmProvider
{
    public string ProviderName => "openai-compatible";
    public string ModelId => options.Model;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = options.Model,
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxOutputTokens,
            ["messages"] = new[]
            {
                new { role = "system", content = request.System },
                new { role = "user", content = request.User }
            }
        };
        if (options.JsonMode) body["response_format"] = new { type = "json_object" };

        using var doc = await ProviderHttp.PostJsonAsync(
            http, ProviderHttp.Combine(options.BaseUrl, "chat/completions"), body,
            r =>
            {
                if (!string.IsNullOrEmpty(options.ApiKey))
                    r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            },
            options, "The language model provider", logger, ct);

        try
        {
            var choice = doc.RootElement.GetProperty("choices")[0];
            var content = choice.GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
                throw new ProviderException("The language model returned an empty answer.", false);

            int? input = null, output = null;
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pi)) input = pi;
                if (usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var ci)) output = ci;
            }
            string? finish = choice.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
            return new LlmResponse(content, input, output, finish);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new ProviderException("The language model response had an unexpected shape.", false, ex);
        }
    }
}
