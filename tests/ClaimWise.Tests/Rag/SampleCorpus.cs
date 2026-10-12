using ClaimWise.Rag;
using ClaimWise.Rag.Chunking;
using ClaimWise.Rag.Embeddings;
using ClaimWise.Rag.Extraction;
using ClaimWise.Rag.Retrieval;

namespace ClaimWise.Tests.Rag;

/// <summary>Builds a real index from the fictional sample policies in samples/policies.</summary>
public static class SampleCorpus
{
    public static readonly HashingEmbeddingProvider Embedder = new();

    // (file, family, version)
    private static readonly (string File, string? Family, string Version)[] Files =
    [
        ("gold-plan-policy-2024.md", "gold-plan", "2024.1"),
        ("gold-plan-amendment-2025.md", "gold-plan", "2025.1"),
        ("basic-plan-policy-2024.md", "basic-plan", "2024.1"),
        ("claims-handling-guidelines.md", "internal", "3"),
    ];

    public static PolicyIndex Build(params string[] only)
    {
        var chunker = new PolicyChunker();
        var extractor = new PlainTextExtractor();
        var chunks = new List<IndexedChunk>();
        int docId = 0, chunkId = 0;

        foreach (var (file, family, version) in Files)
        {
            docId++;
            if (only.Length > 0 && !only.Contains(file)) continue;

            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "samples", "policies", file));
            var doc = extractor.Extract(stream);
            var title = doc.Pages[0].Text.Split('\n')[0].TrimStart('#', ' ');
            foreach (var chunk in chunker.Chunk(doc))
            {
                chunks.Add(new IndexedChunk(++chunkId, docId, title, version, family, chunk.Text, chunk.Heading,
                    chunk.ClauseRef, chunk.SectionPath, chunk.PageNumber, chunk.IndexText, Embedder.Embed(chunk.IndexText)));
            }
        }
        return new PolicyIndex(chunks, Embedder.ModelId);
    }

    public static HybridRetriever Retriever(RetrievalOptions? options = null) =>
        new(Embedder, new HeuristicReranker(), options);
}
