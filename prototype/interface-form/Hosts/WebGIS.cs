// PROTOTYPE – Spiegel von E.Standard.WebMapping.Core.Geometry (nur relevante, originalgetreue Signaturen).
// Markiert mit  // +SpatialNET  ist alles, was WebGIS ergänzen müsste.
//
// Strategie WebGIS: keine eigenen Interfaces -> SpatialNET-Interfaces direkt an die Klassen,
// explizite Indexer in PointCollection / Polyline / Polygon.

using SpatialNET.Core;

namespace E.Standard.WebMapping.Core.Geometry
{
    public class Shape : IGeometry2D                                     // +SpatialNET
    {
        public bool? HasZ { get; set; }
        public virtual Envelope ShapeEnvelope => null!;
    }

    public class Envelope : Shape
    {
        public Envelope(double minx, double miny, double maxx, double maxy) { MinX = minx; MinY = miny; MaxX = maxx; MaxY = maxy; }
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
    }

    public class Point : Shape, IPoint3D                                 // +SpatialNET (implizit: X/Y/Z get;set; erfüllen get)
    {
        public Point(double x, double y, double z = 0) { X = x; Y = y; Z = z; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public bool IsEmpty => false;                                    // Property! (gView: Methode)
    }

    public class PointCollection : Shape, IPointCollection2D            // +SpatialNET
    {
        protected List<Point> _points = new();
        public PointCollection(params Point[] points) => _points.AddRange(points);
        public void AddPoint(Point point) => _points.Add(point);
        public int PointCount => _points.Count;
        public Point this[int pointIndex] => _points[pointIndex];
        // kein IEnumerable – wie im Original

        IPoint2D IPointCollection2D.this[int index] => this[index];      // +SpatialNET
    }

    public class Path : PointCollection, IPath2D                        // +SpatialNET (Indexer kommt aus PointCollection)
    {
        public Path(params Point[] points) : base(points) { }
        public double Length => 0;
    }

    public class Ring : Path, IRing2D                                   // +SpatialNET
    {
        public Ring(params Point[] points) : base(points) { }
        public double Area => 0;
    }

    public class Hole : Ring
    {
        public Hole(params Point[] points) : base(points) { }
    }

    public sealed class MultiPoint : PointCollection, IMultiPoint2D     // +SpatialNET
    {
        public MultiPoint(params Point[] points) : base(points) { }
    }

    public sealed class Polyline : Shape, IPolyline2D                   // +SpatialNET
    {
        private readonly List<Path> _paths = new();
        public Polyline(params Path[] paths) => _paths.AddRange(paths);
        public int PathCount => _paths.Count;
        public Path this[int pathIndex] => _paths[pathIndex];
        public double Length => 0;

        IPath2D IPolyline2D.this[int index] => this[index];              // +SpatialNET
    }

    public sealed class Polygon : Shape, IPolygon2D                     // +SpatialNET
    {
        private readonly List<Ring> _rings = new();
        public Polygon(params Ring[] rings) => _rings.AddRange(rings);
        public int RingCount => _rings.Count;
        public Ring this[int ringIndex] => _rings[ringIndex];
        public double Area => -1; // absichtlich falsch
        public int PointCount => _rings.Sum(r => r.PointCount);         // Achtung: gleicher Name, andere Bedeutung (Summe)

        IRing2D IPolygon2D.this[int index] => this[index];               // +SpatialNET
    }

    public sealed class AggregateShape : Shape { }
}
