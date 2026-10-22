# Öffentliche API verfolgen und die Kriterien für 1.0

Die öffentliche API der vier Bibliotheken wird jetzt **in Dateien verfolgt** und bei jedem Build geprüft, und die Bedingungen, unter denen die Version 1.0 werden darf, sind aufgeschrieben. Bis dahin ist die Bibliothek eine Vorschau: Die API darf sich noch ändern, aber nur mit Absicht und sichtbar.

*English version: [0028-oeffentliche-api-und-kriterien-fuer-1-0.md](0028-oeffentliche-api-und-kriterien-fuer-1-0.md)*

## Consequences

- **PublicApiAnalyzers.** `Microsoft.CodeAnalysis.PublicApiAnalyzers` wird von `SpatialNET.Core`, `.Extensions`, `.Imp` und `.IO` referenziert (Entwicklungsabhängigkeit, nicht Teil der Pakete). Jedes Projekt hat `PublicAPI.Shipped.txt` (die API der veröffentlichten Versionen) und `PublicAPI.Unshipped.txt` (die Änderungen seither). Ein öffentlicher Typ oder Member, der in keiner der beiden Dateien steht (RS0016), oder ein aufgeführter, den es nicht mehr gibt (RS0017), lässt den Build scheitern, weil Warnungen Fehler sind. Das fängt eine API-Änderung, die niemand wollte, schon im Pull Request.
- **Zusammen mit der Paketvalidierung.** `EnablePackageValidation` bleibt: Sie vergleicht die *gepackte Binärdatei* mit dem veröffentlichten Paket und fängt auch Verhalten des Compilers (etwa einen geänderten Standardwert). Die Analyzer vergleichen den *Quelltext* mit den Dateien im Repository, brauchen kein Netz und zeigen die Änderung im Diff. Die zwei Prüfungen beantworten verschiedene Fragen.
- **Ausgangslage.** `PublicAPI.Shipped.txt` enthält die öffentliche API des Tags `v0.4.0-preview.1`, gewonnen aus einem `git archive` des Tags (der Checkout wurde nicht angefasst). Alles seither Hinzugekommene (vorbereitete Distanz, `LineMerge`, `Polygonize`, Transformationen, Formen, Bereinigen) steht in `PublicAPI.Unshipped.txt`.
- **Regeln.** RS0016, RS0017 und die übrigen Regeln des Pakets sind Fehler. RS0026 und RS0027 (mehrere Überladungen mit optionalen Parametern) sind in `Directory.Build.props` abgeschaltet, weil die typisierten Überladungen mit dem gemeinsamen Ende `factory = null, gridSize = null` Absicht sind (ADR 0012).
- **Arbeiten damit.** Eine neue öffentliche API trägt der Code-Fix des Analyzers in der IDE in `PublicAPI.Unshipped.txt` ein, oder auf der Kommandozeile `dotnet format analyzers src/<projekt> --diagnostics RS0016 RS0017 --severity info`. Eine Entfernung oder Änderung erscheint als `*REMOVED*` mit der alten Zeile dahinter. Beim Vorbereiten eines Releases schiebt `tools/ship-public-api.ps1` die Einträge aus Unshipped in die Shipped-Dateien (RELEASING.md).
- **Oberflächen-Review (beim Anlegen der Dateien gemacht; noch nichts geändert).** Dinge, die ein Prüfer der gesamten öffentlichen Fläche vor 1.0 entscheiden sollte:
  - `Hull` enthält nur eine Methode (`ConvexHull`), während die übrigen Formkonstruktionen in `Shapes` stehen; man könnte sie zusammenlegen. Aufrufer nutzen die Erweiterungs-Syntax, der Klassenname spielt kaum eine Rolle, aber die API-Dokumentation listet Klassen.
  - `Circle` ist ein Werttyp in `SpatialNET.Extensions`, während die anderen Werttypen (`Coordinate`, `Envelope2D`) in `SpatialNET.Core` stehen. Er wird nur von `MinimumBoundingCircle` geliefert; er sollte nur dann nach Core wandern, wenn Hosts ihn herumreichen müssen.
  - `Location` (ein Enum von `Locate`) und `Locations` (die Erweiterungsklasse) sind zwei Namen für ein Thema; dasselbe gilt für `Validation` / `ValidationResult` und `Preparation` / `PreparedGeometry`. Das Muster ist einheitlich (Erweiterungsklasse im Plural oder als Verlaufsform, Ergebnistyp im Singular), aber nicht offensichtlich.
  - Die Reihenfolge der optionalen Parameter ist nicht einheitlich: `Simplify(tolerance, preserveTopology, factory, gridSize)`, `Densify(maxSegmentLength, factory)`, `Buffer(distance, options, factory, gridSize)`. Das Ende `factory, gridSize` ist überall gleich, und das zählt für Aufrufstellen mit benannten Argumenten.
  - `SpatialNET.Imp` legt seine konkreten Klassen (`Point`, `Path`, `Ring`, `Polygon`, ...) unter kurzen, allgemeinen Namen offen. Sie liegen in einem eigenen Namespace, aber ein Host, der `SpatialNET.Imp` neben seinem eigenen Namespace importiert, stößt auf Namenskonflikte; ob sie hinter `ImpGeometryFactory` `internal` sein sollten, ist eine Frage für 1.0.
  - `ValidationResult` und `ValidationIssue` sind Records mit öffentlichen Konstruktoren, die ein Host nie aufruft; sie könnten nur von der Bibliothek konstruierbar sein.
- **Die Kriterien für 1.0.** Die Version 1.0.0 wird veröffentlicht, wenn alles Folgende gilt:
  1. **Umfang entschieden.** Kein Kandidat für ein Feature ist offen, das die API ändern würde (die Entscheidung, es wegzulassen, zählt); `ConcaveHull` ist so oder so entschieden.
  2. **Oberfläche geprüft.** Der Review oben ist gemacht, und was er fand, ist entweder geändert oder hier für endgültig erklärt.
  3. **Verträge endgültig.** Die Interfaces von `SpatialNET.Core` (`IGeometryFactory` eingeschlossen) und die Exception-Regeln (ADR 0012) sind das, worauf sich ein Host für die ganze 1.x-Linie verlassen kann. Alles, was noch zu einem Interface hinzukommen könnte, ist schon ein Default-Interface-Member.
  4. **Ein echter Host läuft.** Mindestens eine echte GIS-Anwendung (gView) nutzt die Bibliothek über ihre eigenen Geometrieklassen und ihre eigene Factory, und ihre Befunde sind behoben.
  5. **Qualität ist belegt.** Die wöchentlichen Stresstests liefen vier Wochen in Folge grün, die Orakeltests (NTS) sind auf beiden Betriebssystemen grün, und die Benchmarks haben für die wichtigsten Algorithmen eine festgehaltene Basis.
  6. **Das Veröffentlichen ist geprobt.** Der Release-Workflow hat mindestens zwei Vorabversionen auf nuget.org veröffentlicht, und die Basis der Paketvalidierung ist das vorherige Release.
  7. **Dokumentation vollständig.** Jeder öffentliche Member hat einen XML-Kommentar, jedes Konzept eine Seite in beiden Sprachen, und der Leitfaden zur Host-Anbindung und die Rezepte laufen gegen die endgültige API.
  8. **Keine offenen Vorschau-Hinweise.** Der CHANGELOG hat keinen Eintrag, der „Vorschau“, „kann sich ändern“ oder „experimentell“ sagt.
- **Nach 1.0.** Semantic Versioning: Kein Bruch in 1.x; eine entfernte oder geänderte API braucht eine neue Hauptversion, der mindestens eine Nebenversion mit `[Obsolete]` vorausgeht. Neue API kommt mit einer Nebenversion. RELEASING.md und die Paketvalidierung erzwingen die Mechanik schon.
- **Vor 1.0 (jetzt).** Eine Nebenversion darf brechen, mit einem Eintrag im CHANGELOG unter „Changed“ oder „Removed“, der das sagt. Eine Patch-Version bricht nie.

## Considered Options

- Nur die Paketvalidierung (verworfen: Sie braucht eine veröffentlichte Basis und das Netz, zeigt Brüche erst beim Packen und nicht die gewollten Ergänzungen)
- Eine handgeschriebene API-Liste in der Dokumentation (verworfen: Sie driftet; die Analyzer halten die Liste bei jedem Build wahr)
- RS0026/RS0027 als Warnungen (verworfen: Mit Warnungen als Fehlern lassen sie den Build scheitern; das Muster ist Absicht)
- Den jetzigen Stand als Beginn von „shipped“ behandeln (verworfen: Das Tag ist die Wahrheit darüber, was Nutzer haben; die späteren Ergänzungen sind unveröffentlicht und gehören in die Unshipped-Datei)
- Ein festes Datum oder eine feste Version für 1.0 (verworfen: Die Kriterien entscheiden, nicht der Kalender)
