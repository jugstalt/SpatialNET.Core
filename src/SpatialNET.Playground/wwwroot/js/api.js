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

/** The fixed node and edge types of the network view (ADR 0050) → {nodes:[{id,name}], edges:[{id,name}]}. */
export const networkTypes = () => fetch('api/network/types').then(json);

const post = (url, request) =>
  fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request) }).then(json);

/** Builds a network from points and lines → {ok, nodes, edges, diagnostics} or {ok: false, error, conflicts}. */
export const networkBuild = request => post('api/network/build', request);

/** Shortest path between two node ids (the request of the build plus from, to and blocked) → {found, cost, geometry, edges}. */
export const networkPath = request => post('api/network/path', request);

/** The supply areas of the marked sources and the sources of every customer (ADR 0051) → {ok, sources:[{id, blocked, edges}], sinks:[{id, geometry, sources}]}. */
export const networkSupply = request => post('api/network/supply', request);
