# PROTOTYPE – Form der Geometrie-Interfaces (Ticket #2)

Wegwerfcode. Frage: Wie sehen die lesenden, minimalen Geometrie-Interfaces aus, und passen sie an gView und WebGIS?

- `dotnet run`: rechnet Area/Length/Envelope/Z-Interpolation und LINQ über `.AsList()` auf gView- **und** WebGIS-Geometrien.
- `dotnet build -p:ShowCollisions=true`: zeigt die Kollision der verworfenen Variante B (`IReadOnlyList<T>` → CS0411 bei LINQ auf gView-Typen).
  Fall K1 (Extension-Methode `Area()` vs. Host-Property `Area`) kompiliert, ist also **keine** Kollision.

Dateien: `Core/Interfaces.cs` (Kandidat), `Hosts/GView.cs` und `Hosts/WebGIS.cs` (Spiegel der Original-Signaturen; `// +SpatialNET` = nötige Ergänzung im Host), `Algorithms/Algorithms.cs`, `Collisions/Collisions.cs`.

Ergebnis: siehe docs/adr/0002 auf main und den Resolution-Kommentar in #2.
