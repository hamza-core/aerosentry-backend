using System.Security.Claims;
using AeroSentry.Api.DTOs;
using AeroSentry.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroSentry.Api.Controllers;

[ApiController]
[Route("api/v1/properties/{propertyId:guid}/cameras")]
[Authorize]   // any signed-in user; write actions below add Roles = "Admin"
public class CamerasController : ControllerBase
{
    private readonly ICameraService _cameras;

    public CamerasController(ICameraService cameras)
    {
        _cameras = cameras;
    }

    private Guid CallerId => Guid.Parse(User.FindFirstValue("sub")!);
    private string CallerRole => User.FindFirstValue("role")!;

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(Guid propertyId, CameraRequest request)
    {
        var result = await _cameras.CreateAsync(CallerId, propertyId, request);
        return ToActionResult(result, created: true);
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid propertyId)
    {
        var result = await _cameras.ListAsync(CallerId, CallerRole, propertyId);
        if (result is null)
            return Problem(statusCode: 404, title: "Property not found");

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid propertyId, Guid id)
    {
        var result = await _cameras.GetAsync(CallerId, CallerRole, propertyId, id);
        if (result is null)
            return Problem(statusCode: 404, title: "Camera not found");

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid propertyId, Guid id, CameraRequest request)
    {
        var result = await _cameras.UpdateAsync(CallerId, propertyId, id, request);
        return ToActionResult(result, created: false);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid propertyId, Guid id)
    {
        if (!await _cameras.DeleteAsync(CallerId, propertyId, id))
            return Problem(statusCode: 404, title: "Camera not found");

        return NoContent();
    }

    // Checks that the camera's host:port is reachable. Nothing is saved.
    [HttpPost("test")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Test(Guid propertyId, CameraRequest request)
    {
        var result = await _cameras.TestAsync(CallerId, propertyId, request);
        if (result is null)
            return Problem(statusCode: 404, title: "Property not found");

        return Ok(result);
    }

    private IActionResult ToActionResult(CameraResult result, bool created)
    {
        return result.Status switch
        {
            CameraStatus.NotFound => Problem(statusCode: 404, title: "Property or camera not found"),
            CameraStatus.DuplicateName => Problem(statusCode: 409, title: "A camera with this name already exists in this property"),
            _ => created
                ? StatusCode(StatusCodes.Status201Created, result.Camera)
                : Ok(result.Camera)
        };
    }
}
