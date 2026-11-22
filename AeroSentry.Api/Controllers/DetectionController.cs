using AeroSentry.Api.DTOs;
using AeroSentry.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroSentry.Api.Controllers;

[Route("api/v1/properties/{propertyId:guid}/walls/{wallId:guid}/detection")]
public class DetectionController : ApiControllerBase
{
    private readonly IDetectionService _detection;

    public DetectionController(IDetectionService detection)
    {
        _detection = detection;
    }

    [HttpPost("start")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Start(Guid propertyId, Guid wallId, [FromQuery] double? threshold) =>
        FromResult(await _detection.StartAsync(CallerId, propertyId, wallId, threshold));

    // multipart/form-data: frame (JPEG file), optional frameIndex and timestampSeconds.
    [HttpPost("frame")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Frame(Guid propertyId, Guid wallId, [FromForm] DetectionFrameRequest request)
    {
        using var buffer = new MemoryStream();
        await request.Frame.CopyToAsync(buffer);

        return FromResult(await _detection.FrameAsync(
            CallerId, propertyId, wallId, buffer.ToArray(), request.FrameIndex, request.TimestampSeconds));
    }

    [HttpPost("stop")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Stop(Guid propertyId, Guid wallId) =>
        FromResult(await _detection.StopAsync(CallerId, propertyId, wallId));

    [HttpGet("events")]
    public async Task<IActionResult> Events(Guid propertyId, Guid wallId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        FromResult(await _detection.ListEventsAsync(CallerId, CallerRole, propertyId, wallId, page, pageSize));
}
