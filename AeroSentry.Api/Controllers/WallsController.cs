using AeroSentry.Api.DTOs;
using AeroSentry.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroSentry.Api.Controllers;

[Route("api/v1/properties/{propertyId:guid}/walls")]
public class WallsController : ApiControllerBase
{
    private readonly IWallService _walls;

    public WallsController(IWallService walls)
    {
        _walls = walls;
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(Guid propertyId, WallRequest request) =>
        FromResult(await _walls.CreateAsync(CallerId, propertyId, request), created: true);

    [HttpGet]
    public async Task<IActionResult> List(Guid propertyId) =>
        FromResult(await _walls.ListAsync(CallerId, CallerRole, propertyId));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid propertyId, Guid id) =>
        FromResult(await _walls.GetAsync(CallerId, CallerRole, propertyId, id));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid propertyId, Guid id, WallRequest request) =>
        FromResult(await _walls.UpdateAsync(CallerId, propertyId, id, request));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid propertyId, Guid id) =>
        NoContentFromResult(await _walls.DeleteAsync(CallerId, propertyId, id));
}
