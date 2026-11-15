using System.Security.Claims;
using AeroSentry.Api.DTOs;
using AeroSentry.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroSentry.Api.Controllers;

// =====================================================================
// PropertiesController  (the "Controller" layer)
//
// WHAT IT DOES: receives HTTP requests for /api/v1/properties, checks the input,
//               calls PropertyService, and turns the result into an HTTP status code.
//               It does NOT touch the database itself.
//
// REQUEST FLOW: Client -> [token checked] -> THIS FILE -> PropertyService -> database
//
// SECURITY:  [Authorize]                  = must be logged in (valid token)
//            [Authorize(Roles = "Admin")] = must be logged in AND have role Admin
//            Wrong/missing token -> 401.   Logged in but wrong role -> 403.
//
// STATUS CODES USED: 200 OK, 201 Created, 204 No Content (success, empty body),
//                    400 bad input, 404 not found
//
// IF THE TEACHER ASKS YOU TO add a new endpoint: copy one action below, change the
// attribute ([HttpGet], [HttpPost]...), the route text, and the service method it calls.
// =====================================================================
[ApiController]
[Route("api/v1/properties")]
[Authorize]   // every action needs a logged-in user; write actions also add Roles = "Admin"
public class PropertiesController : ControllerBase
{
    private readonly PropertyService _properties;

    public PropertiesController(PropertyService properties)
    {
        _properties = properties;
    }

    // ---------- small helpers: who is calling? ----------

    // The token contains claims (facts about the user). We saved the user's id in the
    // "sub" claim when we created the token (see AuthService.LoginAsync).
    private Guid GetCurrentUserId()
    {
        string? idText = User.FindFirstValue("sub");
        return Guid.Parse(idText!);
    }

    // The "role" claim is "Admin" or "Guard".
    private string GetCurrentRole()
    {
        string? role = User.FindFirstValue("role");
        return role!;
    }

    // true when only one of latitude/longitude was sent (we want both or neither).
    private bool CoordinatesAreIncomplete(PropertyRequest request)
    {
        bool hasLatitude = request.Latitude != null;
        bool hasLongitude = request.Longitude != null;
        return hasLatitude != hasLongitude;
    }

    // ---------- PROPERTIES ----------

    // POST /api/v1/properties      (Admin) create a property
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(PropertyRequest request)
    {
        // [Required], [Range] etc. on PropertyRequest were already checked by ASP.NET (400 if bad).
        if (CoordinatesAreIncomplete(request))
        {
            return Problem(statusCode: 400, title: "Send both latitude and longitude, or neither");
        }

        var result = await _properties.CreateAsync(GetCurrentUserId(), request);
        return StatusCode(201, result);
    }

    // GET /api/v1/properties       (Admin: his own, Guard: assigned to him)
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var result = await _properties.ListAsync(GetCurrentUserId(), GetCurrentRole());
        return Ok(result);
    }

    // GET /api/v1/properties/{id}  (one property)
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var result = await _properties.GetAsync(GetCurrentUserId(), GetCurrentRole(), id);
        if (result == null)
        {
            return Problem(statusCode: 404, title: "Property not found");
        }
        return Ok(result);
    }

    // PUT /api/v1/properties/{id}  (Admin) replace the property's data
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, PropertyRequest request)
    {
        if (CoordinatesAreIncomplete(request))
        {
            return Problem(statusCode: 400, title: "Send both latitude and longitude, or neither");
        }

        var result = await _properties.UpdateAsync(GetCurrentUserId(), id, request);
        if (result == null)
        {
            return Problem(statusCode: 404, title: "Property not found");
        }
        return Ok(result);
    }

    // DELETE /api/v1/properties/{id}  (Admin)
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        bool deleted = await _properties.DeleteAsync(GetCurrentUserId(), id);
        if (deleted == false)
        {
            return Problem(statusCode: 404, title: "Property not found");
        }
        return NoContent();   // 204: success, nothing to send back
    }

    // ---------- GUARDS OF A PROPERTY (Admin only) ----------

    // GET /api/v1/properties/{id}/guards   list the guards assigned to this property
    [HttpGet("{id:guid}/guards")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ListGuards(Guid id)
    {
        var result = await _properties.ListGuardsAsync(GetCurrentUserId(), id);
        if (result == null)
        {
            return Problem(statusCode: 404, title: "Property not found");
        }
        return Ok(result);
    }

    // PUT /api/v1/properties/{id}/guards/{guardId}   assign a guard to this property
    [HttpPut("{id:guid}/guards/{guardId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AssignGuard(Guid id, Guid guardId)
    {
        bool done = await _properties.AssignGuardAsync(GetCurrentUserId(), id, guardId);
        if (done == false)
        {
            return Problem(statusCode: 404, title: "Property or guard not found");
        }
        return NoContent();
    }

    // DELETE /api/v1/properties/{id}/guards/{guardId}   remove the guard from this property
    [HttpDelete("{id:guid}/guards/{guardId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UnassignGuard(Guid id, Guid guardId)
    {
        bool done = await _properties.UnassignGuardAsync(GetCurrentUserId(), id, guardId);
        if (done == false)
        {
            return Problem(statusCode: 404, title: "Property not found");
        }
        return NoContent();
    }
}
