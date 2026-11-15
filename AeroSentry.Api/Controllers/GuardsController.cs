using System.Security.Claims;
using AeroSentry.Api.DTOs;
using AeroSentry.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroSentry.Api.Controllers;

// =====================================================================
// GuardsController  (the "Controller" layer)
//
// WHAT IT DOES: lets an ADMIN create, list, suspend and delete his own guards.
//
// REQUEST FLOW: Client -> [token checked, role must be Admin] -> THIS FILE
//               -> GuardService -> database
//
// [Authorize(Roles = "Admin")] on the class protects EVERY action in this file:
//   no token -> 401,  token of a Guard -> 403.
// =====================================================================
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

    // The id of the logged-in admin. It was stored in the "sub" claim of his token.
    private Guid GetCurrentAdminId()
    {
        string? idText = User.FindFirstValue("sub");
        return Guid.Parse(idText!);
    }

    // POST /api/v1/guards   create a guard (body: fullName, email, password)
    [HttpPost]
    public async Task<IActionResult> Create(RegisterRequest request)
    {
        var result = await _guards.CreateAsync(GetCurrentAdminId(), request);

        if (result == null)
        {
            return Problem(statusCode: 409, title: "Email already registered");   // 409 = conflict
        }
        return StatusCode(201, result);   // 201 = created
    }

    // GET /api/v1/guards                           all my guards
    // GET /api/v1/guards?status=Active|Suspended   only guards with that status
    [HttpGet]
    public async Task<IActionResult> List(string? status = null)
    {
        // status is optional. If it was sent, it must be "Active" or "Suspended".
        if (status != null)
        {
            bool isActive = status.Equals("Active", StringComparison.OrdinalIgnoreCase);
            bool isSuspended = status.Equals("Suspended", StringComparison.OrdinalIgnoreCase);

            if (isActive == false && isSuspended == false)
            {
                return Problem(statusCode: 400, title: "status must be Active or Suspended");
            }
        }

        var result = await _guards.ListAsync(GetCurrentAdminId(), status);
        return Ok(result);
    }

    // PATCH /api/v1/guards/{id}/status   body: { "status": "Suspended" }
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, UpdateGuardStatusRequest request)
    {
        var result = await _guards.SetStatusAsync(GetCurrentAdminId(), id, request.Status);

        if (result == null)
        {
            return Problem(statusCode: 404, title: "Guard not found");
        }
        return Ok(result);
    }

    // DELETE /api/v1/guards/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        bool deleted = await _guards.DeleteAsync(GetCurrentAdminId(), id);

        if (deleted == false)
        {
            return Problem(statusCode: 404, title: "Guard not found");
        }
        return NoContent();   // 204
    }
}
