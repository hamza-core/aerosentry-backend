using AeroSentry.Api.DTOs;

namespace AeroSentry.Api.Services;

public interface IWallCellService
{
    // Saves the whole list of cells of a wall (the "Save Wall Cells" button).
    Task<Result<List<WallCellResponse>>> SaveCellsAsync(Guid adminId, Guid propertyId, Guid wallId, List<WallCellRequest> cells);
}
