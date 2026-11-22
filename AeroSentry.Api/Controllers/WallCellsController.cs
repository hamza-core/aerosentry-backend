using AeroSentry.Api.DTOs;
using AeroSentry.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroSentry.Api.Controllers;

[Route("api/v1/properties/{propertyId:guid}/walls/{wallId:guid}")]
public class WallCellsController : ApiControllerBase
{
    private readonly IWallCellService _cells;

    public WallCellsController(IWallCellService cells)
    {
        _cells = cells;
    }

    [HttpPut("cells")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SaveCells(Guid propertyId, Guid wallId, List<WallCellRequest> cells) =>
        FromResult(await _cells.SaveCellsAsync(CallerId, propertyId, wallId, cells));

    // Body: squares in the order the drone should visit them, e.g. [{"row":0,"col":0},{"row":1,"col":0}]
    [HttpPut("cells/{cellId:guid}/route")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetRoute(Guid propertyId, Guid wallId, Guid cellId, List<GridSquareDto> squares) =>
        FromResult(await _cells.SetRouteAsync(CallerId, propertyId, wallId, cellId, squares));

    [HttpGet("validation")]
    public async Task<IActionResult> Validate(Guid propertyId, Guid wallId) =>
        FromResult(await _cells.ValidateAsync(CallerId, CallerRole, propertyId, wallId));
}
