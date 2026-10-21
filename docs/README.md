# Documentation

The documentation is bilingual: English is the leading language, German is the second.

| Folder | Contents |
|---|---|
| [`en/`](en/) | English pages (getting started, host integration, glossary); `en/api/` receives the generated API reference |
| [`de/`](de/) | German pages (Erste Schritte, Host-Anbindung, Glossar) |
| [`adr/`](adr/) | Architecture decision records, `NNNN-slug.md` (English) and `NNNN-slug.de.md` (German) |

The pages of both languages are maintained together: a change to a page changes its counterpart in the same commit. The API reference is generated from the XML comments and exists in English only.

Build the sites with DocFX (pinned as a local tool in `.config/dotnet-tools.json`):

```powershell
./docs/build.ps1          # builds docs/_site/en and docs/_site/de
./docs/build.ps1 -Serve   # additionally serves the result at http://localhost:8080
```

The code examples in the pages are covered by `tests/SpatialNET.Tests/DocSamples/`; when an example changes, update the page as well.

---

# Dokumentation

Die Dokumentation ist zweisprachig: Englisch ist die führende Sprache, Deutsch die zweite. Die Ordner `en/`, `de/` und `adr/` sind oben beschrieben; die Seiten beider Sprachen werden gemeinsam gepflegt (eine Änderung betrifft im selben Commit auch die Gegenstück-Seite). Die API-Referenz wird aus den XML-Kommentaren generiert und gibt es nur auf Englisch. Gebaut wird mit `./docs/build.ps1`, die Code-Beispiele deckt `tests/SpatialNET.Tests/DocSamples/` ab.
