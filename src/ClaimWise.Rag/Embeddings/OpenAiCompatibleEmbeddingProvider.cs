using System.Net.Http.Headers;
using System.Text.Json;
using ClaimWise.Rag.Llm;
using Microsoft.Extensions.Logging;

namespace ClaimWise.Rag.Embeddings;

/// <summary>Embeddings client for any OpenAI-compatible /embeddings endpoint. Vectors are L2-normalised.</summary>
public sealed class OpenAiCompatibleEmbeddingProvider(
    HttpClient http, OpenAiCompatibleOptions options, ILogger<OpenAiCompatibleEmbeddingProvider>? logger = null)
    : IEmbeddingProvider
{
    private const int BatchSize = 64;

    public string ModelId => $"openai-compatible:{options.Model}:{options.Dimensions}";
    public int Dimensions => options.Dimensions;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken ct)
    {
        var all = new List<float[]>(texts.Count);
        for (int offset = 0; offset < texts.Count; offset += BatchSize)
        {
            var batch = texts.Skip(offset).Take(BatchSize).ToArray();
            var body = new Dictionary<string, object?> { ["model"] = options.Model, ["input"] = batch };
            if (options.SendDimensions) body["dimensions"] = options.Dimensions;

            using var doc = await ProviderHttp.PostJsonAsync(
                http, ProviderHttp.Combine(options.BaseUrl, "embeddings"), body,
                r =>
                {
                    if (!string.IsNullOrEmpty(options.ApiKey))
                        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                },
                options, "The embedding provider", logger, ct);

            try
            {
                var items = doc.RootElement.GetProperty("data").EnumerateArray()
                    .OrderBy(d => d.TryGetProperty("index", out var i) ? i.GetInt32() : 0)
                    .Select(d => d.GetProperty("embedding").EnumerateArray().Select(x => x.GetSingle()).ToArray())
                    .ToList();

                if (items.Count != batch.Length)
                    throw new ProviderException("The embedding provider returned the wrong number of vectors.", false);
                foreach (var v in items)
                {
                    if (v.Length != options.Dimensions)
                        throw new ProviderException(
                            $"The embedding provider returned {v.Length}-dimensional vectors but {options.Dimensions} are configured.", false);
                    VectorMath.NormalizeInPlace(v);
                    all.Add(v);
                }
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
            {
                throw new ProviderException("The embedding provider response had an unexpected shape.", false, ex);
            }
        }
        return all;
    }
}
