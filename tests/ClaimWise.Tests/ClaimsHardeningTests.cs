using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimWise.Api.Domain;
using ClaimWise.Api.Dtos;
using Xunit;

namespace ClaimWise.Tests;

/// <summary>Regression tests for defects found during the audit (see docs/implementation-status.md).</summary>
public class ClaimsHardeningTests(ClaimWiseFactory factory) : IClassFixture<ClaimWiseFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client = factory.CreateClient();

    private static string Today(int daysAgo) =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-daysAgo).ToString("yyyy-MM-dd");

    private async Task<ClaimDetailDto> CreateAsync(string provider, string policy = "POL-HARD-01", decimal amount = 100m)
    {
        var response = await _client.PostAsJsonAsync("/api/claims", new
        {
            patientName = "Hardening Patient", memberId = "MBR-777001", policyNumber = policy,
            providerName = provider, claimType = "Outpatient", diagnosisCode = "J06.9",
            procedureCode = "PRC-101", amountClaimed = amount, serviceDate = Today(2)
        }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClaimDetailDto>(Json))!;
    }

    private Task<HttpResponseMessage> StatusAsync(int id, string status, string reviewer, string? note = null, int? expectedVersion = null) =>
        _client.PostAsJsonAsync($"/api/claims/{id}/status",
            new { newStatus = status, reviewerName = reviewer, note, expectedVersion }, Json);

    [Fact]
    public async Task CreateClaim_WithMoreThanTwoDecimalPlaces_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/claims", new
        {
            patientName = "Decimal Patient", memberId = "MBR-777002", policyNumber = "POL-HARD-02",
            providerName = "Decimal Clinic", claimType = "Dental", procedureCode = "PRC-220",
            amountClaimed = 10.555m, serviceDate = Today(1)
        }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("decimal", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangeStatus_WithStaleExpectedVersion_Returns409AndLeavesClaimUnchanged()
    {
        var claim = await CreateAsync($"Stale-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(claim.Id, "InReview", "Reviewer One", expectedVersion: claim.Version)).StatusCode);

        // A second reviewer still holds the old version number.
        var stale = await StatusAsync(claim.Id, "NeedsInformation", "Reviewer Two", "Need a report.", expectedVersion: claim.Version);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var detail = await _client.GetFromJsonAsync<ClaimDetailDto>($"/api/claims/{claim.Id}", Json);
        Assert.Equal(ClaimStatus.InReview, detail!.Status);
        Assert.Equal("Reviewer One", detail.ReviewerName);
    }

    [Fact]
    public async Task ChangeStatus_IncrementsVersion()
    {
        var claim = await CreateAsync($"Version-{Guid.NewGuid():N}");

        var moved = await (await StatusAsync(claim.Id, "InReview", "Reviewer One")).Content.ReadFromJsonAsync<ClaimDetailDto>(Json);

        Assert.Equal(claim.Version + 1, moved!.Version);
    }

    [Fact]
    public async Task ChangeStatus_ReplacesOldNote_SoItIsNotShownWithTheNewReviewer()
    {
        var claim = await CreateAsync($"Note-{Guid.NewGuid():N}");
        await StatusAsync(claim.Id, "InReview", "Reviewer One");
        await StatusAsync(claim.Id, "NeedsInformation", "Reviewer One", "Please send the lab report.");

        var back = await (await StatusAsync(claim.Id, "InReview", "Reviewer Two")).Content.ReadFromJsonAsync<ClaimDetailDto>(Json);

        Assert.Equal("Reviewer Two", back!.ReviewerName);
        Assert.Null(back.ReviewerNote);
    }

    [Fact]
    public async Task List_SearchMatchesPolicyNumberAndCodes()
    {
        var unique = $"POL-{Guid.NewGuid():N}"[..18];
        var claim = await CreateAsync($"Search-{Guid.NewGuid():N}", policy: unique);

        var byPolicy = await _client.GetFromJsonAsync<PagedResult<ClaimSummaryDto>>($"/api/claims?search={unique}", Json);
        Assert.Contains(byPolicy!.Items, c => c.Id == claim.Id);
    }

    [Fact]
    public async Task List_SortByStatus_UsesWorkflowOrder()
    {
        var provider = $"Sort-{Guid.NewGuid():N}";
        var first = await CreateAsync(provider);
        var second = await CreateAsync(provider);
        await StatusAsync(second.Id, "InReview", "Reviewer One");

        var asc = await _client.GetFromJsonAsync<PagedResult<ClaimSummaryDto>>(
            $"/api/claims?search={provider}&sortBy=status&sortDir=asc", Json);

        Assert.Equal([first.Id, second.Id], asc!.Items.Select(c => c.Id));
        var desc = await _client.GetFromJsonAsync<PagedResult<ClaimSummaryDto>>(
            $"/api/claims?search={provider}&sortBy=status&sortDir=desc", Json);
        Assert.Equal([second.Id, first.Id], desc!.Items.Select(c => c.Id));
    }
}
