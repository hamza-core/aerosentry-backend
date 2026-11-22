using AeroSentry.Api.DTOs;

namespace AeroSentry.Api.Services;

public interface ICameraService
{
    // null = the property is not visible to this caller (-> 404)
    Task<List<CameraResponse>?> ListAsync(Guid userId, string role, Guid propertyId);
    Task<CameraResponse?> GetAsync(Guid userId, string role, Guid propertyId, Guid id);

    Task<CameraResult> CreateAsync(Guid adminId, Guid propertyId, CameraRequest request);
    Task<CameraResult> UpdateAsync(Guid adminId, Guid propertyId, Guid id, CameraRequest request);
    Task<bool> DeleteAsync(Guid adminId, Guid propertyId, Guid id);

    // null = the property is not this admin's
    Task<CameraTestResponse?> TestAsync(Guid adminId, Guid propertyId, CameraRequest request);
}
