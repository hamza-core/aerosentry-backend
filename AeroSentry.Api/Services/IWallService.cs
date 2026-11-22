using AeroSentry.Api.DTOs;

namespace AeroSentry.Api.Services;

public interface IWallService
{
    Task<Result<List<WallResponse>>> ListAsync(Guid userId, string role, Guid propertyId);
    Task<Result<WallResponse>> GetAsync(Guid userId, string role, Guid propertyId, Guid id);
    Task<Result<WallResponse>> CreateAsync(Guid adminId, Guid propertyId, WallRequest request);
    Task<Result<WallResponse>> UpdateAsync(Guid adminId, Guid propertyId, Guid id, WallRequest request);
    Task<Result<bool>> DeleteAsync(Guid adminId, Guid propertyId, Guid id);
}
