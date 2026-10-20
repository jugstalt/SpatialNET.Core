// PROTOTYPE – dotnet run: rechnet dieselben Algorithmen auf gView- und WebGIS-Geometrien.
using SpatialNET.Core;
using SpatialNET.Extensions;
using GV = gView.Framework.Geometry;
using WG = E.Standard.WebMapping.Core.Geometry;

// Quadrat 10x10 mit Loch 2x2 (Loch ist bei gView ein Hole, bei WebGIS absichtlich ein normaler Ring)
var gvPoly = new GV.Polygon(
    new GV.Ring(new GV.Point(0, 0, 100), new GV.Point(10, 0, 110), new GV.Point(10, 10, 120), new GV.Point(0, 10, 110)),
    new GV.Hole(new GV.Point(4, 4), new GV.Point(6, 4), new GV.Point(6, 6), new GV.Point(4, 6), new GV.Point(4, 4)));
var wgPoly = new WG.Polygon(
    new WG.Ring(new WG.Point(0, 0, 100), new WG.Point(10, 0, 110), new WG.Point(10, 10, 120), new WG.Point(0, 10, 110)),
    new WG.Ring(new WG.Point(4, 4), new WG.Point(6, 4), new WG.Point(6, 6), new WG.Point(4, 6)));

var gvLine = new GV.Polyline(new GV.Path(new GV.Point(0, 0), new GV.Point(3, 4)), new GV.Path(new GV.Point(10, 10), new GV.Point(10, 20)));
var wgLine = new WG.Polyline(new WG.Path(new WG.Point(0, 0), new WG.Point(3, 4)), new WG.Path(new WG.Point(10, 10), new WG.Point(10, 20)));

Show("gView ", gvPoly, gvLine);
Show("WebGIS", wgPoly, wgLine);

Console.WriteLine();
Console.WriteLine("Namens-Shadowing (Extension-Property vs. Host-Property):");
Console.WriteLine($"  gView  Polygon.Area (Host)            = {gvPoly.Area}");
Console.WriteLine($"  gView  ((IPolygon2D)p).Area()         = {((IPolygon2D)gvPoly).Area()}");
Console.WriteLine($"  WebGIS ((IPolygon2D)p).AreaProp       = {((IPolygon2D)wgPoly).AreaProp}");
Console.WriteLine($"  Z an t=0.5 auf erster Kante (gView)   = {Measures.InterpolateZ(gvPoly[0][0], gvPoly[0][1], 0.5)}");

Console.WriteLine();
Console.WriteLine("LINQ über AsList() auf gView- und WebGIS-Typen (kein CS0411, Host unverändert):");
Console.WriteLine($"  gView  Außenring X-Werte  = {string.Join(",", gvPoly[0].AsList().Select(p => p.X))}");
Console.WriteLine($"  WebGIS Pfade mit >1 Punkt = {wgLine.AsList().Count(p => p.PointCount > 1)}");
Console.WriteLine($"  gView-eigenes LINQ auf IRing weiter ok: {gvPoly[0].Count(p => p.X > 0)}");

static void Show(string host, IPolygon2D poly, IPolyline2D line)
{
    Console.WriteLine($"{host}: Area={poly.Area()}  Envelope={poly.Envelope()}  Length={line.Length()}  " +
                      $"Ringe={poly.RingCount}  Punkt0 ist IPoint3D={poly[0][0] is IPoint3D}");
}
