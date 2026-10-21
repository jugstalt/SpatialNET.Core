# Eigene Algorithmen, keine fremde Geometrie-Engine

Alle Algorithmen in SpatialNET.Extensions werden selbst implementiert und arbeiten direkt gegen die Geometrie-Interfaces; es wird intern weder NetTopologySuite noch Clipper2 o.ä. verwendet. Damit bleibt die Lib abhängigkeitsfrei und vollständig unter eigener Kontrolle, auf Kosten von deutlich mehr Aufwand bei Robustheit (Overlay-Randfälle, numerische Präzision). NetTopologySuite darf ausschließlich im Testprojekt als Vergleichs-Orakel vorkommen.

*English version: [0001-eigene-algorithmen-ohne-geometrie-engine.md](0001-eigene-algorithmen-ohne-geometrie-engine.md)*

## Considered Options

- Interface → NTS/Clipper2 konvertieren, dort rechnen, zurückschreiben (verworfen: fremde Engine als Abhängigkeit)
- Gemischt: einfache Algorithmen selbst, Overlays über Clipper2 (verworfen)
