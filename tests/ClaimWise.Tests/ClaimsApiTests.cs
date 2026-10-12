using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimWise.Api.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClaimWise.Tests;

/// <summary>
/// Starts the real API in memory, pointed at a throw-away SQLite file,
/// so tests exercise validation, controllers, services and the database together.
/// </summary>
public class ClaimWiseFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"claimwise-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
                ["Seed:Enabled"] = "false"
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        SqliteConnection.ClearAllPools(); // release the file lock so it can be deleted
        try { File.Delete(_dbPath); } catch (IOException) { /* temp file; the OS will clean it up */ }
    }
}

public class ClaimsApiTests(ClaimWiseFactory factory) : IClassFixture<ClaimWiseFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client = factory.CreateClient();

    // ---------- helpers ----------

    private static object ValidClaim(string provider, string claimType = "Outpatient",
        string? diagnosis = "J06.9", string? procedure = "PRC-101") => new
        {
            patientName = "Test Patient",
            memberId = "MBR-900001",
            policyNumber = "POL-TEST-01",
            providerName = provider,
            claimType,
            diagnosisCode = diagnosis,
            procedureCode = procedure,
            amountClaimed = 1500.50m,
            serviceDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3).ToString("yyyy-MM-dd")
        };

    private async Task<ClaimDetailDto> CreateClaimAsync(string provider, string claimType = "Outpatient",
        string? diagnosis = "J06.9", string? procedure = "PRC-101")
    {
        var response = await _client.PostAsJsonAsync("/api/claims", ValidClaim(provider, claimType, diagnosis, procedure), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClaimDetailDto>(Json))!;
    }

    private Task<HttpResponseMessage> AddDocumentAsync(int claimId, string documentType) =>
        _client.PostAsJsonAsync($"/api/claims/{claimId}/documents",
            new { documentType, fileName = $"{documentType}.pdf" }, Json);

    private Task<HttpResponseMessage> ChangeStatusAsync(int claimId, string newStatus, string? note = null) =>
        _client.PostAsJsonAsync($"/api/claims/{claimId}/status",
            new { newStatus, reviewerName = "Test Reviewer", note }, Json);

    private static string UniqueProvider() => $"Provider-{Guid.NewGuid():N}";

    // ---------- creating claims ----------

    [Fact]
    public async Task CreateClaim_WithValidData_Returns201AndGeneratedClaimNumber()
    {
        var claim = await CreateClaimAsync(UniqueProvider());

        Assert.StartsWith("CW-", claim.ClaimNumber);
        Assert.Equal(Api.Domain.ClaimStatus.Submitted, claim.Status);
        Assert.False(claim.Validation.IsComplete);                 // no documents yet
        Assert.Equal(3, claim.Validation.MissingDocuments.Count);
    }

    [Fact]
    public async Task CreateClaim_WithInvalidData_Returns400()
    {
        var bad = new { patientName = "", memberId = "x", policyNumber = "POL-TEST-01", providerName = "P",
                        claimType = "Outpatient", amountClaimed = 0, serviceDate = "2024-01-01" };

        var response = await _client.PostAsJsonAsync("/api/claims", bad, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PatientName", body);
        Assert.Contains("AmountClaimed", body);
    }

    [Fact]
    public async Task CreateClaim_WithFutureServiceDate_Returns400()
    {
        var future = new
        {
            patientName = "Test Patient", memberId = "MBR-900001", policyNumber = "POL-TEST-01",
            providerName = UniqueProvider(), claimType = "Dental", amountClaimed = 100,
            serviceDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5).ToString("yyyy-MM-dd")
        };

        var response = await _client.PostAsJsonAsync("/api/claims", future, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ServiceDate", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetClaim_Unknown_Returns404()
    {
        var response = await _client.GetAsync("/api/claims/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- listing ----------

    [Fact]
    public async Task ListClaims_SupportsSearchAndPagination()
    {
        var provider = UniqueProvider();
        for (int i = 0; i < 3; i++) await CreateClaimAsync(provider);

        var page1 = await _client.GetFromJsonAsync<PagedResult<ClaimSummaryDto>>(
            $"/api/claims?search={provider}&pageSize=2&page=1", Json);
        var page2 = await _client.GetFromJsonAsync<PagedResult<ClaimSummaryDto>>(
            $"/api/claims?search={provider}&pageSize=2&page=2", Json);

        Assert.Equal(3, page1!.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2!.Items);
    }

    [Fact]
    public async Task ListClaims_CanSortByAmountAndFilterByType()
    {
        var provider = UniqueProvider();
        await CreateClaimAsync(provider, claimType: "Outpatient");
        await CreateClaimAsync(provider, claimType: "Dental", diagnosis: null);

        var dentalOnly = await _client.GetFromJsonAsync<PagedResult<ClaimSummaryDto>>(
            $"/api/claims?search={provider}&claimType=Dental&sortBy=amount&sortDir=asc", Json);

        Assert.Single(dentalOnly!.Items);
        Assert.Equal(Api.Domain.ClaimType.Dental, dentalOnly.Items[0].ClaimType);
    }

    [Fact]
    public async Task ListClaims_WithInvalidPageSize_Returns400()
    {
        var response = await _client.GetAsync("/api/claims?pageSize=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- documents and review workflow ----------

    [Fact]
    public async Task AddDocument_Twice_Returns409()
    {
        var claim = await CreateClaimAsync(UniqueProvider());

        var first = await AddDocumentAsync(claim.Id, "Invoice");
        var second = await AddDocumentAsync(claim.Id, "Invoice");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task ChangeStatus_WithInvalidTransition_Returns409()
    {
        var claim = await CreateClaimAsync(UniqueProvider());

        // Submitted -> Approved skips human review and must be refused.
        var response = await ChangeStatusAsync(claim.Id, "Approved", "Looks fine");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ChangeStatus_ToRejectedWithoutNote_Returns400()
    {
        var claim = await CreateClaimAsync(UniqueProvider());
        await ChangeStatusAsync(claim.Id, "InReview");

        var response = await ChangeStatusAsync(claim.Id, "Rejected", note: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Approve_WithMissingDocuments_Returns409()
    {
        var claim = await CreateClaimAsync(UniqueProvider());
        await ChangeStatusAsync(claim.Id, "InReview");

        var response = await ChangeStatusAsync(claim.Id, "Approved", "Approving for test");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("incomplete", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FullReviewFlow_ApprovesCompleteClaim_AndRecordsAuditTrail()
    {
        var claim = await CreateClaimAsync(UniqueProvider());

        Assert.Equal(HttpStatusCode.OK, (await ChangeStatusAsync(claim.Id, "InReview")).StatusCode);
        foreach (var doc in new[] { "ClaimForm", "Invoice", "MedicalReport" })
            Assert.Equal(HttpStatusCode.Created, (await AddDocumentAsync(claim.Id, doc)).StatusCode);

        var approved = await ChangeStatusAsync(claim.Id, "Approved", "All documents verified (demo).");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var detail = await _client.GetFromJsonAsync<ClaimDetailDto>($"/api/claims/{claim.Id}", Json);
        Assert.Equal(Api.Domain.ClaimStatus.Approved, detail!.Status);
        Assert.True(detail.Validation.IsComplete);
        Assert.Empty(detail.AllowedNextStatuses);                  // final status

        var audit = await _client.GetFromJsonAsync<List<AuditEntryDto>>($"/api/claims/{claim.Id}/audit", Json);
        // 1 creation + 1 move to InReview + 3 documents + 1 approval
        Assert.Equal(6, audit!.Count);
        Assert.Equal("ClaimCreated", audit[0].Action);
        Assert.Equal("StatusChanged", audit[^1].Action);
        Assert.Equal("Test Reviewer", audit[^1].Actor);
    }

    [Fact]
    public async Task AddDocument_ToFinalClaim_Returns409()
    {
        var claim = await CreateClaimAsync(UniqueProvider());
        await ChangeStatusAsync(claim.Id, "InReview");
        await ChangeStatusAsync(claim.Id, "Rejected", "Rejected for test purposes.");

        var response = await AddDocumentAsync(claim.Id, "Invoice");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------- dashboard and health ----------

    [Fact]
    public async Task Summary_ReturnsCountsForEveryStatus()
    {
        await CreateClaimAsync(UniqueProvider());

        var summary = await _client.GetFromJsonAsync<DashboardDto>("/api/claims/summary", Json);

        Assert.True(summary!.TotalClaims >= 1);
        Assert.Equal(5, summary.ByStatus.Count);
        Assert.True(summary.TotalAmountClaimed > 0);
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
