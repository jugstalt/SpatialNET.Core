using SpatialNET.Internal;

namespace SpatialNET.IO;

/// <summary>Options for building vector tiles from world geometries.</summary>
public sealed record MvtTileOptions
{
    /// <summary>The default options.</summary>
    public static MvtTileOptions Default { get; } = new();

    /// <summary>The number of tile units per tile edge; default 4096.</summary>
    public uint Extent { get; init; } = MvtLayer.DefaultExtent;

    /// <summary>The buffer around the tile in tile units into which geometries are kept; default 64 (of 4096).</summary>
    public double Buffer { get; init; } = 64;

    /// <summary>
    /// The Douglas-Peucker tolerance in pixels of the tile's rendered size (<see cref="TileGrid.TileWidth"/>); default 1. 0 switches
    /// simplification off.
    /// </summary>
    public double SimplifyTolerance { get; init; } = 1;

    /// <summary>Whether simplification preserves topology (slower; see <see cref="Simplification"/>); default <c>false</c>.</summary>
    public bool PreserveTopology { get; init; }
}

/// <summary>
/// The affine mapping between the world coordinates of one tile and its tile coordinates (0..extent, y down), for any grid.
/// X maps <c>[MinX, MaxX]</c> to <c>[0, extent]</c>, Y maps <c>[MaxY, MinY]</c> to <c>[0, extent]</c>; for non-square tiles both
/// axes are scaled independently.
/// </summary>
public sealed class MvtTileTransform
{
    /// <summary>Creates the transform for a tile of a grid.</summary>
    /// <param name="grid">The tile grid.</param>
    /// <param name="level">The level.</param>
    /// <param name="row">The row.</param>
    /// <param name="column">The column.</param>
    /// <param name="extent">The number of tile units per tile edge; default 4096.</param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not a level of the grid, or <paramref name="extent"/> is 0.</exception>
    public MvtTileTransform(TileGrid grid, int level, long row, long column, uint extent = MvtLayer.DefaultExtent)
        : this((grid ?? throw new ArgumentNullException(nameof(grid))).TileEnvelope(level, row, column), extent)
    {
    }

    /// <summary>Creates the transform for a tile envelope.</summary>
    /// <param name="tileEnvelope">The world envelope of the tile; must have an area.</param>
    /// <param name="extent">The number of tile units per tile edge; default 4096.</param>
    /// <exception cref="ArgumentOutOfRangeException">The envelope has no area or is not finite, or <paramref name="extent"/> is 0.</exception>
    public MvtTileTransform(Envelope2D tileEnvelope, uint extent = MvtLayer.DefaultExtent)
    {
        if (tileEnvelope.IsEmpty || !(tileEnvelope.Width > 0) || !(tileEnvelope.Height > 0) || !double.IsFinite(tileEnvelope.Width) || !double.IsFinite(tileEnvelope.Height))
        {
            throw new ArgumentOutOfRangeException(nameof(tileEnvelope), "The tile envelope must be finite and have an area.");
        }

        ArgumentOutOfRangeException.ThrowIfZero(extent);
        TileEnvelope = tileEnvelope;
        Extent = extent;
    }

    /// <summary>The world envelope of the tile.</summary>
    public Envelope2D TileEnvelope { get; }

    /// <summary>The number of tile units per tile edge.</summary>
    public uint Extent { get; }

    /// <summary>World units per tile unit in X.</summary>
    public double UnitWidth => TileEnvelope.Width / Extent;

    /// <summary>World units per tile unit in Y.</summary>
    public double UnitHeight => TileEnvelope.Height / Extent;

    /// <summary>Converts a world coordinate into (unrounded) tile coordinates.</summary>
    /// <param name="world">The world coordinate.</param>
    public Coordinate ToTile(Coordinate world) =>
        new((world.X - TileEnvelope.MinX) / UnitWidth, (TileEnvelope.MaxY - world.Y) / UnitHeight);

    /// <summary>Converts tile coordinates into a world coordinate.</summary>
    /// <param name="x">The tile X.</param>
    /// <param name="y">The tile Y (down).</param>
    public Coordinate ToWorld(double x, double y) =>
        new(TileEnvelope.MinX + x * UnitWidth, TileEnvelope.MaxY - y * UnitHeight);

    /// <summary>The world envelope of the tile widened by <paramref name="buffer"/> tile units on each side.</summary>
    /// <param name="buffer">The buffer in tile units, ≥ 0.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="buffer"/> is negative or not finite.</exception>
    public Envelope2D BufferedEnvelope(double buffer)
    {
        if (!double.IsFinite(buffer) || buffer < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer), "The buffer must be finite and not negative.");
        }

        var e = TileEnvelope;
        double dx = buffer * UnitWidth, dy = buffer * UnitHeight;
        return new Envelope2D(e.MinX - dx, e.MinY - dy, e.MaxX + dx, e.MaxY + dy);
    }
}

/// <summary>
/// Builds one vector tile of any tile grid from world geometries: each geometry is clipped to the tile plus buffer, simplified
/// (about one pixel), transformed to tile coordinates, rounded to integers, cleaned of repeated points and degenerate parts and
/// its rings oriented as MVT requires (exterior clockwise on screen, i.e. positive area in tile coordinates with y down;
/// interior rings opposite). Multipolygons are written part by part, each exterior ring followed by its holes.
/// </summary>
/// <remarks>
/// Rounding to integers can make a valid polygon touch or cross itself at sub-unit features; this is the usual MVT trade-off and
/// renderers handle it. Layers keep the order of their first feature; layers without features are not written.
/// </remarks>
public sealed class MvtTileBuilder
{
    private readonly List<MvtLayer> _layers = [];
    private readonly MvtTileOptions _options;
    private readonly Envelope2D _clip;
    private readonly double _tolerance;

    /// <summary>Creates a builder for a tile of a grid.</summary>
    /// <param name="grid">The tile grid.</param>
    /// <param name="level">The level.</param>
    /// <param name="row">The row.</param>
    /// <param name="column">The column.</param>
    /// <param name="options">The options; <c>null</c> = <see cref="MvtTileOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not a level of the grid, or an option is out of range.</exception>
    public MvtTileBuilder(TileGrid grid, int level, long row, long column, MvtTileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        _options = options ?? MvtTileOptions.Default;
        if (!double.IsFinite(_options.SimplifyTolerance) || _options.SimplifyTolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SimplifyTolerance must be finite and not negative.");
        }

        Transform = new MvtTileTransform(grid, level, row, column, _options.Extent);
        _clip = Transform.BufferedEnvelope(_options.Buffer);
        _tolerance = _options.SimplifyTolerance * grid.Resolution(level);
    }

    /// <summary>The mapping between world and tile coordinates of this tile.</summary>
    public MvtTileTransform Transform { get; }

    /// <summary>The world envelope geometries are clipped to: the tile plus buffer.</summary>
    public Envelope2D ClipEnvelope => _clip;

    /// <summary>Whether no feature has been added yet.</summary>
    public bool IsEmpty => _layers.Count == 0;

    /// <summary>Adds a feature in world coordinates to a layer (created on first use).</summary>
    /// <param name="layerName">The layer name.</param>
    /// <param name="geometry">The geometry in world coordinates of the grid's CRS.</param>
    /// <param name="attributes">The attributes; <c>null</c> values are skipped, a repeated key keeps its first value.</param>
    /// <param name="id">The feature id (typically the object id) or <c>null</c>.</param>
    /// <returns><c>true</c> if the feature was added; <c>false</c> if nothing of the geometry is left in the tile.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="layerName"/> or <paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    public bool AddFeature(string layerName, IGeometry2D geometry, IEnumerable<KeyValuePair<string, object?>>? attributes = null, ulong? id = null)
    {
        ArgumentNullException.ThrowIfNull(layerName);
        var shape = ToTileShape(geometry);
        if (shape is null)
        {
            return false;
        }

        var layer = _layers.FirstOrDefault(l => l.Name == layerName);
        if (layer is null)
        {
            layer = new MvtLayer(layerName) { Extent = _options.Extent };
            _layers.Add(layer);
        }

        layer.Features.Add(MvtFeature.FromShape(shape, attributes, id));
        return true;
    }

    /// <summary>
    /// The tile-coordinate geometry a world geometry becomes in this tile (clipped, simplified, rounded and oriented as for
    /// <see cref="AddFeature"/>), or <c>null</c> if nothing is left.
    /// </summary>
    /// <param name="geometry">The geometry in world coordinates.</param>
    /// <param name="factory">The factory for the result; <c>null</c> = <see cref="GeometryFactory.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="geometry"/> is not a known geometry kind.</exception>
    /// <exception cref="InvalidOperationException">No factory available.</exception>
    public IGeometry2D? ToTileGeometry(IGeometry2D geometry, IGeometryFactory? factory = null)
    {
        var shape = ToTileShape(geometry);
        return shape is null ? null : MvtGeometry.Decode(shape.Type, MvtGeometry.Encode(shape), factory);
    }

    /// <summary>The tile with all non-empty layers.</summary>
    public MvtTile Build()
    {
        var tile = new MvtTile();
        foreach (var layer in _layers)
        {
            tile.Layers.Add(layer);
        }

        return tile;
    }

    /// <summary>The encoded tile.</summary>
    /// <param name="gzip">Whether to gzip the bytes; default <c>false</c>.</param>
    public byte[] ToBytes(bool gzip = false)
    {
        var bytes = Mvt.Encode(Build());
        return gzip ? Mvt.Gzip(bytes) : bytes;
    }

    private TileShape? ToTileShape(IGeometry2D geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        GeometryAccess.EnsureKnown(geometry, nameof(geometry));
        var f = CaptureFactory.Instance;
        IGeometry2D clipped;
        switch (geometry)
        {
            case IPolygon2D polygon:
                var p = polygon.Clip(_clip, f);
                clipped = _tolerance > 0 && p.RingCount > 0 ? p.Simplify(_tolerance, _options.PreserveTopology, f) : p;
                break;
            case IPolyline2D line:
                var l = line.Clip(_clip, f);
                clipped = _tolerance > 0 && l.PathCount > 0 ? l.Simplify(_tolerance, _options.PreserveTopology, f) : l;
                break;
            case IMultiPoint2D multiPoint:
                clipped = multiPoint.Clip(_clip, f);
                break;
            default:
                var point = (IPoint2D)geometry;
                if (!_clip.Contains(point.ToCoordinate()))
                {
                    return null;
                }

                clipped = point;
                break;
        }

        return TileShape.FromTileGeometry(clipped, Transform.ToTile);
    }
}

/// <summary>A minimal coordinate-backed factory for the intermediate results of clip and simplify (no Z, no M).</summary>
internal sealed class CaptureFactory : IGeometryFactory
{
    public static CaptureFactory Instance { get; } = new();

    public bool SupportsZ => false;

    public IPoint2D CreatePoint(Coordinate coordinate, IGeometry2D? source) => new PointImpl(coordinate.X, coordinate.Y);

    public IMultiPoint2D CreateMultiPoint(ReadOnlySpan<Coordinate> coordinates, IGeometry2D? source) => new MultiPointImpl(coordinates.ToArray());

    public IPolyline2D CreatePolyline(ReadOnlySpan<ReadOnlyMemory<Coordinate>> paths, IGeometry2D? source) =>
        new PolylineImpl(paths.ToArray().Select(p => new PathImpl(p.ToArray())).ToArray());

    public IPolygon2D CreatePolygon(ReadOnlySpan<PolygonPart> parts, IGeometry2D? source)
    {
        var rings = new List<PathImpl>();
        foreach (var part in parts)
        {
            rings.Add(new PathImpl(part.Shell.ToArray()));
            foreach (var hole in part.Holes.Span)
            {
                rings.Add(new PathImpl(hole.ToArray()));
            }
        }

        return new PolygonImpl(rings.ToArray());
    }

    private sealed record PointImpl(double X, double Y) : IPoint2D;

    private class PathImpl(Coordinate[] points) : IRing2D
    {
        public int PointCount => points.Length;

        public IPoint2D this[int index] => new PointImpl(points[index].X, points[index].Y);
    }

    private sealed class MultiPointImpl(Coordinate[] points) : PathImpl(points), IMultiPoint2D;

    private sealed class PolylineImpl(PathImpl[] paths) : IPolyline2D
    {
        public int PathCount => paths.Length;

        public IPath2D this[int index] => paths[index];
    }

    private sealed class PolygonImpl(PathImpl[] rings) : IPolygon2D
    {
        public int RingCount => rings.Length;

        public IRing2D this[int index] => rings[index];
    }
}
