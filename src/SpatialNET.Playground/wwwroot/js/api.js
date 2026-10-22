// Access to the minimal API (ADR 0011).

async function json(response) {
  const body = await response.json().catch(() => ({ error: `HTTP ${response.status}` }));
  if (!response.ok) throw new Error(body.error ?? `HTTP ${response.status}`);
  return body;
}

export const algorithms = () => fetch('api/algorithms').then(json);

export const fixtures = (algorithm, all) =>
  fetch(`api/fixtures?algorithm=${encodeURIComponent(algorithm)}${all ? '&all=true' : ''}`).then(json);

/** Computes; {algorithm, inputs:{a,b}, params, kbs, local} → description of the result. */
export const run = request =>
  fetch('api/run', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request) }).then(json);

/** WKT <-> GeoJSON: {wkt} or {geometry} -> {geometry, wkt}; WKT carries Z and M, GeoJSON does not (ADR 0015). */
export const convert = request =>
  fetch('api/convert', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request) }).then(json);

/** Reads a GeoJSON file on the server (GeoJson.ReadAll) → list of geometries. */
export const importGeoJson = file =>
  fetch('api/import', { method: 'POST', headers: { 'Content-Type': 'application/geo+json' }, body: file }).then(json);
