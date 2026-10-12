using ClaimWise.Api.Dtos;
using ClaimWise.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClaimWise.Api.Controllers;

[ApiController]
[Route("api/claims")]
[Produces("application/json")]
public class ClaimsController(IClaimService claims, IAiSummaryService summaries) : ControllerBase
{
    /// <summary>Submit a new claim (claim intake).</summary>
    [HttpPost]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ClaimDetailDto>> Create(CreateClaimRequest request, CancellationToken ct)
    {
        var created = await claims.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>List claims with filtering, searching, sorting and pagination.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ClaimSummaryDto>>> List([FromQuery] ClaimListQuery query, CancellationToken ct)
        => Ok(await claims.ListAsync(query, ct));

    /// <summary>Dashboard numbers: counts per status, total amount, incomplete open claims.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardDto>> Summary(CancellationToken ct)
        => Ok(await claims.GetDashboardAsync(ct));

    /// <summary>Get one claim with its documents, validation result and allowed next statuses.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClaimDetailDto>> GetById(int id, CancellationToken ct)
        => Ok(await claims.GetAsync(id, ct));

    /// <summary>Check which required documents and fields are missing.</summary>
    [HttpGet("{id:int}/validation")]
    public async Task<ActionResult<ValidationDto>> Validation(int id, CancellationToken ct)
        => Ok(await claims.GetValidationAsync(id, ct));

    /// <summary>Record that a supporting document was received (metadata only in this stage).</summary>
    [HttpPost("{id:int}/documents")]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<DocumentDto>> AddDocument(int id, AddDocumentRequest request, CancellationToken ct)
    {
        var doc = await claims.AddDocumentAsync(id, request, ct);
        return Created($"/api/claims/{id}", doc);
    }

    /// <summary>Move a claim to a new status. Decisions are always made by a named human reviewer.</summary>
    [HttpPost("{id:int}/status")]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClaimDetailDto>> ChangeStatus(int id, ChangeStatusRequest request, CancellationToken ct)
        => Ok(await claims.ChangeStatusAsync(id, request, ct));

    /// <summary>
    /// Generate an evidence-grounded AI summary from the policy documents. The result is Answered (with validated
    /// citations), Abstained (evidence insufficient) or Failed (provider error). It never changes the claim status.
    /// </summary>
    [HttpPost("{id:int}/summaries")]
    [EnableRateLimiting("ai")]
    [ProducesResponseType<AiSummaryDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AiSummaryDto>> GenerateSummary(int id, GenerateSummaryRequest request, CancellationToken ct)
    {
        var summary = await summaries.GenerateAsync(id, request, ct);
        return CreatedAtAction(nameof(GetSummary), new { id, summaryId = summary.Id }, summary);
    }

    /// <summary>All AI summaries for a claim, newest first.</summary>
    [HttpGet("{id:int}/summaries")]
    public async Task<ActionResult<IReadOnlyList<AiSummaryDto>>> ListSummaries(int id, CancellationToken ct)
        => Ok(await summaries.ListAsync(id, ct));

    [HttpGet("{id:int}/summaries/{summaryId:int}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AiSummaryDto>> GetSummary(int id, int summaryId, CancellationToken ct)
        => Ok(await summaries.GetAsync(id, summaryId, ct));

    /// <summary>A named human reviewer accepts or rejects an AI summary. Required before the claim can be approved or rejected.</summary>
    [HttpPost("{id:int}/summaries/{summaryId:int}/review")]
    [ProducesResponseType<AiSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AiSummaryDto>> ReviewSummary(int id, int summaryId, ReviewSummaryRequest request, CancellationToken ct)
        => Ok(await summaries.ReviewAsync(id, summaryId, request, ct));

    /// <summary>The audit history for a claim, oldest first.</summary>
    [HttpGet("{id:int}/audit")]
    public async Task<ActionResult<IReadOnlyList<AuditEntryDto>>> Audit(int id, CancellationToken ct)
        => Ok(await claims.GetAuditAsync(id, ct));
}
