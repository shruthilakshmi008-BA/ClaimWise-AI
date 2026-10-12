using ClaimWise.Api.Dtos;

namespace ClaimWise.Api.Services;

public interface IClaimService
{
    Task<ClaimDetailDto> CreateAsync(CreateClaimRequest request, CancellationToken ct);
    Task<PagedResult<ClaimSummaryDto>> ListAsync(ClaimListQuery query, CancellationToken ct);
    Task<ClaimDetailDto> GetAsync(int id, CancellationToken ct);
    Task<DashboardDto> GetDashboardAsync(CancellationToken ct);
    Task<ValidationDto> GetValidationAsync(int id, CancellationToken ct);
    Task<DocumentDto> AddDocumentAsync(int id, AddDocumentRequest request, CancellationToken ct);
    Task<ClaimDetailDto> ChangeStatusAsync(int id, ChangeStatusRequest request, CancellationToken ct);
    Task<IReadOnlyList<AuditEntryDto>> GetAuditAsync(int id, CancellationToken ct);
}
