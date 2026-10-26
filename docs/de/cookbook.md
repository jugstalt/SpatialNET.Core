# Rezepte

Kurze Rezepte für das, was Hosts täglich tun. Jedes ist ein vollständiges, lauffähiges Fragment (sie werden im Testprojekt übersetzt und geprüft) und nennt die Seite, die die Idee dahinter erklärt. Sie setzen die Einrichtung aus der [Host-Anbindung](host-integration.md) voraus: Die Host-Klassen sind vorhanden und `GeometryFactory.Default` ist gesetzt; hier stehen die mitgelieferten `SpatialNET.Imp`-Klassen und WKT stellvertretend für Ihre Daten.

## Welches Flurstück liegt unter dem Klick?

Ein räumlicher Index findet Kandidaten über die Envelope; der genaue Test entscheidet. Dasselbe Muster beantwortet „alles im Abstand von einem Punkt“: die Envelope aufweiten, dann genau testen ([Räumlicher Index](concepts/spatial-index.md)).

```csharp
var parcels = new Dictionary<string, IPolygon2D>
{
    ["A-1"] = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
    ["A-2"] = Wkt.Read<IPolygon2D>("POLYGON((10 0, 20 0, 20 10, 10 10, 10 0))"),
};

// Build once (the index never changes); the items are the dictionary entries.
var index = parcels.ToSpatialIndex(p => p.Value.Envelope());

// Which parcel is under the click? The index returns envelope candidates, Locate decides.
var click = new Coordinate(14, 3);
var hit = index.Query(new Envelope2D(click.X, click.Y, click.X, click.Y))
    .First(p => p.Value.Locate(click) != Location.Outside);

Console.WriteLine(hit.Key); // A-2

// Everything within 6 map units of a well: widen the envelope, then test exactly.
var well = Wkt.Read<IPoint2D>("POINT(24 5)");
var near = index.Query(new Envelope2D(well.X - 6, well.Y - 6, well.X + 6, well.Y + 6))
    .Where(p => p.Value.IsWithinDistance(well, 6))
    .Select(p => p.Key);

Console.WriteLine(string.Join(",", near)); // A-2

```

## Ein Polygon, viele Punkte

Tausende Punkte gegen ein großes Polygon zu testen ist der Zweck von `Prepare()`. Die vorbereitete Geometrie ist thread-sicher: einmal bauen und von allen Threads nutzen lassen ([Prädikate](concepts/predicates.md), [ADR 0024](../adr/0024-prepared-geometry-thread-sicher-und-distanz.de.md)).

```csharp
var district = Wkt.Read<IPolygon2D>("POLYGON((0 0, 100 0, 100 100, 0 100, 0 0))");
var points = Enumerable.Range(0, 1000).Select(i => new Coordinate(i % 40 * 5, i / 40 * 5)).ToArray();

// Prepare once; a PreparedGeometry is thread-safe, so the threads share it.
PreparedGeometry prepared = district.Prepare();
int inside = points.AsParallel().Count(p => prepared.Locate(p) == Location.Inside);

Console.WriteLine(inside); // points strictly inside, not on the boundary

```

## Räumlicher Verschnitt mit Flächenanteilen

`Join` paart die Elemente zweier Indizes, deren Envelopes sich treffen, und überspringt ganze Teilbäume, die sich nicht treffen können; danach testen Sie genau und rechnen, was Sie brauchen, hier wie viel jedes Flurstücks in einer Überschwemmungszone liegt ([Räumlicher Index](concepts/spatial-index.md), [Overlays](concepts/overlay-and-grid.md)).

```csharp
var parcels = new[]
{
    Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
    Wkt.Read<IPolygon2D>("POLYGON((20 0, 30 0, 30 10, 20 10, 20 0))"),
}.ToSpatialIndex();
var floodZones = new[]
{
    Wkt.Read<IPolygon2D>("POLYGON((5 -5, 15 -5, 15 5, 5 5, 5 -5))"),
}.ToSpatialIndex();

// Join walks both trees together and gives envelope pairs; the exact test and the overlay are yours.
var shares = parcels.Join(floodZones)
    .Where(pair => pair.Left.Intersects(pair.Right))
    .Select(pair => (Parcel: pair.Left, Share: pair.Left.Intersection(pair.Right).Area() / pair.Left.Area()))
    .ToList();

Console.WriteLine(shares.Count);   // 1: the second parcel is nowhere near the zone
Console.WriteLine(shares[0].Share); // 0.25

```

## Nach Attribut verschmelzen

Erst gruppieren, dann je Gruppe `UnionAll`. Es verschmilzt viele Polygone in einem Zug, nicht als Schleife paarweiser Vereinigungen ([Overlays](concepts/overlay-and-grid.md)).

```csharp
var landUse = new (string Use, string Wkt)[]
{
    ("forest", "POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))"),
    ("forest", "POLYGON((10 0, 20 0, 20 10, 10 10, 10 0))"),
    ("field", "POLYGON((0 20, 10 20, 10 30, 0 30, 0 20))"),
};

// One union per key; neighbours that share an edge become one polygon.
Dictionary<string, IPolygon2D> dissolved = landUse
    .GroupBy(x => x.Use)
    .ToDictionary(g => g.Key, g => g.Select(x => Wkt.Read<IPolygon2D>(x.Wkt)).UnionAll());

Console.WriteLine(dissolved["forest"].Area()); // 200
Console.WriteLine(dissolved["forest"].RingCount); // 1: one outline, the shared edge is gone

```

## Ein gezeichnetes Polygon reparieren

`Validate` sagt, was falsch ist und wo; `MakeValid` repariert es nach der Even-Odd-Regel. Prüfen Sie zuerst und lassen Sie gültige Eingaben, wie sie sind: `MakeValid` liefert immer ein neues Polygon auf dem Raster ([Vereinfachen, konvexe Hülle und Validierung](concepts/simplify-and-validation.md)).

```csharp
// A user drew a bow tie: the outline crosses itself.
var drawn = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 10, 10 0, 0 10, 0 0))");

ValidationResult check = drawn.Validate();
foreach (var issue in check.Issues)
{
    Console.WriteLine($"{issue.Kind} at {issue.Location.X} {issue.Location.Y}"); // SelfIntersection at 5 5
}

// MakeValid resolves it under the even-odd rule: two triangles that touch in a point.
IPolygon2D repaired = check.IsValid ? drawn : drawn.MakeValid();
Console.WriteLine(repaired.IsValid()); // True
Console.WriteLine(repaired.Area());    // 50

```

## Ein Flurstück teilen

`Cut` liefert die Teile als flache Liste. Ein Teil, den die Linie nicht erreicht, kommt unversehrt zurück ([Cut](concepts/cut.md)).

```csharp
var parcel = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");
var fence = Wkt.Read<IPolyline2D>("LINESTRING(4 -1, 4 11)");

// The pieces come as a flat list, in no left/right order.
IReadOnlyList<IPolygon2D> pieces = parcel.Cut(fence);

Console.WriteLine(string.Join(" and ", pieces.Select(p => p.Area()).Order())); // 40 and 60

```

## Von Linien zu Flächen

`LineMerge` verbindet Stücke, die Ende an Ende liegen, zu längeren Pfaden; `Polygonize` macht aus Linien die Flächen, die sie umschließen. Linien, die nur herausragen, umschließen nichts und entfallen ([Overlays](concepts/overlay-and-grid.md), [Maße und Konstruktionen](concepts/measures-and-constructions.md)).

```csharp
// A road network digitised in pieces: the pieces only touch at their ends.
var pieces = Wkt.Read<IPolyline2D>("MULTILINESTRING((0 0, 5 0), (5 0, 10 5), (10 5, 20 5))");
IPolyline2D road = pieces.LineMerge();
Console.WriteLine(road.PathCount); // 1: three pieces joined into one path

// A closed outline with a line across it: the lines enclose two areas.
var fences = Wkt.Read<IPolyline2D>(
    "MULTILINESTRING((0 0, 10 0, 10 10, 0 10, 0 0), (5 -5, 5 15))");
IReadOnlyList<IPolygon2D> fields = fences.Polygonize();
Console.WriteLine(fields.Count); // 2: the ends of the crossing line outside the outline are dropped

```

## Stationierung entlang einer Straße

Position entlang einer Linie, nächste Position zu einem Punkt und das Stück zwischen zwei Positionen. Mit M-Werten gibt es dieselben drei nach Messwert statt nach Länge ([Lineare Referenzierung](concepts/linear-referencing.md)).

```csharp
var road = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 100 0, 100 50)");

Coordinate? at = road.Interpolate(120);               // (100 20): 120 along the road
double chainage = road.Project(new Coordinate(40, 7)); // 40: where the nearest point of the road lies
IPolyline2D section = road.Substring(50, 120);          // from (50 0) via (100 0) to (100 20)

Console.WriteLine($"{at!.Value.X} {at.Value.Y}, {chainage}, {section.Length()}"); // 100 20, 40, 70

```

## Eine Straße mit Kilometersteinen

Die Straße hat kein M, und die Messwerte sitzen auf Punkten entlang der Linie. Einmal kalibrieren, dann die drei Fragen eines Routensystems beantworten: Welchen Messwert hat ein Klick, wo liegt ein Messwert, und welche Strecke liegt zwischen zwei Messwerten ([Lineare Referenzierung](concepts/linear-referencing.md)).

```csharp
var road = Wkt.Read<IPolyline2D>("LINESTRING(0 0, 100 0, 100 50)");
var posts = new[]
{
    new Coordinate(0, 0, M: 12000), new Coordinate(100, 0, M: 12200), new Coordinate(100, 50, M: 12300),
};

// Once, when the road is loaded: the posts become M on the line.
IPolyline2D route = road.CalibrateM(posts, maxSnapDistance: 0.5);

double measure = route.ProjectToM(new Coordinate(40, 7));      // 12080: a click becomes a measure
IMultiPoint2D at = route.LocateAlong(12_150);                   // (75 0): a measure becomes a point
IPolyline2D section = route.LocateBetween(12_100, 12_260);      // (50 0) (100 0) (100 30): a range becomes a stretch

Console.WriteLine($"{measure}, {at[0].X} {at[0].Y}, {section.Length()}"); // 12080, 75 0, 80

```

## Tempolimit und Belag entlang einer Route

Zwei Ereignistabellen einer Route (ein Tempolimit und ein Belag, je ein Bereich von M-Werten mit einem Wert) und die Frage, wo beide gelten. Die Rechnung läuft allein auf den Zahlen; die Straßengeometrie kommt erst hinzu, um das Ergebnis zu zeichnen oder zu messen. Die Zeilen kommen aus Ihrer Datenbank, die Bibliothek liest nie eine Tabelle; die letzten Zeilen gehen den umgekehrten Weg, von einem Flurstück zu seinem M-Bereich ([Ereignistabellen](concepts/event-tables.md), [Lineare Referenzierung](concepts/linear-referencing.md)).

```csharp
var route = Wkt.Read<IPolyline2D>("LINESTRING M (0 0 12000, 100 0 12200, 100 50 12300)");
var speed = new[] { new LineEvent<int>(12000, 12150, 50), new LineEvent<int>(12150, 12300, 70) };
var surface = new[] { new LineEvent<string>(12000, 12100, "asphalt"), new LineEvent<string>(12100, 12300, "gravel") };

// Where do both hold? Pure interval arithmetic on the measures, no geometry yet.
var both = LineEvents.Intersect(speed, surface, (s, b) => $"{s} km/h, {b}");

foreach (var piece in both)
{
    // Only now the stretch of road: its shape, in the direction of the route.
    IPolyline2D shape = route.LocateBetween(piece.From, piece.To);

    Console.WriteLine($"{piece.Value}: {shape.Length()} m");
    // 50 km/h, asphalt: 50 m / 50 km/h, gravel: 25 m / 70 km/h, gravel: 75 m
}

// The way back: which part of the route does a parcel along the road belong to?
var parcel = Wkt.Read<IPolygon2D>("POLYGON((20 0, 50 0, 50 10, 20 10, 20 0))");
var (from, to) = route.LocateFeature(parcel, tolerance: 0).Single();
Console.WriteLine($"{Math.Round(from)} to {Math.Round(to)}"); // 12040 to 12100

```

## Einzugsgebiete: welche Station ist die nächste?

Die Voronoi-Zelle eines Punktes ist die Fläche, in der er von allen der nächste ist. Auf eine Grenze zugeschnitten, sind die Zellen Einzugsgebiete, und die Flächen sagen, wie viel jeder Punkt versorgt ([Triangulierung, Voronoi und Gelände](concepts/triangulation-and-voronoi.md)).

```csharp
var hydrants = Wkt.Read<IMultiPoint2D>("MULTIPOINT ((10 20), (35 80), (60 30), (85 70), (50 55))");
var district = Wkt.Read<IPolygon2D>("POLYGON ((0 0, 100 0, 100 45, 60 45, 60 100, 0 100, 0 0))");

// One cell per hydrant: where it is the nearest one, cut to the district.
var cells = hydrants.VoronoiDiagram(district);

// Which hydrant serves a point? The one whose cell contains it.
var served = cells.First(c => c.Cell.Locate(new Coordinate(20, 60)) != Location.Outside);
Console.WriteLine($"{served.Site.X} {served.Site.Y}");   // 35 80

// How much of the district does each hydrant serve?
var areas = cells.Select(c => Math.Round(c.Cell.Area())).ToArray();

```

## Höhe an einem Punkt und Höhenlinien

Eine Triangulierung von Punkten mit Z ist ein Geländemodell: Sie liefert die Höhe überall innerhalb und die Linie jeder Höhe. Bruchkanten (Rücken, Gräben) lassen sich als Zwangskanten ergänzen; siehe die Konzeptseite ([Triangulierung, Voronoi und Gelände](concepts/triangulation-and-voronoi.md)).

```csharp
var points = Wkt.Read("MULTIPOINT Z ((0 0 10), (100 0 20), (100 100 40), (0 100 30), (50 50 25))");
var terrain = Triangulation.FromPoints(points);

Console.WriteLine(terrain.InterpolateZ(new Coordinate(25, 10)));            // 14.5
foreach (double level in new[] { 20.0, 30.0 })
{
    Console.WriteLine($"{level}: {terrain.Contour(level).Length():0.0} m");   // 20: 111.8 m, 30: 111.8 m
}

```

## Höhenprofil entlang einer Straße

Über ein Geländemodell gelegt, bekommt eine Straße einen Stützpunkt überall, wo sie eine Kante der Triangulierung kreuzt, mit der Höhe als Z und dem Abstand entlang der Straße als M. Z gegen M ist das Höhenprofil ([Triangulierung, Voronoi und Gelände](concepts/triangulation-and-voronoi.md)).

```csharp
var terrain = Triangulation.FromPoints(Wkt.Read(
    "MULTIPOINT Z ((0 0 0), (100 0 10), (100 100 30), (0 100 20), (50 50 15), (50 0 5), (50 100 25))"));
var road = Wkt.Read<IPolyline2D>("LINESTRING (0 50, 100 50)");

// The road over the terrain: Z is the height, M the distance along the road.
var draped = terrain.Drape(road);

var profile = Enumerable.Range(0, draped[0].PointCount)
    .Select(i => (Distance: ((IPointM)draped[0][i]).M, Height: ((IPoint3D)draped[0][i]).Z))
    .ToArray();
foreach (var (distance, height) in profile)
{
    Console.WriteLine($"{distance:0} m: {height:0.0} m");   // 0 m: 10.0 m, ..., 100 m: 20.0 m
}

```

## Für die Karte

Für kleine Maßstäbe vereinfachen, auf den Bildschirm zuschneiden und einen guten Ort für die Beschriftung finden ([Vereinfachen, konvexe Hülle und Validierung](concepts/simplify-and-validation.md), [Overlays](concepts/overlay-and-grid.md), [Maße und Konstruktionen](concepts/measures-and-constructions.md)).

```csharp
// A wiggly shore with many vertices.
var shore = Wkt.Read<IPolygon2D>(
    "POLYGON((0 0, 25 0.2, 50 0, 75 -0.2, 100 0, 100 40, 60 40, 60 100, 40 100, 40 40, 0 40, 0 0))");

// Fewer vertices for a small scale, without crossings.
IPolygon2D light = shore.Simplify(1, preserveTopology: true);

// Only what is on screen.
IGeometry2D visible = shore.Clip(new Envelope2D(-10, -10, 120, 50));

// Where to put the name: inside, and as far from the edge as possible.
Coordinate? label = shore.Polylabel();

Console.WriteLine(light.AsList().Sum(r => r.PointCount) < shore.AsList().Sum(r => r.PointCount)); // True

```

## Austausch mit Datenbanken und Web-Clients

WKB für eine Datenbankspalte und GeoJSON für einen Web-Client schreiben und lesen. Das Lesen nimmt die Factory, die Ihre eigenen Klassen erzeugt; was Sie lesen, ist also schon eine Host-Geometrie ([Erste Schritte](getting-started.md)).

```csharp
var parcel = Wkt.Read<IPolygon2D>("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))");

// Binary for a database column, JSON for a web client; reading takes the factory that creates your classes.
byte[] wkb = parcel.ToWkb();
string json = parcel.ToGeoJson();

IPolygon2D fromDatabase = Wkb.Read<IPolygon2D>(wkb, ImpGeometryFactory.Instance);
IPolygon2D fromClient = GeoJson.Read<IPolygon2D>(json, ImpGeometryFactory.Instance);

Console.WriteLine(fromDatabase.EqualsTopo(fromClient)); // True

```
