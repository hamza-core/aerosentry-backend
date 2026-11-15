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
}
