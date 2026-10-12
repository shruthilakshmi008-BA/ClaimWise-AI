using ClaimWise.Rag.Chunking;
using ClaimWise.Rag.Embeddings;
using ClaimWise.Rag.Extraction;
using ClaimWise.Rag.Grounding;
using ClaimWise.Rag.Llm;
using ClaimWise.Rag.Retrieval;
using Microsoft.Extensions.Options;

namespace ClaimWise.Api.Ai;

public static class AiServiceCollectionExtensions
{
    /// <summary>Registers extraction, chunking, retrieval and summarisation, choosing providers from the "Ai" configuration.</summary>
    public static IServiceCollection AddClaimWiseAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        var ai = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();

        // One pooled-connection lifetime keeps DNS changes visible to long-lived singletons.
        foreach (var name in new[] { "embeddings", "llm" })
            services.AddHttpClient(name, c => c.Timeout = Timeout.InfiniteTimeSpan) // per-attempt timeouts are enforced by the providers
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });

        services.AddSingleton(TextExtractorRegistry.CreateDefault());
        services.AddSingleton(new PolicyChunker());
        services.AddSingleton<IReranker, HeuristicReranker>();
        services.AddSingleton(ai.Retrieval);
        services.AddSingleton<HybridRetriever>();
        services.AddSingleton<PolicyIndexCache>();

        services.AddSingleton<IEmbeddingProvider>(sp => CreateEmbeddings(sp, ai.Embeddings));
        services.AddSingleton<ISummaryGenerator>(sp => CreateGenerator(sp, ai.Llm));

        services.AddSingleton(new EvidenceAssessor(ai.Evidence));
        services.AddSingleton<ConflictDetector>();
        services.AddSingleton(new CitationValidator(ai.Validation));
        services.AddSingleton<GroundedSummaryPipeline>(sp => new GroundedSummaryPipeline(
            sp.GetRequiredService<ISummaryGenerator>(), sp.GetRequiredService<EvidenceAssessor>(),
            sp.GetRequiredService<ConflictDetector>(), sp.GetRequiredService<CitationValidator>(),
            logger: sp.GetRequiredService<ILogger<GroundedSummaryPipeline>>()));

        return services;
    }

    private static IEmbeddingProvider CreateEmbeddings(IServiceProvider sp, EmbeddingsSection section)
    {
        switch (section.Provider.ToLowerInvariant())
        {
            case "local":
                return new HashingEmbeddingProvider(section.LocalDimensions);
            case "openaicompatible":
                Require(section.OpenAiCompatible, "Ai:Embeddings:OpenAiCompatible");
                return new OpenAiCompatibleEmbeddingProvider(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient("embeddings"), section.OpenAiCompatible,
                    sp.GetRequiredService<ILogger<OpenAiCompatibleEmbeddingProvider>>());
            default:
                throw new InvalidOperationException($"Unsupported Ai:Embeddings:Provider '{section.Provider}'. Use 'Local' or 'OpenAiCompatible'.");
        }
    }

    private static ISummaryGenerator CreateGenerator(IServiceProvider sp, LlmSection section)
    {
        switch (section.Provider.ToLowerInvariant())
        {
            case "extractive":
                return new ExtractiveSummaryGenerator();
            case "openaicompatible":
                Require(section.OpenAiCompatible, "Ai:Llm:OpenAiCompatible");
                return new LlmSummaryGenerator(new OpenAiCompatibleLlmProvider(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient("llm"), section.OpenAiCompatible,
                    sp.GetRequiredService<ILogger<OpenAiCompatibleLlmProvider>>()));
            case "anthropic":
                return new LlmSummaryGenerator(new AnthropicLlmProvider(section.Anthropic, sp.GetRequiredService<ILogger<AnthropicLlmProvider>>()));
            default:
                throw new InvalidOperationException($"Unsupported Ai:Llm:Provider '{section.Provider}'. Use 'Extractive', 'OpenAiCompatible' or 'Anthropic'.");
        }
    }

    private static void Require(OpenAiCompatibleOptions o, string path)
    {
        if (string.IsNullOrWhiteSpace(o.BaseUrl)) throw new InvalidOperationException($"{path}:BaseUrl is required.");
        if (string.IsNullOrWhiteSpace(o.Model)) throw new InvalidOperationException($"{path}:Model is required.");
    }
}
