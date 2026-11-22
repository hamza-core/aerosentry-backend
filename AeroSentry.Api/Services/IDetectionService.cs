using AeroSentry.Api.DTOs;

namespace AeroSentry.Api.Services;

public interface IDetectionService
{
    // Opens (or re-opens) the ML live session for a wall, using the wall's annotation.
    Task<Result<DetectionStartResponse>> StartAsync(Guid adminId, Guid propertyId, Guid wallId, double? threshold);

    // One JPEG frame in; probability, people with their cells, and a stored event on a new alarm out.
    Task<Result<DetectionFrameResponse>> FrameAsync(Guid adminId, Guid propertyId, Guid wallId, byte[] jpeg, int frameIndex, double timestampSeconds);

    Task<Result<DetectionStopResponse>> StopAsync(Guid adminId, Guid propertyId, Guid wallId);

    Task<Result<PagedResponse<DetectionEventResponse>>> ListEventsAsync(Guid userId, string role, Guid propertyId, Guid wallId, int page, int pageSize);
}
