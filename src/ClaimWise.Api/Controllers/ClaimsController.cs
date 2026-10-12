using ClaimWise.Api.Dtos;
using ClaimWise.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaimWise.Api.Controllers;

[ApiController]
[Route("api/claims")]
[Produces("application/json")]
public class ClaimsController(IClaimService claims) : ControllerBase
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

    /// <summary>The audit history for a claim, oldest first.</summary>
    [HttpGet("{id:int}/audit")]
    public async Task<ActionResult<IReadOnlyList<AuditEntryDto>>> Audit(int id, CancellationToken ct)
        => Ok(await claims.GetAuditAsync(id, ct));
}
