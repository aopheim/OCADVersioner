using Dbscan;
using Dbscan.RBush;

namespace OcadViewer.Changes;

/// <summary>
/// Groups changed pixels by density, using the Dbscan package.
/// </summary>
/// <remarks>
/// The one file in the solution that names a clustering library, which is the
/// point of <see cref="IChangeGrouper"/>. Everything above it asks for regions.
///
/// <para>Two things happen here that the algorithm does not do by itself. The
/// mask is reduced to cells first, because clustering several hundred thousand
/// individual pixels answers a question nobody asked at a cost nobody wants.
/// And the minimum cluster size is one, which turns DBSCAN into plain
/// density-linked grouping with no notion of noise: a single changed millimetre
/// is a real edit and dropping it as an outlier would be a lie about the
/// map.</para>
///
/// <para>The R-tree index, rather than the package's default, because the
/// default compares every point with every other. That is fine for the hundreds
/// of cells an ordinary revision produces and hopeless for the tens of
/// thousands that comparing two unrelated maps produces - which is a thing
/// users do by accident, and it should be slow rather than hung.</para>
/// </remarks>
public sealed class DbscanChangeGrouper : IChangeGrouper
{
    public ChangeGrouping Group(ChangeMask mask, ChangeGroupingOptions options = null)
    {
        ArgumentNullException.ThrowIfNull(mask);
        options ??= ChangeGroupingOptions.Default;

        int cellPixels = Math.Max(
            1, (int)Math.Round(options.CellMillimetres * mask.PixelsPerMillimetre));

        List<Cell> cells = Occupied(mask, cellPixels, out int changedPixels);

        if (cells.Count == 0)
            return new ChangeGrouping([], 0, 0, mask.Pixels.Length);

        // Cell widths, since that is what the points are measured in. At least
        // one, so neighbouring cells always group however coarse they are.
        double epsilon = Math.Max(
            1.0, options.GapMillimetres * mask.PixelsPerMillimetre / cellPixels);

        ClusterSet<Cell> clustered = DbscanRBush.CalculateClusters(
            cells, epsilon, options.MinimumCells);

        // Density clustering leaves out the cells too isolated to be part of
        // anything. They are still changes on the map, so each becomes a region
        // of its own and is judged on its size like the rest.
        IEnumerable<IEnumerable<Cell>> groups = clustered.Clusters
            .Select(cluster => (IEnumerable<Cell>)cluster.Objects)
            .Concat(clustered.UnclusteredObjects.Select(cell => (IEnumerable<Cell>)[cell]));

        // Worth a trip, or not. Sorted first so the floor is applied to a list
        // that is already in the order the page will show.
        List<ChangeRegion> found = [..
            groups.Select(group => Region(mask, group))
                  .OrderByDescending(region => region.Pixels)
                  // Ties broken by position, top-left first, so the same mask
                  // always produces the same list in the same order.
                  .ThenByDescending(region => region.Area.MaxY)
                  .ThenBy(region => region.Area.MinX)];

        int floor = (int)Math.Ceiling(changedPixels * options.MinimumShare);
        int listed = found.Count(region => region.Pixels >= floor);

        List<ChangeRegion> regions = [..
            found.Take(listed).Select((region, index) => region with { Rank = index + 1 })];

        return new ChangeGrouping(regions, found.Count - listed, changedPixels, mask.Pixels.Length);
    }

    /// <summary>
    /// The mask reduced to the cells that contain a change, each remembering the
    /// pixels it actually covered.
    /// </summary>
    /// <remarks>
    /// Keeping the exact pixel bounds per cell is what lets a region report a
    /// tight rectangle rather than one rounded out to the cell grid, without a
    /// second pass over the mask.
    /// </remarks>
    private static List<Cell> Occupied(ChangeMask mask, int cellPixels, out int changedPixels)
    {
        int across = (mask.Width + cellPixels - 1) / cellPixels;
        int down = (mask.Height + cellPixels - 1) / cellPixels;

        Cell[] grid = new Cell[across * down];
        List<Cell> occupied = [];
        byte[] pixels = mask.Pixels;

        changedPixels = 0;

        for (int row = 0, at = 0; row < mask.Height; row++) {
            int cellRow = row / cellPixels;

            for (int column = 0; column < mask.Width; column++, at++) {
                if (pixels[at] == 0)
                    continue;

                changedPixels++;

                int cellColumn = column / cellPixels;
                int index = cellRow * across + cellColumn;

                Cell cell = grid[index];

                if (cell is null) {
                    cell = new Cell(cellColumn, cellRow, column, row);
                    grid[index] = cell;
                    occupied.Add(cell);
                }
                else {
                    cell.Add(column, row);
                }
            }
        }

        return occupied;
    }

    private static ChangeRegion Region(ChangeMask mask, IEnumerable<Cell> cells)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        int pixels = 0;

        foreach (Cell cell in cells) {
            left = Math.Min(left, cell.Left);
            top = Math.Min(top, cell.Top);
            right = Math.Max(right, cell.Right);
            bottom = Math.Max(bottom, cell.Bottom);
            pixels += cell.Pixels;
        }

        // Rank is filled in once the regions are in order.
        return new ChangeRegion(0, mask.PaperFor(left, top, right, bottom), pixels);
    }

    /// <summary>
    /// One cell of the mask: where it sits on the cell grid, which is what gets
    /// clustered, and which pixels inside it changed, which is what gets reported.
    /// </summary>
    private sealed class Cell : IPointData
    {
        public Cell(int cellColumn, int cellRow, int column, int row)
        {
            Point = new Point(cellColumn, cellRow);
            Left = Right = column;
            Top = Bottom = row;
            Pixels = 1;
        }

        public Point Point { get; }

        public int Left { get; private set; }

        public int Top { get; private set; }

        public int Right { get; private set; }

        public int Bottom { get; private set; }

        public int Pixels { get; private set; }

        public void Add(int column, int row)
        {
            if (column < Left) Left = column;
            if (column > Right) Right = column;
            if (row < Top) Top = row;
            if (row > Bottom) Bottom = row;
            Pixels++;
        }
    }
}
