using ClaimWise.Api.Dtos;
using ClaimWise.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClaimWise.Api.Controllers;

[ApiController]
[Route("api/policies")]
[Produces("application/json")]
public class PoliciesController(IPolicyService policies, IPolicySearchService search) : ControllerBase
{
    /// <summary>Upload a (fictional) policy document: .txt, .md, .pdf or .docx. Text is extracted, chunked and embedded.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    [EnableRateLimiting("upload")]
    [ProducesResponseType<PolicyDocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PolicyDocumentDto>> Upload([FromForm] UploadPolicyRequest request, CancellationToken ct)
    {
        var created = await policies.UploadAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>List policy documents, newest first (including inactive ones).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PolicyDocumentDto>>> List(CancellationToken ct) => Ok(await policies.ListAsync(ct));

    [HttpGet("{id:int}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PolicyDocumentDto>> Get(int id, CancellationToken ct) => Ok(await policies.GetAsync(id, ct));

    /// <summary>The passages a document was split into, with clause, section and page metadata.</summary>
    [HttpGet("{id:int}/chunks")]
    public async Task<ActionResult<IReadOnlyList<PolicyChunkDto>>> Chunks(int id, CancellationToken ct) => Ok(await policies.GetChunksAsync(id, ct));

    /// <summary>Include or exclude a document from retrieval without deleting it.</summary>
    [HttpPatch("{id:int}/active")]
    public async Task<ActionResult<PolicyDocumentDto>> SetActive(int id, SetPolicyActiveRequest request, CancellationToken ct) =>
        Ok(await policies.SetActiveAsync(id, request.IsActive!.Value, ct));

    /// <summary>Re-embed all passages with the configured embedding model (after changing the embedding provider).</summary>
    [HttpPost("reindex")]
    [EnableRateLimiting("ai")]
    public async Task<ActionResult<object>> Reindex(CancellationToken ct) => Ok(new { reindexedChunks = await policies.ReindexAsync(ct) });

    /// <summary>Hybrid (keyword + vector + rerank) search over active policy passages, with per-stage scores.</summary>
    [HttpPost("search")]
    [EnableRateLimiting("ai")]
    public async Task<ActionResult<PolicySearchResponse>> Search(PolicySearchRequest request, CancellationToken ct) =>
        Ok(await search.SearchAsync(request, ct));
}

[ApiController]
[Route("api/ai")]
[Produces("application/json")]
public class AiController(IPolicySearchService search) : ControllerBase
{
    /// <summary>Which embedding and summary providers are active, and how much is indexed.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<AiStatusDto>> Status(CancellationToken ct) => Ok(await search.GetStatusAsync(ct));
}
