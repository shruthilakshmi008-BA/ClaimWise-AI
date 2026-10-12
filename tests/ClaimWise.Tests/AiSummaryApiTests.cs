using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimWise.Api.Domain;
using ClaimWise.Api.Dtos;
using ClaimWise.Rag.Embeddings;
using ClaimWise.Rag.Grounding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClaimWise.Tests;

/// <summary>A factory whose summary generator is scripted by the test (stands in for an LLM provider).</summary>
public sealed class ScriptedGeneratorFactory : ClaimWiseFactory
{
    public Func<SummaryRequest, DraftSummary> Script { get; set; } = _ => new DraftSummary(true, "not scripted", []);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.AddSingleton<ISummaryGenerator>(new Generator(this)));
    }

    private sealed class Generator(ScriptedGeneratorFactory owner) : ISummaryGenerator
    {
        public string Name => "llm:scripted";
        public string? ModelId => "scripted-1";
        public Task<DraftSummary> GenerateAsync(SummaryRequest request, CancellationToken ct) => Task.FromResult(owner.Script(request));
    }
}

public abstract class ClaimWorkflowTestBase(ClaimWiseFactory factory)
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    protected readonly HttpClient Client = factory.CreateClient();

    protected async Task UploadSamplesAsync(params string[] files)
    {
        foreach (var file in files)
        {
            var bytes = await File.ReadAllBytesAsync(PolicyApiTests.SamplePath(file));
            var family = file.StartsWith("gold") ? "gold-plan" : file.StartsWith("basic") ? "basic-plan" : "internal";
            var response = await Client.PostAsync("/api/policies", PolicyApiTests.Form(bytes, file, family, "1"));
            // Tests in a class share one database; the duplicate guard answers 409 when a sample is already there.
            Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, $"{file}: {response.StatusCode}");
        }
    }

    protected async Task<ClaimDetailDto> CreateClaimAsync(string type = "Outpatient")
    {
        var response = await Client.PostAsJsonAsync("/api/claims", new
        {
            patientName = "Workflow Patient", memberId = "MBR-555001", policyNumber = "POL-GOLD-01",
            providerName = $"Provider-{Guid.NewGuid():N}", claimType = type, diagnosisCode = "J06.9", procedureCode = "PRC-101",
            amountClaimed = 1850m, serviceDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3).ToString("yyyy-MM-dd")
        }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClaimDetailDto>(Json))!;
    }

    protected async Task CompleteDocumentsAsync(int claimId)
    {
        foreach (var doc in new[] { "ClaimForm", "Invoice", "MedicalReport" })
            Assert.Equal(HttpStatusCode.Created,
                (await Client.PostAsJsonAsync($"/api/claims/{claimId}/documents", new { documentType = doc, fileName = $"{doc}.pdf" }, Json)).StatusCode);
    }

    protected Task<HttpResponseMessage> GenerateAsync(int claimId, string? question = null, int[]? documentIds = null) =>
        Client.PostAsJsonAsync($"/api/claims/{claimId}/summaries", new { question, requestedBy = "Rita Reviewer", documentIds }, Json);

    protected async Task<AiSummaryDto> GenerateOkAsync(int claimId, string? question = null, int[]? documentIds = null)
    {
        var response = await GenerateAsync(claimId, question, documentIds);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AiSummaryDto>(Json))!;
    }

    protected Task<HttpResponseMessage> StatusAsync(int claimId, string status, string? note = null) =>
        Client.PostAsJsonAsync($"/api/claims/{claimId}/status", new { newStatus = status, reviewerName = "Rita Reviewer", note }, Json);

    protected Task<HttpResponseMessage> ReviewAsync(int claimId, int summaryId, string decision, string? note = null) =>
        Client.PostAsJsonAsync($"/api/claims/{claimId}/summaries/{summaryId}/review", new { decision, reviewerName = "Rita Reviewer", note }, Json);

    protected async Task<List<AuditEntryDto>> AuditAsync(int claimId) =>
        (await Client.GetFromJsonAsync<List<AuditEntryDto>>($"/api/claims/{claimId}/audit", Json))!;
}

public class AiSummaryWorkflowTests(ClaimWiseFactory factory) : ClaimWorkflowTestBase(factory), IClassFixture<ClaimWiseFactory>
{
    [Fact]
    public async Task EndToEnd_CreateClaim_RetrieveEvidence_CitedSummary_HumanReview_Decision()
    {
        await UploadSamplesAsync("gold-plan-policy-2024.md");
        var claim = await CreateClaimAsync();
        await CompleteDocumentsAsync(claim.Id);

        // 1. Generate an evidence-grounded summary.
        var summary = await GenerateOkAsync(claim.Id, "annual outpatient limit and co-payment");

        Assert.Equal(AiSummaryStatus.Answered, summary.Status);
        Assert.False(summary.UsedLanguageModel);                       // extractive: no model involved
        Assert.Equal(SummaryReviewState.PendingReview, summary.Review.State);
        Assert.Contains("not a coverage decision", summary.Notice);
        Assert.NotEmpty(summary.Statements);

        // 2. Every citation points at a real stored passage and quotes it verbatim.
        foreach (var statement in summary.Statements)
        {
            Assert.NotEmpty(statement.Citations);
            foreach (var citation in statement.Citations)
            {
                var passage = Assert.Single(summary.Passages, p => p.Label == citation.Passage);
                Assert.Contains(TextAnalysisNormalize(citation.Quote), TextAnalysisNormalize(passage.Text));
                Assert.False(string.IsNullOrEmpty(passage.ClauseRef));
                Assert.True(passage.PageNumber >= 1);
            }
        }
        Assert.Equal("Sufficient", summary.Evidence.Verdict);
        Assert.Contains(summary.Passages, p => p.ClauseRef == "2.1" && p.Text.Contains("5,000"));

        // 3. The claim cannot be decided while the AI summary is unreviewed.
        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(claim.Id, "InReview")).StatusCode);
        var blocked = await StatusAsync(claim.Id, "Approved", "Looks fine.");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("has not been reviewed", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await StatusAsync(claim.Id, "Rejected", "Nope.")).StatusCode);

        // 4. A named human reviews the summary, then records the decision.
        var reviewed = await (await ReviewAsync(claim.Id, summary.Id, "Accepted", "Cited clauses checked against the policy.")).Content.ReadFromJsonAsync<AiSummaryDto>(Json);
        Assert.Equal(SummaryReviewState.Accepted, reviewed!.Review.State);
        Assert.Equal("Rita Reviewer", reviewed.Review.ReviewedBy);

        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(claim.Id, "Approved", "Within the annual limit; documents complete.")).StatusCode);

        // 5. The audit trail records generation, the human review and the AI-aware decision, in order.
        var audit = await AuditAsync(claim.Id);
        var actions = audit.Select(a => a.Action).ToList();
        Assert.True(actions.IndexOf("AiSummaryGenerated") < actions.IndexOf("AiSummaryReviewed"));
        Assert.True(actions.IndexOf("AiSummaryReviewed") < actions.LastIndexOf("StatusChanged"));
        Assert.Contains("AI summaries reviewed by a person", audit[^1].Details);
        Assert.Equal("Rita Reviewer", audit.Single(a => a.Action == "AiSummaryReviewed").Actor);

        // 6. The summary stays retrievable with its snapshot, even after the policy is deactivated.
        var list = await Client.GetFromJsonAsync<List<AiSummaryDto>>($"/api/claims/{claim.Id}/summaries", Json);
        Assert.Single(list!);
    }

    private static string TextAnalysisNormalize(string s) => ClaimWise.Rag.TextAnalysis.NormalizeForComparison(s);

    [Fact]
    public async Task RejectedSummary_AlsoUnblocksTheDecision_AndIsAudited()
    {
        await UploadSamplesAsync("gold-plan-policy-2024.md");
        var claim = await CreateClaimAsync();
        var summary = await GenerateOkAsync(claim.Id, "dental waiting period");
        await StatusAsync(claim.Id, "InReview");

        Assert.Equal(HttpStatusCode.BadRequest, (await ReviewAsync(claim.Id, summary.Id, "Rejected")).StatusCode);          // note required
        Assert.Equal(HttpStatusCode.BadRequest, (await ReviewAsync(claim.Id, summary.Id, "PendingReview")).StatusCode);     // invalid verdict
        Assert.Equal(HttpStatusCode.OK, (await ReviewAsync(claim.Id, summary.Id, "Rejected", "Not relevant to this claim.")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(claim.Id, "Rejected", "Claim rejected by reviewer for test.")).StatusCode);
        Assert.Contains("#" + summary.Id + " Rejected", (await AuditAsync(claim.Id))[^1].Details);
    }

    [Fact]
    public async Task ASummaryCanOnlyBeReviewedOnce_AndUnknownIdsReturn404()
    {
        await UploadSamplesAsync("gold-plan-policy-2024.md");
        var claim = await CreateClaimAsync();
        var summary = await GenerateOkAsync(claim.Id, "dental waiting period");

        Assert.Equal(HttpStatusCode.OK, (await ReviewAsync(claim.Id, summary.Id, "Accepted")).StatusCode);
        var again = await ReviewAsync(claim.Id, summary.Id, "Rejected", "changed my mind");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already accepted", await again.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await ReviewAsync(claim.Id, 999999, "Accepted")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GenerateAsync(999999)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/claims/{claim.Id}/summaries/999999")).StatusCode);
    }

    [Fact]
    public async Task OffTopicQuestion_Abstains_NoFindings_AndDoesNotBlockTheClaim()
    {
        await UploadSamplesAsync("gold-plan-policy-2024.md");
        var claim = await CreateClaimAsync();
        await CompleteDocumentsAsync(claim.Id);

        var summary = await GenerateOkAsync(claim.Id, "quantum computing satellite launch insurance");

        Assert.Equal(AiSummaryStatus.Abstained, summary.Status);
        Assert.Empty(summary.Statements);
        Assert.Equal(SummaryReviewState.NotRequired, summary.Review.State);
        Assert.Contains("Insufficient policy evidence", summary.AbstainReason);
        Assert.Equal("Insufficient", summary.Evidence.Verdict);

        await StatusAsync(claim.Id, "InReview");
        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(claim.Id, "Approved", "Documents complete; no AI findings relied on.")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ReviewAsync(claim.Id, summary.Id, "Accepted")).StatusCode);   // nothing to review
    }

    [Fact]
    public async Task ConflictingPolicyVersions_AreSurfaced_NotResolved()
    {
        await UploadSamplesAsync("gold-plan-policy-2024.md", "gold-plan-amendment-2025.md");
        var claim = await CreateClaimAsync();

        var summary = await GenerateOkAsync(claim.Id, "annual outpatient limit per policy year");

        Assert.Equal(AiSummaryStatus.Answered, summary.Status);
        var conflict = Assert.Single(summary.Conflicts, c => c.Description.Contains("5,000") && c.Description.Contains("6,000"));
        Assert.Equal("ValueConflict", conflict.Kind);
        var notice = Assert.Single(summary.Statements, s => s.Origin == "System" && s.Text.Contains("6,000"));
        Assert.Equal(2, notice.Citations.Count);
        Assert.Contains("must resolve", notice.Text);
    }

    [Fact]
    public async Task EvidenceFromDifferentPlans_IsFlaggedForTheReviewer_AndDocumentScopingFixesIt()
    {
        await UploadSamplesAsync("gold-plan-policy-2024.md", "basic-plan-policy-2024.md");
        var claim = await CreateClaimAsync();

        var mixed = await GenerateOkAsync(claim.Id, "annual outpatient limit per policy year");
        Assert.Contains(mixed.Warnings, w => w.Contains("different policies"));
        Assert.Empty(mixed.Conflicts);                       // different products, not a contradiction

        var docs = await Client.GetFromJsonAsync<List<PolicyDocumentDto>>("/api/policies", Json);
        var basic = docs!.First(d => d.PolicyFamily == "basic-plan");
        var scoped = await GenerateOkAsync(claim.Id, "annual outpatient limit per policy year", [basic.Id]);
        Assert.DoesNotContain(scoped.Warnings, w => w.Contains("different policies"));
        Assert.All(scoped.Passages, p => Assert.Equal(basic.Id, p.DocumentId));
    }

    [Fact]
    public async Task FinalClaims_CannotRequestNewSummaries()
    {
        var claim = await CreateClaimAsync();
        await StatusAsync(claim.Id, "InReview");
        await StatusAsync(claim.Id, "Rejected", "Rejected for test purposes.");

        Assert.Equal(HttpStatusCode.Conflict, (await GenerateAsync(claim.Id)).StatusCode);
    }

    [Fact]
    public async Task Validation_RequiresARequesterAndBoundsTheQuestion()
    {
        var claim = await CreateClaimAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Client.PostAsJsonAsync($"/api/claims/{claim.Id}/summaries", new { question = "q" }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Client.PostAsJsonAsync($"/api/claims/{claim.Id}/summaries", new { question = new string('x', 501), requestedBy = "Rita" }, Json)).StatusCode);
    }
}

public class AiSummaryWithNoPoliciesTests(ClaimWiseFactory factory) : ClaimWorkflowTestBase(factory), IClassFixture<ClaimWiseFactory>
{
    [Fact]
    public async Task WithoutAnyPolicyDocuments_TheSystemAbstains()
    {
        var claim = await CreateClaimAsync();

        var summary = await GenerateOkAsync(claim.Id);

        Assert.Equal(AiSummaryStatus.Abstained, summary.Status);
        Assert.Empty(summary.Statements);
        Assert.Contains("No policy passages", summary.AbstainReason);
    }
}

public class LlmPathTests(ScriptedGeneratorFactory factory) : ClaimWorkflowTestBase(factory), IClassFixture<ScriptedGeneratorFactory>
{
    private readonly ScriptedGeneratorFactory _factory = factory;

    [Fact]
    public async Task FabricatedCitationsFromAModel_AreDropped_AndNothingIsPresentedAsAFinding()
    {
        await UploadSamplesAsync("gold-plan-policy-2024.md");
        _factory.Script = _ => new DraftSummary(false, null,
        [
            new DraftStatement("Dental treatment has no waiting period.", [new DraftCitation("P1", "there is absolutely no waiting period for any treatment")]),
            new DraftStatement("Cover is unlimited.", [new DraftCitation("P7", "cover is unlimited for every member")])
        ]);
        var claim = await CreateClaimAsync();

        var summary = await GenerateOkAsync(claim.Id, "dental waiting period");

        Assert.Equal(AiSummaryStatus.Abstained, summary.Status);
        Assert.True(summary.UsedLanguageModel);
        Assert.Empty(summary.Statements);
        Assert.Equal(2, summary.RejectedStatements.Count);
        Assert.Equal(SummaryReviewState.NotRequired, summary.Review.State);
    }

    [Fact]
    public async Task ValidModelStatements_AreKept_AndInventedOnesRemoved()
    {
        await UploadSamplesAsync("gold-plan-policy-2024.md");
        _factory.Script = req =>
        {
            var dental = req.Passages.First(p => p.Chunk.ClauseRef == "5.2");
            return new DraftSummary(false, null,
            [
                new DraftStatement("Dental benefits have a 90 day waiting period from the membership start date.",
                    [new DraftCitation(dental.Label, "A waiting period of 90 days from the membership start date applies to dental benefits")]),
                new DraftStatement("The waiting period is 30 days.", [new DraftCitation(dental.Label, "a waiting period of 30 days applies to dental benefits")])
            ]);
        };
        var claim = await CreateClaimAsync("Dental");

        var summary = await GenerateOkAsync(claim.Id, "dental waiting period");

        Assert.Equal(AiSummaryStatus.Answered, summary.Status);
        var kept = Assert.Single(summary.Statements);
        Assert.Contains("90 day", kept.Text);
        Assert.Single(summary.RejectedStatements);
        Assert.Contains(summary.Warnings, w => w.Contains("removed"));
        Assert.Equal("scripted-1", summary.Model);
    }

    [Fact]
    public async Task ModelThatRecommendsADecision_IsRefused()
    {
        await UploadSamplesAsync("gold-plan-policy-2024.md");
        _factory.Script = req =>
        {
            var p = req.Passages.First(x => x.Chunk.ClauseRef == "5.2");
            return new DraftSummary(false, null,
                [new DraftStatement("The claim should be approved because the dental waiting period has elapsed.",
                    [new DraftCitation(p.Label, "A waiting period of 90 days from the membership start date applies to dental benefits")])]);
        };
        var claim = await CreateClaimAsync("Dental");

        var summary = await GenerateOkAsync(claim.Id, "dental waiting period");

        Assert.Equal(AiSummaryStatus.Abstained, summary.Status);
        Assert.Contains("decision", Assert.Single(summary.RejectedStatements).Reason);
    }

    [Fact]
    public async Task ProviderFailure_IsRecordedAsFailed_WithNoFabricatedContent()
    {
        await UploadSamplesAsync("gold-plan-policy-2024.md");
        _factory.Script = _ => throw new ProviderException("The language model provider did not answer within 60s.", true);
        var claim = await CreateClaimAsync();

        var summary = await GenerateOkAsync(claim.Id, "dental waiting period");

        Assert.Equal(AiSummaryStatus.Failed, summary.Status);
        Assert.Contains("did not answer", summary.FailureReason);
        Assert.Empty(summary.Statements);
        Assert.Equal(SummaryReviewState.NotRequired, summary.Review.State);
        Assert.Contains((await AuditAsync(claim.Id)), a => a.Action == "AiSummaryGenerated" && a.Details.StartsWith("Failed"));
    }
}
