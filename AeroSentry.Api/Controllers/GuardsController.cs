using AeroSentry.Api.DTOs;
using AeroSentry.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroSentry.Api.Controllers;

[ApiController]
[Route("api/v1/guards")]
[Authorize(Roles = "Admin")]
public class GuardsController : ControllerBase
{
    private readonly GuardService _guards;

    public GuardsController(GuardService guards)
    {
        _guards = guards;
    }

    [HttpPost]
    public async Task<IActionResult> Create(RegisterRequest request)
    {
        var result = await _guards.CreateAsync(request);
        if (result is null)
            return Problem(statusCode: 409, title: "Email already registered");

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet]
    public async Task<IActionResult> List(string? status = null)
    {
        if (status is not null &&
            !status.Equals("Active", StringComparison.OrdinalIgnoreCase) &&
            !status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
        {
            return Problem(statusCode: 400, title: "status must be Active or Suspended");
        }

        return Ok(await _guards.ListAsync(status));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, UpdateGuardStatusRequest request)
    {
        var result = await _guards.SetStatusAsync(id, request.Status);
        if (result is null)
            return Problem(statusCode: 404, title: "Guard not found");

        return Ok(result);
    }


    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _guards.DeleteAsync(id);
        if (!deleted)
            return Problem(statusCode: 404, title: "Guard not found");

        return NoContent();
    }
}

