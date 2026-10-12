using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimWise.Api.Dtos;
using Xunit;

namespace ClaimWise.Tests;

public class ClaimDocumentUploadTests(ClaimWiseFactory factory) : ClaimWorkflowTestBase(factory), IClassFixture<ClaimWiseFactory>
{
    private static readonly byte[] Pdf = [.. "%PDF-1.4\n1 0 obj\n<<>>\nendobj\n"u8, 0x0A];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private static MultipartFormDataContent Form(string documentType, byte[] bytes, string fileName)
    {
        var form = new MultipartFormDataContent { { new StringContent(documentType), "DocumentType" } };
        form.Add(new ByteArrayContent(bytes), "File", fileName);
        return form;
    }

    private Task<HttpResponseMessage> UploadAsync(int claimId, string type, byte[] bytes, string name) =>
        Client.PostAsync($"/api/claims/{claimId}/documents/upload", Form(type, bytes, name));

    [Fact]
    public async Task Upload_StoresFile_UpdatesCompleteness_AndDownloadsIdenticalBytes()
    {
        var claim = await CreateClaimAsync();

        var response = await UploadAsync(claim.Id, "ClaimForm", Pdf, "claim-form.pdf");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var doc = (await response.Content.ReadFromJsonAsync<DocumentDto>(Json))!;
        Assert.True(doc.HasFile);
        Assert.Equal(Pdf.Length, doc.SizeBytes);
        Assert.Equal(64, doc.Sha256!.Length);

        var detail = await Client.GetFromJsonAsync<ClaimDetailDto>($"/api/claims/{claim.Id}", Json);
        Assert.DoesNotContain(Api.Domain.DocumentType.ClaimForm, detail!.Validation.MissingDocuments);

        var download = await Client.GetAsync($"/api/claims/{claim.Id}/documents/{doc.Id}/file");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());

        Assert.Contains((await AuditAsync(claim.Id)), a => a.Action == "DocumentAdded" && a.Details.Contains("ClaimForm"));
    }

    [Fact]
    public async Task Upload_StoresUnderAServerGeneratedName_NeverTheUploadedOne()
    {
        var claim = await CreateClaimAsync();

        var response = await UploadAsync(claim.Id, "Invoice", Png, "..\\..\\..\\evil/../../invoice.png");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var doc = (await response.Content.ReadFromJsonAsync<DocumentDto>(Json))!;
        Assert.Equal("invoice.png", doc.FileName);
        var stored = Directory.GetFiles(factory.FilesPath, "*", SearchOption.AllDirectories).Single(f => f.Contains($"{Path.DirectorySeparatorChar}{claim.Id}{Path.DirectorySeparatorChar}"));
        Assert.Matches(@"^[0-9a-f]{32}\.png$", Path.GetFileName(stored));
        Assert.StartsWith(Path.GetFullPath(factory.FilesPath), Path.GetFullPath(stored));
    }

    [Theory]
    [InlineData("malware.exe", "MZ-not-allowed")]
    [InlineData("report.pdf", "this is plain text not a pdf")]      // extension and content disagree
    [InlineData("photo.png", "%PDF-1.4 actually a pdf")]
    public async Task Upload_RejectsDisallowedOrMislabelledFiles(string name, string content)
    {
        var claim = await CreateClaimAsync();

        var response = await UploadAsync(claim.Id, "MedicalReport", System.Text.Encoding.UTF8.GetBytes(content), name);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var detail = await Client.GetFromJsonAsync<ClaimDetailDto>($"/api/claims/{claim.Id}", Json);
        Assert.Empty(detail!.Documents);
    }

    [Fact]
    public async Task Upload_EnforcesSizeLimit_DuplicateTypeAndFinalStatus()
    {
        var claim = await CreateClaimAsync();
        var big = new byte[10 * 1024 * 1024 + 1];
        Pdf.CopyTo(big, 0);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await UploadAsync(claim.Id, "Invoice", big, "big.pdf")).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(claim.Id, "Invoice", Pdf, "a.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await UploadAsync(claim.Id, "Invoice", Pdf, "b.pdf")).StatusCode);

        await StatusAsync(claim.Id, "InReview");
        await StatusAsync(claim.Id, "Rejected", "Rejected for test purposes.");
        Assert.Equal(HttpStatusCode.Conflict, (await UploadAsync(claim.Id, "ClaimForm", Pdf, "c.pdf")).StatusCode);
    }

    [Fact]
    public async Task MetadataOnlyDocuments_HaveNoFileToDownload_AndUnknownIdsReturn404()
    {
        var claim = await CreateClaimAsync();
        var meta = await Client.PostAsJsonAsync($"/api/claims/{claim.Id}/documents", new { documentType = "ClaimForm", fileName = "form.pdf" }, Json);
        var doc = (await meta.Content.ReadFromJsonAsync<DocumentDto>(Json))!;

        Assert.False(doc.HasFile);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/claims/{claim.Id}/documents/{doc.Id}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/claims/{claim.Id}/documents/999999/file")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(999999, "Invoice", Pdf, "x.pdf")).StatusCode);
    }

    private readonly ClaimWiseFactory factory = factory;
}
