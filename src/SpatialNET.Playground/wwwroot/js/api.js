// Zugriff auf die Minimal API (ADR 0011).

async function json(response) {
  const body = await response.json().catch(() => ({ error: `HTTP ${response.status}` }));
  if (!response.ok) throw new Error(body.error ?? `HTTP ${response.status}`);
  return body;
}

export const algorithms = () => fetch('api/algorithms').then(json);

export const fixtures = (algorithm, all) =>
  fetch(`api/fixtures?algorithm=${encodeURIComponent(algorithm)}${all ? '&all=true' : ''}`).then(json);

/** Rechnet; {algorithm, inputs:{a,b}, params, kbs, local} → Ergebnis-Beschreibung. */
export const run = request =>
  fetch('api/run', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request) }).then(json);

/** WKT <-> GeoJSON: {wkt} oder {geometry} -> {geometry, wkt}; WKT trägt Z und M, GeoJSON nicht (ADR 0015). */
export const convert = request =>
  fetch('api/convert', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request) }).then(json);

/** Liest eine GeoJSON-Datei auf dem Server (GeoJson.ReadAll) → Liste von Geometrien. */
export const importGeoJson = file =>
  fetch('api/import', { method: 'POST', headers: { 'Content-Type': 'application/geo+json' }, body: file }).then(json);
