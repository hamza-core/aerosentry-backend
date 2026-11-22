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
}
