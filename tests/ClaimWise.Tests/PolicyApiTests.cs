using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimWise.Api.Dtos;
using Xunit;

namespace ClaimWise.Tests;

public class PolicyApiTests(ClaimWiseFactory factory) : IClassFixture<ClaimWiseFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly HttpClient _client = factory.CreateClient();

    internal static string SamplePath(string file) => Path.Combine(AppContext.BaseDirectory, "samples", "policies", file);

    internal static MultipartFormDataContent Form(byte[] bytes, string fileName, string? family = null, string? version = null, string? title = null, string uploadedBy = "Policy Admin")
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "File", fileName);
        form.Add(new StringContent(uploadedBy), "UploadedBy");
        if (family is not null) form.Add(new StringContent(family), "PolicyFamily");
        if (version is not null) form.Add(new StringContent(version), "Version");
        if (title is not null) form.Add(new StringContent(title), "Title");
        return form;
    }

    private async Task<PolicyDocumentDto> UploadSampleAsync(string file, string family, string version, string? unique = null)
    {
        // Appending a unique comment changes the hash so the same sample can be uploaded by several tests.
        var text = await File.ReadAllTextAsync(SamplePath(file)) + (unique is null ? "" : $"\n\n<!-- {unique} -->");
        var response = await _client.PostAsync("/api/policies", Form(Encoding.UTF8.GetBytes(text), file, family, version));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PolicyDocumentDto>(Json))!;
    }

    [Fact]
    public async Task Upload_ExtractsChunksAndKeepsSourceMetadata()
    {
        var doc = await UploadSampleAsync("gold-plan-policy-2024.md", "Gold Plan", "2024.1", Guid.NewGuid().ToString());

        Assert.Contains("Gold Plan Policy", doc.Title);
        Assert.Equal("gold-plan", doc.PolicyFamily);            // normalised
        Assert.True(doc.ChunkCount >= 27);
        Assert.Equal(64, doc.Sha256.Length);

        var chunks = await _client.GetFromJsonAsync<List<PolicyChunkDto>>($"/api/policies/{doc.Id}/chunks", Json);
        Assert.Equal(doc.ChunkCount, chunks!.Count);
        var limit = chunks.Single(c => c.ClauseRef == "2.1");
        Assert.Contains("5,000", limit.Text);
        Assert.Equal("2 Outpatient benefits", limit.Heading);
        Assert.Equal(1, limit.PageNumber);
    }

    [Fact]
    public async Task Upload_Pdf_KeepsPageNumbers()
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var unique = Guid.NewGuid().ToString("N")[..8];
        builder.AddPage(595, 842).AddText($"Page one introduction {unique}.", 12, new UglyToad.PdfPig.Core.PdfPoint(40, 780), font);
        builder.AddPage(595, 842).AddText($"Hospital room charges are covered up to a semi-private room {unique}.", 12, new UglyToad.PdfPig.Core.PdfPoint(40, 780), font);

        var response = await _client.PostAsync("/api/policies", Form(builder.Build(), "scan-free.pdf", title: "PDF policy"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var doc = (await response.Content.ReadFromJsonAsync<PolicyDocumentDto>(Json))!;
        Assert.Equal(2, doc.PageCount);
        var chunks = await _client.GetFromJsonAsync<List<PolicyChunkDto>>($"/api/policies/{doc.Id}/chunks", Json);
        Assert.Contains(chunks!, c => c.PageNumber == 2 && c.Text.Contains("semi-private"));
    }

    [Fact]
    public async Task Upload_SameFileTwice_Returns409()
    {
        var bytes = Encoding.UTF8.GetBytes($"# Dup policy\n\n1.1 Dup text {Guid.NewGuid()} is covered in full.");
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsync("/api/policies", Form(bytes, "dup.md"))).StatusCode);

        var second = await _client.PostAsync("/api/policies", Form(bytes, "dup-renamed.md"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("already uploaded", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_RejectsUnsupportedExtensions_BinaryText_AndEmptyFiles()
    {
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,
            (await _client.PostAsync("/api/policies", Form([1, 2, 3], "malware.exe"))).StatusCode);

        var binary = await _client.PostAsync("/api/policies", Form([1, 2, 0, 3], "fake.txt"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, binary.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/policies", Form([], "empty.txt"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/policies", Form([65, 66], "x.txt", uploadedBy: "x"))).StatusCode);
    }

    [Fact]
    public async Task Upload_RejectsOversizedFiles_Returns413()
    {
        var big = new byte[10 * 1024 * 1024 + 1];
        Array.Fill(big, (byte)'a');

        var response = await _client.PostAsync("/api/policies", Form(big, "big.txt"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Upload_PathTraversalInFileName_IsHarmless()
    {
        var bytes = Encoding.UTF8.GetBytes($"# Traversal policy\n\n1.1 Unique {Guid.NewGuid()} wording about cover.");
        var response = await _client.PostAsync("/api/policies", Form(bytes, "..\\..\\etc/passwd.md"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var doc = (await response.Content.ReadFromJsonAsync<PolicyDocumentDto>(Json))!;
        Assert.Equal("passwd.md", doc.FileName);
    }

    [Fact]
    public async Task Search_ReturnsRankedPassagesWithScores_AndCanBeScopedToDocuments()
    {
        await UploadSampleAsync("gold-plan-policy-2024.md", "gold-plan", "2024.1", Guid.NewGuid().ToString());
        var basic = await UploadSampleAsync("basic-plan-policy-2024.md", "basic-plan", "2024.1", Guid.NewGuid().ToString());

        var all = await Search("dental waiting period");
        Assert.Equal("5.2", all.Passages[0].ClauseRef);
        Assert.Contains("Gold Plan Policy", all.Passages[0].DocumentTitle);   // other tests may upload identical copies
        Assert.NotNull(all.Passages[0].KeywordScore);
        Assert.NotNull(all.Passages[0].VectorScore);

        var scoped = await Search("annual outpatient limit", documentIds: [basic.Id]);
        Assert.All(scoped.Passages, p => Assert.Equal(basic.Id, p.DocumentId));
    }

    [Fact]
    public async Task Search_ValidatesInput()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/policies/search", new { query = "x" }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/policies/search", new { query = "dental waiting", topK = 500 }, Json)).StatusCode);
    }

    [Fact]
    public async Task DeactivatedDocuments_AreExcludedFromRetrieval()
    {
        var marker = $"zxqv{Guid.NewGuid():N}"[..14];
        var bytes = Encoding.UTF8.GetBytes($"# Marker policy\n\n1.1 The {marker} benefit covers a special unicorn grooming allowance.");
        var created = await _client.PostAsync("/api/policies", Form(bytes, "marker.md"));
        var doc = (await created.Content.ReadFromJsonAsync<PolicyDocumentDto>(Json))!;

        Assert.Contains(doc.Id, (await Search(marker)).Passages.Select(p => p.DocumentId));

        var patch = await _client.PatchAsJsonAsync($"/api/policies/{doc.Id}/active", new { isActive = false }, Json);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        Assert.DoesNotContain(doc.Id, (await Search(marker)).Passages.Select(p => p.DocumentId));
    }

    [Fact]
    public async Task GetUnknownDocument_Returns404_AndStatusReportsProviders()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/policies/999999")).StatusCode);

        var status = await _client.GetFromJsonAsync<AiStatusDto>("/api/ai/status", Json);
        Assert.StartsWith("local-hashing", status!.EmbeddingModel);
        Assert.Equal("extractive", status.SummaryGenerator);
        Assert.False(status.SummariesUseLanguageModel);
    }

    [Fact]
    public async Task Reindex_RebuildsEmbeddingsWithoutLosingPassages()
    {
        await UploadSampleAsync("claims-handling-guidelines.md", "internal", "3", Guid.NewGuid().ToString());
        var before = await _client.GetFromJsonAsync<AiStatusDto>("/api/ai/status", Json);

        var response = await _client.PostAsync("/api/policies/reindex", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await _client.GetFromJsonAsync<AiStatusDto>("/api/ai/status", Json);
        Assert.Equal(before!.IndexedChunks, after!.IndexedChunks);
        Assert.Equal(0, after.StaleChunks);
    }

    private async Task<PolicySearchResponse> Search(string query, int[]? documentIds = null)
    {
        var response = await _client.PostAsJsonAsync("/api/policies/search", new { query, topK = 6, documentIds }, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PolicySearchResponse>(Json))!;
    }
}
