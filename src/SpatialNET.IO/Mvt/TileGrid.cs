namespace SpatialNET.IO;

/// <summary>The corner of the grid origin, and with it the direction in which rows count.</summary>
public enum TileOrigin
{
    /// <summary>The origin is the upper-left corner; row 0 is the top row and rows count downwards (XYZ / WMTS).</summary>
    UpperLeft,

    /// <summary>The origin is the lower-left corner; row 0 is the bottom row and rows count upwards (TMS).</summary>
    LowerLeft,
}

/// <summary>
/// A tile grid in any coordinate reference system: origin, origin corner, tile size in pixels and one resolution
/// (world units per pixel) per level. The grid only does arithmetic; it never projects.
/// </summary>
public sealed class TileGrid
{
    /// <summary>The meters per inch used to convert between scale and resolution (OGC: 0.0254).</summary>
    public const double MetersPerInch = 0.0254;

    /// <summary>The half extent of the WebMercator (EPSG:3857) world: 20037508.342789244 m.</summary>
    public const double WebMercatorHalfExtent = 20037508.342789244;

    private readonly double[] _resolutions;

    /// <summary>Creates a grid.</summary>
    /// <param name="originX">The X coordinate of the origin.</param>
    /// <param name="originY">The Y coordinate of the origin.</param>
    /// <param name="resolutions">World units per pixel per level, level 0 first.</param>
    /// <param name="tileWidth">The tile width in pixels; default 256.</param>
    /// <param name="tileHeight">The tile height in pixels; default 256.</param>
    /// <param name="origin">The origin corner; default <see cref="TileOrigin.UpperLeft"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resolutions"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The origin is not finite, a resolution is not finite and positive, or a tile size is not positive.</exception>
    public TileGrid(double originX, double originY, IEnumerable<double> resolutions, int tileWidth = 256, int tileHeight = 256, TileOrigin origin = TileOrigin.UpperLeft)
    {
        ArgumentNullException.ThrowIfNull(resolutions);
        if (!double.IsFinite(originX) || !double.IsFinite(originY))
        {
            throw new ArgumentOutOfRangeException(nameof(originX), "The origin must be finite.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileHeight);
        _resolutions = resolutions.ToArray();
        if (_resolutions.Any(r => !double.IsFinite(r) || r <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(resolutions), "Every resolution must be finite and greater than 0.");
        }

        OriginX = originX;
        OriginY = originY;
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        Origin = origin;
    }

    /// <summary>Creates a grid from map scales (as tile service metadata usually lists them).</summary>
    /// <param name="originX">The X coordinate of the origin.</param>
    /// <param name="originY">The Y coordinate of the origin.</param>
    /// <param name="scales">The scale denominators per level, level 0 first.</param>
    /// <param name="dpi">The dots per inch the scales refer to; default 96.</param>
    /// <param name="tileWidth">The tile width in pixels; default 256.</param>
    /// <param name="tileHeight">The tile height in pixels; default 256.</param>
    /// <param name="origin">The origin corner; default <see cref="TileOrigin.UpperLeft"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scales"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range (see the constructor), or <paramref name="dpi"/> is not finite and positive.</exception>
    public static TileGrid FromScales(double originX, double originY, IEnumerable<double> scales, double dpi = 96, int tileWidth = 256, int tileHeight = 256, TileOrigin origin = TileOrigin.UpperLeft)
    {
        ArgumentNullException.ThrowIfNull(scales);
        EnsureDpi(dpi);
        return new TileGrid(originX, originY, scales.Select(s => ScaleToResolution(s, dpi)).ToArray(), tileWidth, tileHeight, origin);
    }

    /// <summary>
    /// The standard WebMercator grid (EPSG:3857, Google/OSM/Mapbox XYZ): origin (-20037508.342789244, 20037508.342789244) upper left,
    /// 256 px tiles, level z has 2^z × 2^z tiles. Level = zoom, row = y, column = x.
    /// </summary>
    /// <param name="maxZoom">The highest zoom level; default 24.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxZoom"/> is not in 0..30.</exception>
    public static TileGrid WebMercator(int maxZoom = 24)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxZoom);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxZoom, 30);
        return new TileGrid(-WebMercatorHalfExtent, WebMercatorHalfExtent, Enumerable.Range(0, maxZoom + 1).Select(WebMercatorResolution));
    }

    /// <summary>The X coordinate of the origin.</summary>
    public double OriginX { get; }

    /// <summary>The Y coordinate of the origin.</summary>
    public double OriginY { get; }

    /// <summary>The tile width in pixels.</summary>
    public int TileWidth { get; }

    /// <summary>The tile height in pixels.</summary>
    public int TileHeight { get; }

    /// <summary>The origin corner.</summary>
    public TileOrigin Origin { get; }

    /// <summary>The resolutions (world units per pixel) per level.</summary>
    public IReadOnlyList<double> Resolutions => _resolutions;

    /// <summary>The number of levels.</summary>
    public int LevelCount => _resolutions.Length;

    /// <summary>The resolution of a level.</summary>
    /// <param name="level">The level.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not a level of the grid.</exception>
    public double Resolution(int level)
    {
        EnsureLevel(level);
        return _resolutions[level];
    }

    /// <summary>The scale denominator of a level.</summary>
    /// <param name="level">The level.</param>
    /// <param name="dpi">The dots per inch; default 96.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not a level of the grid, or <paramref name="dpi"/> is invalid.</exception>
    public double Scale(int level, double dpi = 96) => ResolutionToScale(Resolution(level), dpi);

    /// <summary>The world envelope of a tile.</summary>
    /// <param name="level">The level.</param>
    /// <param name="row">The row, counted from the origin corner.</param>
    /// <param name="column">The column, counted from the origin to the right.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not a level of the grid.</exception>
    public Envelope2D TileEnvelope(int level, long row, long column)
    {
        double res = Resolution(level);
        double w = res * TileWidth, h = res * TileHeight;
        double minX = OriginX + column * w;
        return Origin == TileOrigin.UpperLeft
            ? new Envelope2D(minX, OriginY - (row + 1) * h, minX + w, OriginY - row * h)
            : new Envelope2D(minX, OriginY + row * h, minX + w, OriginY + (row + 1) * h);
    }

    /// <summary>The tile that contains a world coordinate; a point on a tile border belongs to the tile right of / away from the origin.</summary>
    /// <param name="level">The level.</param>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not a level of the grid.</exception>
    public (long Row, long Column) TileAt(int level, double x, double y)
    {
        double res = Resolution(level);
        double w = res * TileWidth, h = res * TileHeight;
        long column = (long)Math.Floor((x - OriginX) / w);
        long row = (long)Math.Floor((Origin == TileOrigin.UpperLeft ? OriginY - y : y - OriginY) / h);
        return (row, column);
    }

    /// <summary>The inclusive row and column range of the tiles that meet a world envelope.</summary>
    /// <param name="level">The level.</param>
    /// <param name="envelope">The envelope; must not be empty.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not a level of the grid, or the envelope is empty.</exception>
    public (long MinRow, long MinColumn, long MaxRow, long MaxColumn) TileRange(int level, Envelope2D envelope)
    {
        if (envelope.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(envelope), "The envelope is empty.");
        }

        var a = TileAt(level, envelope.MinX, envelope.MinY);
        var b = TileAt(level, envelope.MaxX, envelope.MaxY);
        return (Math.Min(a.Row, b.Row), Math.Min(a.Column, b.Column), Math.Max(a.Row, b.Row), Math.Max(a.Column, b.Column));
    }

    /// <summary>
    /// Whether a level of this grid is a standard WebMercator zoom level: same origin (upper left), 256 px tiles and the resolution
    /// of zoom <paramref name="zoom"/>. Tiles of such a level can be served under that z/x/y unchanged.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <param name="zoom">The WebMercator zoom, or -1.</param>
    /// <param name="relativeTolerance">The allowed relative difference of origin and resolution; default 1e-6.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not a level of the grid.</exception>
    public bool TryGetWebMercatorZoom(int level, out int zoom, double relativeTolerance = 1e-6)
    {
        double res = Resolution(level);
        zoom = -1;
        bool sameLayout = Origin == TileOrigin.UpperLeft && TileWidth == 256 && TileHeight == 256 &&
            Math.Abs(OriginX + WebMercatorHalfExtent) <= WebMercatorHalfExtent * relativeTolerance &&
            Math.Abs(OriginY - WebMercatorHalfExtent) <= WebMercatorHalfExtent * relativeTolerance;
        return sameLayout && TryGetWebMercatorZoomForResolution(res, out zoom, relativeTolerance);
    }

    /// <summary>The resolution of a WebMercator zoom level: 2 · 20037508.342789244 / 256 / 2^zoom.</summary>
    /// <param name="zoom">The zoom level, ≥ 0.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zoom"/> is negative.</exception>
    public static double WebMercatorResolution(int zoom)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(zoom);
        return 2 * WebMercatorHalfExtent / 256 / Math.Pow(2, zoom);
    }

    /// <summary>Whether a resolution matches a WebMercator zoom level (0..30).</summary>
    /// <param name="resolution">The resolution in meters per pixel.</param>
    /// <param name="zoom">The zoom level, or -1.</param>
    /// <param name="relativeTolerance">The allowed relative difference; default 1e-6.</param>
    public static bool TryGetWebMercatorZoomForResolution(double resolution, out int zoom, double relativeTolerance = 1e-6)
    {
        zoom = -1;
        if (!double.IsFinite(resolution) || resolution <= 0)
        {
            return false;
        }

        int candidate = (int)Math.Round(Math.Log2(WebMercatorResolution(0) / resolution));
        if (candidate < 0 || candidate > 30 ||
            Math.Abs(resolution - WebMercatorResolution(candidate)) > WebMercatorResolution(candidate) * relativeTolerance)
        {
            return false;
        }

        zoom = candidate;
        return true;
    }

    /// <summary>
    /// Whether a map scale matches a WebMercator zoom level (0..30). The scale is that of the projected plane (as tile services
    /// state it, e.g. 1:591657527.59 for zoom 0 at 96 dpi), not the true scale at a latitude.
    /// </summary>
    /// <param name="scale">The scale denominator.</param>
    /// <param name="zoom">The zoom level, or -1.</param>
    /// <param name="dpi">The dots per inch; default 96.</param>
    /// <param name="relativeTolerance">The allowed relative difference; default 1e-6. Use a larger one for rounded scales.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dpi"/> is not finite and positive.</exception>
    public static bool TryGetWebMercatorZoom(double scale, out int zoom, double dpi = 96, double relativeTolerance = 1e-6) =>
        TryGetWebMercatorZoomForResolution(ScaleToResolution(scale, dpi), out zoom, relativeTolerance);

    /// <summary>Converts a scale denominator into a resolution: scale · 0.0254 / dpi.</summary>
    /// <param name="scale">The scale denominator.</param>
    /// <param name="dpi">The dots per inch; default 96.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dpi"/> is not finite and positive.</exception>
    public static double ScaleToResolution(double scale, double dpi = 96)
    {
        EnsureDpi(dpi);
        return scale * MetersPerInch / dpi;
    }

    /// <summary>Converts a resolution into a scale denominator: resolution · dpi / 0.0254.</summary>
    /// <param name="resolution">The resolution in world units (meters) per pixel.</param>
    /// <param name="dpi">The dots per inch; default 96.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dpi"/> is not finite and positive.</exception>
    public static double ResolutionToScale(double resolution, double dpi = 96)
    {
        EnsureDpi(dpi);
        return resolution * dpi / MetersPerInch;
    }

    private static void EnsureDpi(double dpi)
    {
        if (!double.IsFinite(dpi) || dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "The dpi must be finite and greater than 0.");
        }
    }

    private void EnsureLevel(int level)
    {
        if ((uint)level >= (uint)_resolutions.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(level), $"The grid has the levels 0..{_resolutions.Length - 1}.");
        }
    }
}
