using System.Text.Json;
using AeroSentry.Api.Data.Entities;

namespace AeroSentry.Api.Services;

// Decides which wall cell a point is in. The point is a person's foot point:
// the bottom-centre of the bounding box the ML service reports.
public static class CellLocator
{
    // Returns the first cell (in wall order) that contains the point, or null when the point is
    // outside every cell (those points are ignored, as in the design).
    public static WallCell? Find(IEnumerable<WallCell> cells, double x, double y)
    {
        foreach (var cell in cells.OrderBy(c => c.Position))
        {
            // Stored order: top-left, bottom-left, top-right, bottom-right.
            var c = JsonSerializer.Deserialize<double[][]>(cell.CornersJson)!;

            // Walk around the edge instead: top-left -> top-right -> bottom-right -> bottom-left.
            var polygon = new[] { c[0], c[2], c[3], c[1] };

            if (Contains(polygon, x, y))
                return cell;
        }
        return null;
    }

    // Ray casting: shoot a line to the right of the point and count how many polygon edges it crosses.
    // An odd count means the point is inside.
    private static bool Contains(double[][] polygon, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            double xi = polygon[i][0], yi = polygon[i][1];
            double xj = polygon[j][0], yj = polygon[j][1];

            var edgeCrossesRow = (yi > y) != (yj > y);
            if (edgeCrossesRow && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }
}
