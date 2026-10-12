using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using ClaimWise.Rag.Embeddings;
using ClaimWise.Rag.Llm;

namespace ClaimWise.Api.Ai;

/// <summary>
/// <see cref="ILlmProvider"/> backed by the official Anthropic SDK. Prompts and responses are never logged. Sampling
/// parameters are deliberately not sent: recent Claude models reject non-default temperature values, and the summary
/// is checked by <c>CitationValidator</c> regardless of how the model words it.
/// </summary>
public sealed class AnthropicLlmProvider : ILlmProvider
{
    private readonly AnthropicClient _client;
    private readonly AnthropicSection _options;
    private readonly ILogger<AnthropicLlmProvider> _logger;

    public AnthropicLlmProvider(AnthropicSection options, ILogger<AnthropicLlmProvider> logger)
    {
        _options = options;
        _logger = logger;
        _client = string.IsNullOrWhiteSpace(options.ApiKey)
            ? new AnthropicClient { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds), MaxRetries = options.MaxRetries }
            : new AnthropicClient { ApiKey = options.ApiKey, Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds), MaxRetries = options.MaxRetries };
    }

    public string ProviderName => "anthropic";
    public string ModelId => _options.Model;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        try
        {
            var message = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _options.Model,
                MaxTokens = Math.Min(request.MaxOutputTokens, _options.MaxOutputTokens),
                System = request.System,
                Messages = [new() { Role = Role.User, Content = request.User }]
            }, ct);

            var text = string.Concat(message.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
            if (string.IsNullOrWhiteSpace(text))
                throw new ProviderException(
                    $"The Claude model returned no text (stop reason: {message.StopReason?.ToString() ?? "unknown"}).", false);

            return new LlmResponse(text, (int?)message.Usage?.InputTokens, (int?)message.Usage?.OutputTokens, message.StopReason?.ToString());
        }
        catch (AnthropicUnauthorizedException)
        {
            throw new ProviderException("Anthropic rejected the API key (HTTP 401). Check Ai:Llm:Anthropic:ApiKey or ANTHROPIC_API_KEY.", false);
        }
        catch (AnthropicForbiddenException)
        {
            throw new ProviderException("Anthropic denied access to this model or workspace (HTTP 403).", false);
        }
        catch (AnthropicNotFoundException)
        {
            throw new ProviderException($"Anthropic does not know the model '{_options.Model}' (HTTP 404). Check Ai:Llm:Anthropic:Model.", false);
        }
        catch (AnthropicRateLimitException)
        {
            throw new ProviderException("Anthropic rate limit reached (HTTP 429). Try again shortly.", true);
        }
        catch (Anthropic5xxException ex)
        {
            _logger.LogWarning("Anthropic server error {Status}", ex.StatusCode);
            throw new ProviderException("Anthropic is temporarily unavailable (server error).", true);
        }
        catch (Anthropic4xxException ex)
        {
            _logger.LogWarning("Anthropic rejected the request with {Status}", ex.StatusCode);
            throw new ProviderException($"Anthropic rejected the request (HTTP {(int)ex.StatusCode}).", false);
        }
        catch (AnthropicIOException)
        {
            throw new ProviderException("Anthropic could not be reached (network error or timeout).", true);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ProviderException($"Anthropic did not answer within {_options.TimeoutSeconds}s.", true);
        }
    }
}
