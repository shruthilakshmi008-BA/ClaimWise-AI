using System.Net;
using System.Text;
using System.Text.Json;
using ClaimWise.Rag.Embeddings;
using Microsoft.Extensions.Logging;

namespace ClaimWise.Rag.Llm;

public sealed record OpenAiCompatibleOptions
{
    /// <summary>Base URL up to and including the version segment, e.g. https://api.openai.com/v1 or http://localhost:11434/v1.</summary>
    public string BaseUrl { get; init; } = "";
    public string? ApiKey { get; init; }
    public string Model { get; init; } = "";
    public int TimeoutSeconds { get; init; } = 60;
    public int MaxRetries { get; init; } = 2;
    public int RetryBaseDelayMs { get; init; } = 500;

    /// <summary>Ask for response_format=json_object. Disable for servers that reject the field.</summary>
    public bool JsonMode { get; init; } = true;

    /// <summary>Embedding vector size (only used by the embedding provider).</summary>
    public int Dimensions { get; init; } = 1536;

    /// <summary>Whether to send the "dimensions" request parameter (supported by OpenAI text-embedding-3 models).</summary>
    public bool SendDimensions { get; init; }
}

/// <summary>Shared HTTP behaviour: per-attempt timeout, bounded retries with backoff for transient failures, safe errors.</summary>
internal static class ProviderHttp
{
    public static async Task<JsonDocument> PostJsonAsync(
        HttpClient http, Uri url, object body, Action<HttpRequestMessage> configure,
        OpenAiCompatibleOptions options, string provider, ILogger? logger, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(body);
        Exception? last = null;

        for (int attempt = 0; attempt <= options.MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

            TimeSpan? retryAfter = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                configure(request);

                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
                var text = await response.Content.ReadAsStringAsync(timeout.Token);

                if (response.IsSuccessStatusCode)
                {
                    try { return JsonDocument.Parse(text); }
                    catch (JsonException ex) { throw new ProviderException($"{provider} returned a response that is not valid JSON.", false, ex); }
                }

                var status = (int)response.StatusCode;
                if (status is 401 or 403)
                    throw new ProviderException($"{provider} rejected the credentials (HTTP {status}). Check the API key.", false);

                var transient = status is 408 or 429 || status >= 500;
                var detail = ExtractErrorMessage(text);
                last = new ProviderException($"{provider} returned HTTP {status}{detail}", transient);
                if (!transient) throw last;

                if (response.Headers.RetryAfter?.Delta is { } delta) retryAfter = delta;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                last = new ProviderException($"{provider} did not answer within {options.TimeoutSeconds}s.", true);
            }
            catch (HttpRequestException ex)
            {
                last = new ProviderException($"{provider} could not be reached ({ex.HttpRequestError}).", true, ex);
            }

            if (attempt < options.MaxRetries)
            {
                var delay = retryAfter is { } ra && ra <= TimeSpan.FromSeconds(10)
                    ? ra
                    : TimeSpan.FromMilliseconds(options.RetryBaseDelayMs * Math.Pow(2, attempt));
                logger?.LogWarning("{Provider} attempt {Attempt} failed ({Reason}); retrying in {Delay}ms",
                    provider, attempt + 1, last.Message, (int)delay.TotalMilliseconds);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            }
        }

        throw new ProviderException($"{last!.Message} (gave up after {options.MaxRetries + 1} attempts)", true, last);
    }

    // Provider error bodies ("error": {"message": ...}) are useful and not sensitive; keep them short.
    private static string ExtractErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var msg = err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var m) ? m.GetString() : err.GetString();
                if (!string.IsNullOrWhiteSpace(msg)) return ": " + (msg.Length > 200 ? msg[..200] + "..." : msg);
            }
        }
        catch (JsonException) { }
        return "";
    }

    public static Uri Combine(string baseUrl, string path)
    {
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/" + path.TrimStart('/'), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ProviderException("The provider BaseUrl is not a valid http(s) URL.", false);
        return uri;
    }
}
