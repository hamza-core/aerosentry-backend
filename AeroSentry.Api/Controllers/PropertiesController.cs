using System.Security.Claims;
using AeroSentry.Api.DTOs;
using AeroSentry.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroSentry.Api.Controllers;

[ApiController]
[Route("api/v1/properties")]
[Authorize]   // any signed-in user; write actions below add Roles = "Admin"
public class PropertiesController : ControllerBase
{
    private readonly PropertyService _properties;

    public PropertiesController(PropertyService properties)
    {
        _properties = properties;
    }

    private Guid CallerId => Guid.Parse(User.FindFirstValue("sub")!);
    private string CallerRole => User.FindFirstValue("role")!;

    // Latitude and longitude must come together or not at all.
    private bool CoordinatesIncomplete(PropertyRequest r) => (r.Latitude is null) != (r.Longitude is null);

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(PropertyRequest request)
    {
        if (CoordinatesIncomplete(request))
            return Problem(statusCode: 400, title: "Send both latitude and longitude, or neither");

        var result = await _properties.CreateAsync(CallerId, request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        return Ok(await _properties.ListAsync(CallerId, CallerRole));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var result = await _properties.GetAsync(CallerId, CallerRole, id);
        if (result is null)
            return Problem(statusCode: 404, title: "Property not found");

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, PropertyRequest request)
    {
        if (CoordinatesIncomplete(request))
            return Problem(statusCode: 400, title: "Send both latitude and longitude, or neither");

        var result = await _properties.UpdateAsync(CallerId, id, request);
        if (result is null)
            return Problem(statusCode: 404, title: "Property not found");

        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await _properties.DeleteAsync(CallerId, id))
            return Problem(statusCode: 404, title: "Property not found");

        return NoContent();
    }

    // ---- which guards work at this property ----

    [HttpGet("{id:guid}/guards")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ListGuards(Guid id)
    {
        var result = await _properties.ListGuardsAsync(CallerId, id);
        if (result is null)
            return Problem(statusCode: 404, title: "Property not found");

        return Ok(result);
    }

    [HttpPut("{id:guid}/guards/{guardId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AssignGuard(Guid id, Guid guardId)
    {
        if (!await _properties.AssignGuardAsync(CallerId, id, guardId))
            return Problem(statusCode: 404, title: "Property or guard not found");

        return NoContent();
    }

    [HttpDelete("{id:guid}/guards/{guardId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UnassignGuard(Guid id, Guid guardId)
    {
        if (!await _properties.UnassignGuardAsync(CallerId, id, guardId))
            return Problem(statusCode: 404, title: "Property not found");

        return NoContent();
    }
}
