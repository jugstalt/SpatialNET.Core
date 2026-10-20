// PROTOTYPE – absichtlich nicht kompilierende Fälle. Nur mit:  dotnet build -p:ShowCollisions=true
#if SHOW_COLLISIONS
using SpatialNET.Core;
using SpatialNET.Extensions;

namespace Collisions;

static class K1_ExtensionMethodVsHostProperty
{
    // gView/WebGIS haben "double Area { get; }" auf der konkreten Klasse bzw. dem Host-Interface.
    // ERGEBNIS: kompiliert! Bei Aufruf-Syntax p.Area() blendet C# nicht aufrufbare Member (Properties) aus,
    // die Extension-Methode wird gefunden. Methoden-Form kollidiert also NICHT mit Host-Properties.
    static double A(gView.Framework.Geometry.Polygon p) => p.Area();
    static double B(E.Standard.WebMapping.Core.Geometry.Polygon p) => p.Area();
    static double C(gView.Framework.Core.Geometry.IPolygon p) => p.Area();
}

// ---- Variante B: Punktzugriff über IReadOnlyList<IPoint2D> statt Count+Indexer ----
public interface IPath2D_B : IReadOnlyList<IPoint2D> { }

// gView-PointCollection implementiert schon IEnumerable<IPoint>; IReadOnlyList<IPoint2D> bringt ein zweites IEnumerable<T> dazu.
public class GvPath_B : IEnumerable<gView.Framework.Core.Geometry.IPoint>, IPath2D_B
{
    readonly List<gView.Framework.Core.Geometry.IPoint> _p = new();
    public int PointCount => _p.Count;
    public gView.Framework.Core.Geometry.IPoint this[int i] => _p[i];
    public IEnumerator<gView.Framework.Core.Geometry.IPoint> GetEnumerator() => _p.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    // Variante B verlangt zusätzlich: Count (neuer Name neben PointCount), expliziter Indexer, zweiter Enumerator
    int IReadOnlyCollection<IPoint2D>.Count => _p.Count;
    IPoint2D IReadOnlyList<IPoint2D>.this[int i] => _p[i];
    IEnumerator<IPoint2D> IEnumerable<IPoint2D>.GetEnumerator() => _p.GetEnumerator();
}

static class K2_VariantB_Linq
{
    // ERGEBNIS: CS0411 – Typinferenz für Select scheitert, weil GvPath_B zwei IEnumerable<T> hat.
    static IEnumerable<double> Xs(GvPath_B path) => path.Select(p => p.X);
}
#endif
