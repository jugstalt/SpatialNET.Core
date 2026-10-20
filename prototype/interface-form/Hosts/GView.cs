// PROTOTYPE – Spiegel von gView.Framework.Core.Geometry / gView.Framework.Geometry (nur die relevanten,
// originalgetreuen Member-Signaturen). Markiert mit  // +SpatialNET  ist alles, was gView ergänzen müsste.
//
// Strategie gView: SpatialNET-Interfaces auf INTERFACE-Ebene anhängen; die nötigen expliziten
// Implementierungen stehen als Default-Interface-Member in gViews eigenen Interfaces.
// Die Klassen (Point, Path, Ring, Polygon …) bleiben unverändert.

using System.Collections;
using SpatialNET.Core;

namespace gView.Framework.Core.Geometry
{
    public interface IEnvelope : IGeometry
    {
        double MinX { get; set; }
        double MinY { get; set; }
        double MaxX { get; set; }
        double MaxY { get; set; }
    }

    public interface IGeometry
    {
        IEnvelope Envelope { get; }     // Kollidiert NICHT, weil SpatialNET kein Envelope-Property definiert
        int VertexCount { get; }
        bool IsEmpty();                 // WebGIS hat IsEmpty als Property -> SpatialNET definiert kein IsEmpty
    }

    public interface IPoint : IGeometry, IPoint3D                      // +SpatialNET
    {
        new double X { get; set; }                                       // +SpatialNET: "new" (sonst Warnung CS0108)
        new double Y { get; set; }                                       // +SpatialNET
        new double Z { get; set; }                                       // +SpatialNET
        double M { get; set; }
        double Distance(IPoint p);
    }

    public interface IPointCollection : IEnumerable<IPoint>, IPointCollection2D        // +SpatialNET
    {
        void AddPoint(IPoint point);
        new int PointCount { get; }                                      // +SpatialNET: "new"
        new IPoint this[int pointIndex] { get; }                         // +SpatialNET: "new"
        IEnvelope Envelope { get; }

        IPoint2D IPointCollection2D.this[int index] => this[index];      // +SpatialNET (explizit, Default-Member)
    }

    public interface IPath : IPointCollection, IPath2D                  // +SpatialNET
    {
        double Length { get; }
    }

    public interface IRing : IPath, IRing2D                             // +SpatialNET
    {
        double Area { get; }
        IPoint Centroid { get; }
    }

    public interface IHole : IRing { }

    public interface IMultiPoint : IGeometry, IPointCollection, IMultiPoint2D   // +SpatialNET
    {
    }

    public interface IPolyline : IGeometry, IEnumerable<IPath>, IPolyline2D     // +SpatialNET
    {
        void AddPath(IPath path);
        new int PathCount { get; }                                       // +SpatialNET: "new"
        new IPath this[int pathIndex] { get; }                           // +SpatialNET: "new"
        double Length { get; }

        IPath2D IPolyline2D.this[int index] => this[index];              // +SpatialNET
    }

    public interface IPolygon : IGeometry, IEnumerable<IRing>, IPolygon2D       // +SpatialNET
    {
        void AddRing(IRing ring);
        new int RingCount { get; }                                       // +SpatialNET: "new"
        new IRing this[int ringIndex] { get; }                           // +SpatialNET: "new"
        IEnumerable<IHole> Holes { get; }
        double Area { get; }

        IRing2D IPolygon2D.this[int index] => this[index];               // +SpatialNET
    }
}

namespace gView.Framework.Geometry
{
    using gView.Framework.Core.Geometry;

    // ---- Klassen: unverändert gegenüber gView (gekürzt) ----

    public sealed class Envelope : IEnvelope
    {
        public Envelope(double minX, double minY, double maxX, double maxY) { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        IEnvelope IGeometry.Envelope => this;
        public int VertexCount => 4;
        public bool IsEmpty() => false;
    }

    public class Point : IPoint
    {
        public Point(double x, double y, double z = 0) { X = x; Y = y; Z = z; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double M { get; set; }
        public double Distance(IPoint p) => Math.Sqrt((X - p.X) * (X - p.X) + (Y - p.Y) * (Y - p.Y) + (Z - p.Z) * (Z - p.Z));
        public IEnvelope Envelope => new Envelope(X, Y, X, Y);
        public int VertexCount => 1;
        public bool IsEmpty() => false;
    }

    public class PointCollection : IPointCollection
    {
        protected List<IPoint> _points = new();
        public PointCollection(params IPoint[] points) => _points.AddRange(points);
        public void AddPoint(IPoint point) => _points.Add(point);
        public int PointCount => _points.Count;
        public IPoint this[int pointIndex] => _points[pointIndex];
        public IEnvelope Envelope => new Envelope(_points.Min(p => p.X), _points.Min(p => p.Y), _points.Max(p => p.X), _points.Max(p => p.Y));
        public IEnumerator<IPoint> GetEnumerator() => _points.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class Path : PointCollection, IPath
    {
        public Path(params IPoint[] points) : base(points) { }
        public double Length => 0; // gView-eigene Berechnung (hier egal)
    }

    public class Ring : Path, IRing
    {
        public Ring(params IPoint[] points) : base(points) { }
        public double Area => 0;
        public IPoint Centroid => _points[0];
    }

    public class Hole : Ring, IHole
    {
        public Hole(params IPoint[] points) : base(points) { }
    }

    public sealed class MultiPoint : PointCollection, IMultiPoint
    {
        public MultiPoint(params IPoint[] points) : base(points) { }
        public int VertexCount => PointCount;
        public bool IsEmpty() => PointCount == 0;
    }

    public sealed class Polyline : IPolyline
    {
        private readonly List<IPath> _paths = new();
        public Polyline(params IPath[] paths) => _paths.AddRange(paths);
        public void AddPath(IPath path) => _paths.Add(path);
        public int PathCount => _paths.Count;
        public IPath this[int pathIndex] => _paths[pathIndex];
        public double Length => 0;
        public IEnvelope Envelope => null!;
        public int VertexCount => _paths.Sum(p => p.PointCount);
        public bool IsEmpty() => PathCount == 0;
        public IEnumerator<IPath> GetEnumerator() => _paths.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class Polygon : IPolygon
    {
        private readonly List<IRing> _rings = new();
        public Polygon(params IRing[] rings) => _rings.AddRange(rings);
        public void AddRing(IRing ring) => _rings.Add(ring);
        public int RingCount => _rings.Count;
        public IRing this[int ringIndex] => _rings[ringIndex];
        public IEnumerable<IHole> Holes => _rings.OfType<IHole>();
        public double Area => -1; // absichtlich falsch, um zu zeigen, WESSEN Area aufgerufen wird
        public IEnvelope Envelope => null!;
        public int VertexCount => _rings.Sum(r => r.PointCount);
        public bool IsEmpty() => RingCount == 0;
        public IEnumerator<IRing> GetEnumerator() => _rings.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
