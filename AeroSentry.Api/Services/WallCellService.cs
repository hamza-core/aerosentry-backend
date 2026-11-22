using System.Text.Json;
using AeroSentry.Api.Data;
using AeroSentry.Api.Data.Entities;
using AeroSentry.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AeroSentry.Api.Services;

public class WallCellService : IWallCellService
{
    private readonly AppDbContext _db;

    public WallCellService(AppDbContext db)
    {
        _db = db;
    }

    // The list sent is the full truth: cells in the list are added or updated (matched by label,
    // so a cell keeps its id and its laser route), cells missing from the list are deleted.
    public async Task<Result<List<WallCellResponse>>> SaveCellsAsync(
        Guid adminId, Guid propertyId, Guid wallId, List<WallCellRequest> cells)
    {
        if (!await _db.OwnsProperty(adminId, propertyId))
            return Result<List<WallCellResponse>>.NotFound("Property not found");

        var wall = await _db.Walls.Include(w => w.Cells)
            .FirstOrDefaultAsync(w => w.Id == wallId && w.PropertyId == propertyId);
        if (wall is null)
            return Result<List<WallCellResponse>>.NotFound("Wall not found");

        var problem = Validate(cells);
        if (problem is not null)
            return Result<List<WallCellResponse>>.BadRequest(problem);

        for (var i = 0; i < cells.Count; i++)
        {
            var request = cells[i];
            var label = request.Label.Trim();

            var cell = wall.Cells.FirstOrDefault(c => c.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
            if (cell is null)
            {
                // No Id here on purpose: an entity added through a tracked navigation with a key already
                // set is assumed to exist (EF would send an UPDATE). With no key, EF generates one and INSERTs.
                cell = new WallCell { WallId = wall.Id };
                wall.Cells.Add(cell);
            }

            cell.Label = label;
            cell.Position = i;
            cell.LengthMeters = request.LengthMeters;
            cell.CornersJson = JsonSerializer.Serialize(new[]
            {
                request.TopLeft, request.BottomLeft, request.TopRight, request.BottomRight
            });
        }

        var keep = cells.Select(c => c.Label.Trim().ToLowerInvariant()).ToHashSet();
        foreach (var gone in wall.Cells.Where(c => !keep.Contains(c.Label.ToLowerInvariant())).ToList())
        {
            wall.Cells.Remove(gone);
            _db.WallCells.Remove(gone);
        }

        await _db.SaveChangesAsync();
        return Result<List<WallCellResponse>>.Ok(ToResponses(wall.Cells));
    }

    // ---- helpers ----

    private static string? Validate(List<WallCellRequest> cells)
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cells)
        {
            if (string.IsNullOrWhiteSpace(c.Label))
                return "Every cell needs a label";
            if (!labels.Add(c.Label.Trim()))
                return $"Duplicate cell label '{c.Label.Trim()}'";
            if (!(ValidPoint(c.TopLeft) && ValidPoint(c.BottomLeft) && ValidPoint(c.TopRight) && ValidPoint(c.BottomRight)))
                return $"Cell '{c.Label.Trim()}': every corner must be [x, y] with values from 0 to 1";
        }
        return null;
    }

    private static bool ValidPoint(double[]? p) => p is { Length: 2 } && p.All(v => v >= 0 && v <= 1);

    // Public so WallService can reuse it when it returns a wall with its cells.
    public static List<WallCellResponse> ToResponses(IEnumerable<WallCell> cells)
    {
        var list = new List<WallCellResponse>();
        double start = 0;

        foreach (var c in cells.OrderBy(c => c.Position))
        {
            var corners = JsonSerializer.Deserialize<double[][]>(c.CornersJson)!;
            list.Add(new WallCellResponse
            {
                Id = c.Id,
                Label = c.Label,
                Position = c.Position,
                LengthMeters = c.LengthMeters,
                StartMeters = start,
                EndMeters = start + c.LengthMeters,
                TopLeft = corners[0],
                BottomLeft = corners[1],
                TopRight = corners[2],
                BottomRight = corners[3]
            });
            start += c.LengthMeters;
        }

        return list;
    }
}
