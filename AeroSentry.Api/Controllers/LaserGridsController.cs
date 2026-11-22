using AeroSentry.Api.DTOs;
using AeroSentry.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroSentry.Api.Controllers;

[Route("api/v1/properties/{propertyId:guid}/laser-grids")]
public class LaserGridsController : ApiControllerBase
{
    private readonly ILaserGridService _grids;

    public LaserGridsController(ILaserGridService grids)
    {
        _grids = grids;
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(Guid propertyId, LaserGridRequest request) =>
        FromResult(await _grids.CreateAsync(CallerId, propertyId, request), created: true);

    [HttpGet]
    public async Task<IActionResult> List(Guid propertyId) =>
        FromResult(await _grids.ListAsync(CallerId, CallerRole, propertyId));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid propertyId, Guid id) =>
        FromResult(await _grids.GetAsync(CallerId, CallerRole, propertyId, id));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid propertyId, Guid id, LaserGridRequest request) =>
        FromResult(await _grids.UpdateAsync(CallerId, propertyId, id, request));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid propertyId, Guid id) =>
        NoContentFromResult(await _grids.DeleteAsync(CallerId, propertyId, id));
}
