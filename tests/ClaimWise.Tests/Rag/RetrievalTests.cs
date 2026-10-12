using ClaimWise.Rag;
using ClaimWise.Rag.Embeddings;
using ClaimWise.Rag.Retrieval;
using Xunit;
using Xunit.Abstractions;

namespace ClaimWise.Tests.Rag;

public class TextAnalysisTests
{
    [Fact]
    public void Terms_KeepCodesWholeAndSplitHyphenatedWords()
    {
        var terms = TextAnalysis.Terms("Pre-authorization for PRC-101 and J06.9 treatments");
        Assert.Contains("prc-101", terms);
        Assert.Contains("j06.9", terms);
        Assert.Contains("authorization", terms);
        Assert.DoesNotContain("for", terms);
        Assert.Contains("treatment", terms);        // stemmed
    }
}

public class Bm25Tests
{
    [Fact]
    public void Search_RanksTheMostRelevantDocumentFirst()
    {
        var index = new Bm25Index([
            (1, "Dental waiting period is ninety days"),
            (2, "Outpatient annual limit applies per policy year"),
            (3, "Pharmacy prescriptions require a licensed pharmacy")]);

        var hits = index.Search("dental waiting", 3);

        Assert.Equal(1, hits[0].Id);
        Assert.Single(hits);                       // documents sharing no term are not returned
    }

    [Fact]
    public void Search_RareTermsOutweighCommonOnes_AndEmptyInputsAreSafe()
    {
        var index = new Bm25Index([(1, "plan plan plan benefit"), (2, "plan appendectomy")]);
        Assert.Equal(2, index.Search("plan appendectomy", 2)[0].Id);
        Assert.Empty(index.Search("", 5));
        Assert.Empty(new Bm25Index([]).Search("anything", 5));
    }
}

public class EmbeddingTests
{
    private readonly HashingEmbeddingProvider _e = new();

    [Fact]
    public void Embeddings_AreDeterministicAndUnitLength()
    {
        var a = _e.Embed("Annual outpatient limit");
        var b = _e.Embed("Annual outpatient limit");
        Assert.Equal(a, b);
        Assert.Equal(1.0, Math.Sqrt(a.Sum(x => (double)x * x)), 4);
        Assert.Equal(512, a.Length);
    }

    [Fact]
    public void Similarity_ToleratesWordFormsAndDomainSynonyms_ButNotUnrelatedText()
    {
        var q = _e.Embed("hospitalization admission");
        var related = _e.Embed("inpatient treatment requires hospital stay");
        var unrelated = _e.Embed("dental orthodontic braces waiting period");
        Assert.True(VectorMath.Cosine(q, related) > VectorMath.Cosine(q, unrelated) + 0.1);

        Assert.True(VectorMath.Cosine(_e.Embed("reimbursement of prescriptions"), _e.Embed("prescription reimbursed")) > 0.4);
    }

    [Fact]
    public void Bytes_RoundTrip()
    {
        var v = _e.Embed("round trip");
        Assert.Equal(v, VectorMath.FromBytes(VectorMath.ToBytes(v)));
    }
}

public class FusionTests
{
    [Fact]
    public void Rrf_RewardsAgreementBetweenRankers()
    {
        var fused = ReciprocalRankFusion.Fuse([[1, 2, 3], [3, 1, 4]]);
        var order = fused.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
        Assert.Equal([1, 3], order.Take(2));        // appear in both lists
        Assert.True(fused[2] < fused[1] && fused[4] < fused[3]);
    }
}

/// <summary>
/// A small labelled evaluation set over the fictional sample policies. Each query lists acceptable (document, clause)
/// answers. The numbers are what this implementation actually scores; they are a regression guard, not a benchmark.
/// </summary>
public class RetrievalEvaluationTests(ITestOutputHelper output)
{
    private record Case(string Query, params (string Doc, string Clause)[] Relevant);

    private static readonly Case[] Cases =
    [
        new("What is the annual outpatient limit?", ("Gold Plan Policy", "2.1"), ("Gold Plan Amendment", "2.1"), ("Basic Plan", "2.1")),
        new("dental waiting period", ("Gold Plan Policy", "5.2")),
        new("is orthodontic treatment such as braces covered", ("Gold Plan Policy", "5.4"), ("Basic Plan", "6.1")),
        new("pre-authorization for planned hospital admission", ("Gold Plan Policy", "3.2")),
        new("pharmacy prescription co-payment percentage", ("Gold Plan Policy", "4.2"), ("Basic Plan", "4.1")),
        new("cosmetic surgery exclusion", ("Gold Plan Policy", "6.1"), ("Basic Plan", "6.1")),
        new("procedure code PRC-310", ("Gold Plan Policy", "8.1"), ("Gold Plan Policy", "3.4")),
        new("how long do I have to submit a claim after treatment", ("Gold Plan Policy", "7.3"), ("Gold Plan Amendment", "3.1"), ("Basic Plan", "7.1")),
        new("documents required for an inpatient claim discharge summary", ("Gold Plan Policy", "7.2")),
        new("when does a second reviewer need to confirm a decision", ("Handling Guidelines", "3.2")),
        new("physiotherapy sessions per year", ("Gold Plan Policy", "2.4")),
        new("pre-existing conditions", ("Gold Plan Policy", "6.3"), ("Basic Plan", "6.2")),
        new("video doctor consultation telemedicine", ("Gold Plan Amendment", "4.1")),
        new("private hospital room difference", ("Gold Plan Policy", "3.5")),
        new("over the counter vitamins supplements", ("Gold Plan Policy", "4.3")),
        new("who decides whether a claim is approved", ("Handling Guidelines", "1.1")),
    ];

    private static bool IsRelevant(RetrievedPassage p, Case c) =>
        c.Relevant.Any(r => p.Chunk.ClauseRef == r.Clause && Matches(p.Chunk.DocumentTitle, r.Doc));

    private static bool Matches(string title, string key) => key switch
    {
        "Handling Guidelines" => title.Contains("Handling"),
        _ => title.Contains(key, StringComparison.OrdinalIgnoreCase)
    };

    private (double RecallAt3, double Mrr) Evaluate(RetrievalMode mode)
    {
        var index = SampleCorpus.Build();
        var retriever = SampleCorpus.Retriever(new RetrievalOptions { Mode = mode });
        int hits = 0;
        double rr = 0;
        foreach (var c in Cases)
        {
            var result = retriever.SearchAsync(index, c.Query, CancellationToken.None).GetAwaiter().GetResult();
            var rank = result.Passages.ToList().FindIndex(p => IsRelevant(p, c)) + 1;
            if (rank is > 0 and <= 3) hits++;
            if (rank > 0) rr += 1.0 / rank;
            if (rank is 0 or > 3) output.WriteLine($"[{mode}] MISS@3: '{c.Query}' first relevant rank={rank}; top={result.Passages.FirstOrDefault()?.Chunk.DocumentTitle} {result.Passages.FirstOrDefault()?.Chunk.ClauseRef}");
        }
        output.WriteLine($"{mode}: recall@3={(double)hits / Cases.Length:0.000} MRR={rr / Cases.Length:0.000}");
        return ((double)hits / Cases.Length, rr / Cases.Length);
    }

    [Fact]
    public void Hybrid_FindsTheRightClauseForMostQuestions()
    {
        var hybrid = Evaluate(RetrievalMode.Hybrid);
        Assert.True(hybrid.RecallAt3 >= 0.90, $"hybrid recall@3 = {hybrid.RecallAt3:0.000}");
        Assert.True(hybrid.Mrr >= 0.85, $"hybrid MRR = {hybrid.Mrr:0.000}");
    }

    [Fact]
    public void Hybrid_IsNotMateriallyWorseThanEitherSingleRanker()
    {
        // 16 hand-written queries are far too few to claim hybrid always wins (here it ties the vector ranker);
        // what matters is that fusion does not lose recall compared with either ranker alone.
        var hybrid = Evaluate(RetrievalMode.Hybrid);
        var keyword = Evaluate(RetrievalMode.KeywordOnly);
        var vector = Evaluate(RetrievalMode.VectorOnly);
        Assert.True(hybrid.RecallAt3 >= keyword.RecallAt3 - 1e-9, "hybrid recall is below keyword-only");
        Assert.True(hybrid.RecallAt3 >= vector.RecallAt3 - 0.07, "hybrid recall is more than one query below vector-only");
        Assert.True(hybrid.Mrr >= keyword.Mrr - 0.02, "hybrid MRR is below keyword-only");
    }

    [Fact]
    public async Task Results_ExposeScoresFromEveryStage()
    {
        var index = SampleCorpus.Build();
        var result = await SampleCorpus.Retriever().SearchAsync(index, "dental waiting period", CancellationToken.None);

        var top = result.Passages[0];
        Assert.Equal(1, top.Rank);
        Assert.Equal("5.2", top.Chunk.ClauseRef);
        Assert.NotNull(top.KeywordScore);
        Assert.NotNull(top.VectorScore);
        Assert.InRange(top.RerankScore, 0, 1);
        Assert.True(result.Passages.Zip(result.Passages.Skip(1)).All(p => p.First.RerankScore >= p.Second.RerankScore));
    }

    [Fact]
    public async Task Search_RefusesToMixEmbeddingModels()
    {
        var index = new PolicyIndex(SampleCorpus.Build().Chunks, "some-other-model");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SampleCorpus.Retriever().SearchAsync(index, "dental", CancellationToken.None));
    }

    [Fact]
    public async Task EmptyQueryOrIndex_ReturnsNoPassages()
    {
        var empty = new PolicyIndex([], SampleCorpus.Embedder.ModelId);
        Assert.Empty((await SampleCorpus.Retriever().SearchAsync(empty, "dental", CancellationToken.None)).Passages);
        Assert.Empty((await SampleCorpus.Retriever().SearchAsync(SampleCorpus.Build(), "  ", CancellationToken.None)).Passages);
    }
}
