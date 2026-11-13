// The network view of the playground (ADR 0050): draw or load points and lines, build a network on the server,
// show real nodes, helper nodes and edges, the shortest path between two clicked nodes, and the supply of the customers
// by the nodes marked as sources (ADR 0051), and check a small table of rules over the network (ADR 0052).
// Not a descriptor (ADR 0011), because its input is a set of lines and points.

/* global L */

import * as api from './api.js';
import { escape } from './strip.js';

const EDGE_COLORS = { 11: '#2563eb', 12: '#ea580c', 13: '#7c3aed' };
const NODE_COLORS = { 25: '#dc2626', 15: '#ca8a04', 17: '#0f172a', 16: '#16a34a' };
const NEUTRAL = '#64748b';
// One colour per supply area; a customer that two areas reach is orange with a black ring, one nobody reaches is red.
const AREA_COLORS = ['#2563eb', '#16a34a', '#db2777', '#0891b2', '#ca8a04', '#7c3aed', '#ea580c', '#475569'];

const state = {
  local: false,
  types: { nodes: [], edges: [] },
  points: [],       // {geometry, type}; the server calls them P1, P2 … in this order
  lines: [],        // {geometry, type}; L1, L2 …
  network: null,    // the answer of the last successful build; any edit of the input drops it
  from: null,       // node ids of the path
  to: null,
  blocked: new Set(),
  sources: new Set(), // node ids that are sources of the supply trace
  rules: [],        // rows of the rule table: the plain values of a NetworkRuleDefinition (ADR 0052)
};

// The rules of the concept that fit the types of the view; patterns use the names of the types.
const DEFAULT_RULES = () => [
  { id: 'KASTEN-ABGANG', severity: 'Error', subject: 'Node', subjectTypes: 'Kasten', endFilter: 'All', endTypes: '', countTypes: 'Kastenverbinder', min: 1, max: '' },
  { id: 'KV-FREIES-ENDE', severity: 'Error', subject: 'Edge', subjectTypes: 'Kastenverbinder', endFilter: 'NodeIsNot', endTypes: 'Kasten', countTypes: 'Kabel | Freileitung', min: 1, max: 1 },
  { id: 'LEITUNG-OFFEN', severity: 'Warning', subject: 'Edge', subjectTypes: 'Kabel | Freileitung', endFilter: 'NodeIs', endTypes: 'untyped', countTypes: '*', min: 1, max: '' },
];

const $ = id => document.getElementById(id);
let map, inputLayer, resultLayer, supplyLayer, pathLayer, ruleLayer, flashLayer, sequence = 0, supplySequence = 0, ruleSequence = 0;

// ---------- Map ----------

function setMode(local) {
  state.local = local;
  map?.remove();
  $('map').classList.toggle('local', local);
  map = L.map('map', local
    ? { crs: L.CRS.Simple, minZoom: -20, maxZoom: 20, zoomSnap: 0.25, attributionControl: false, boxZoom: false }
    : { minZoom: 1, maxZoom: 22, boxZoom: false });   // no box zoom: Shift-click blocks a node
  if (local) {
    map.setView([0, 0], 0);
  } else {
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 22, maxNativeZoom: 19, attribution: '© OpenStreetMap contributors',
    }).addTo(map);
    map.setView([47.07, 15.44], 13);
  }

  map.pm.setLang('en');
  map.pm.addControls({
    position: 'topleft', drawMarker: false, drawCircle: false, drawCircleMarker: false, drawText: false,
    drawPolyline: false, drawPolygon: false, drawRectangle: false, cutPolygon: false, rotateMode: false,
    editMode: true, dragMode: true, removalMode: false,
  });
  map.on('pm:create', e => {
    const geometry = e.layer.toGeoJSON().geometry;
    e.layer.remove();
    if (geometry.type === 'Point') state.points.push({ geometry, type: Number($('point-type').value) });
    else state.lines.push({ geometry, type: Number($('line-type').value) });
    inputChanged();
  });
  map.on('pm:drawend', () => document.querySelectorAll('[id^=draw-]').forEach(b => b.classList.remove('on')));
  inputLayer = L.featureGroup().addTo(map);
  resultLayer = L.featureGroup().addTo(map);
  supplyLayer = L.featureGroup().addTo(map);
  pathLayer = L.featureGroup().addTo(map);
  ruleLayer = L.featureGroup().addTo(map);
  flashLayer = L.featureGroup().addTo(map);
  $('tol-unit').textContent = local ? '(units)' : '(m)';
  renderInput();
  renderResult();
}

const latlng = point => L.GeoJSON.coordsToLatLng(point.coordinates);

function fit() {
  const bounds = L.featureGroup([...inputLayer.getLayers(), ...resultLayer.getLayers()]).getBounds();
  // The panel on the left and the chips on the right cover the map; on a narrow window only the panel is left out.
  const wide = map.getSize().x > 1000;
  if (bounds.isValid()) map.fitBounds(bounds, { paddingTopLeft: [410, 40], paddingBottomRight: [wide ? 330 : 40, 40], maxZoom: state.local ? 20 : 19 });
}

/** Zooms to a place and marks it for a moment. */
function flash(point) {
  const at = latlng(point);
  map.setView(at, Math.min(map.getMaxZoom(), Math.max(map.getZoom(), state.local ? 5 : 19)));
  flashLayer.clearLayers();
  L.circleMarker(at, { radius: 16, color: '#dc2626', weight: 3, fill: false, pmIgnore: true, interactive: false }).addTo(flashLayer);
  setTimeout(() => flashLayer.clearLayers(), 2500);
}

// ---------- Input ----------

function inputChanged() {
  sequence++;
  supplySequence++;
  ruleSequence++;
  state.network = null;
  state.from = state.to = null;
  state.blocked.clear();
  state.sources.clear();
  renderInput();
  renderResult();
  renderElements();
  $('build-state').textContent = '';
  showError(null);
  $('result').innerHTML = '';
  $('path').innerHTML = '';
  $('supply').innerHTML = '';
  $('rules-result').innerHTML = '';
}

function renderInput() {
  inputLayer.clearLayers();
  const built = !!state.network;
  const add = (kind, i, item) => {
    const color = (kind === 'point' ? NODE_COLORS : EDGE_COLORS)[item.type] ?? NEUTRAL;
    const layer = L.geoJSON(item.geometry, {
      style: built ? { color: NEUTRAL, weight: 1.5, dashArray: '4,4', opacity: 0.6, interactive: false } : { color, weight: 3 },
      pointToLayer: (_, at) => L.circleMarker(at, built
        ? { radius: 3, color: NEUTRAL, opacity: 0.6, interactive: false, pmIgnore: true }
        : { radius: 7, color: '#fff', weight: 2, fillColor: color, fillOpacity: 1 }),
    });
    if (!built) {
      layer.bindTooltip(`${kind === 'point' ? 'P' : 'L'}${i + 1}`);
      layer.on('pm:edit pm:dragend', () => {
        item.geometry = layer.toGeoJSON().features[0].geometry;
        inputChanged();
      });
    }
    layer.addTo(inputLayer);
  };
  state.lines.forEach((l, i) => add('line', i, l));
  state.points.forEach((p, i) => add('point', i, p));
}

function renderElements() {
  const list = $('elements');
  list.innerHTML = '';
  $('count').textContent = `(${state.points.length} points, ${state.lines.length} lines)`;
  const row = (kind, i, item) => {
    const types = kind === 'point' ? state.types.nodes : state.types.edges;
    const div = document.createElement('div');
    div.className = 'element';
    div.innerHTML = `<span class="name">${kind === 'point' ? 'P' : 'L'}${i + 1}</span>
      <select>${types.map(t => `<option value="${t.id}"${t.id === item.type ? ' selected' : ''}>${escape(t.name)} ${t.id}</option>`).join('')}</select>
      <button title="Remove">×</button>`;
    div.querySelector('.name').onclick = () => {
      const g = L.geoJSON(item.geometry).getBounds();
      if (g.isValid()) map.fitBounds(g.pad(2), { maxZoom: state.local ? 20 : 19 });
    };
    div.querySelector('select').onchange = e => {
      item.type = Number(e.target.value);
      inputChanged();
    };
    div.querySelector('button').onclick = () => {
      (kind === 'point' ? state.points : state.lines).splice(i, 1);
      inputChanged();
    };
    list.append(div);
  };
  state.points.forEach((p, i) => row('point', i, p));
  state.lines.forEach((l, i) => row('line', i, l));
}

/** Splits a loaded geometry into points and lines; polygons and the like are rejected. */
function addGeometry(g, point, line) {
  switch (g.type) {
    case 'Point': state.points.push({ geometry: g, type: point }); break;
    case 'MultiPoint': g.coordinates.forEach(c => state.points.push({ geometry: { type: 'Point', coordinates: c }, type: point })); break;
    case 'LineString': state.lines.push({ geometry: g, type: line }); break;
    case 'MultiLineString': g.coordinates.forEach(c => state.lines.push({ geometry: { type: 'LineString', coordinates: c }, type: line })); break;
    case 'GeometryCollection': g.geometries.forEach(x => addGeometry(x, point, line)); break;
    default: throw new Error(`A network is made of points and lines; ${g.type} cannot be used.`);
  }
}

async function loadWkt() {
  const lines = $('wkt').value.split('\n').map(s => s.trim()).filter(Boolean);
  try {
    for (const wkt of lines) {
      const { geometry } = await api.convert({ wkt });
      addGeometry(geometry, Number($('point-type').value), Number($('line-type').value));
    }
    $('wkt').value = '';
    inputChanged();
    fit();
  } catch (e) {
    inputChanged();
    showError(e.message);
  }
}

async function loadFile(file) {
  try {
    const { geometries } = await api.importGeoJson(file);
    geometries.forEach(g => addGeometry(g, Number($('point-type').value), Number($('line-type').value)));
    inputChanged();
    fit();
  } catch (e) {
    showError(e.message);
  }
}

/** The example of the concept page: a transformer, a cable with a switch in the middle, a connector ending on the cable 4 mm beside it, a customer. */
function loadExample() {
  setMode(true);
  $('mode').value = 'local';
  const pt = (x, y, type) => ({ geometry: { type: 'Point', coordinates: [x, y] }, type });
  const ln = (type, ...c) => ({ geometry: { type: 'LineString', coordinates: c }, type });
  state.points = [pt(0, 0, 25), pt(30, 0, 17), pt(50, 30, 16)];
  state.lines = [ln(11, [0, 0], [100, 0]), ln(13, [50, 30], [50, 0.004])];
  inputChanged();
  fit();
}

// ---------- Build ----------

function request() {
  return {
    points: state.points, lines: state.lines,
    tolerance: Number($('tolerance').value), kbs: 'auto', local: state.local,
    splitAtNodes: $('split-nodes').checked, splitAtLineEnds: $('split-ends').checked, nodeAtCrossings: $('crossings').checked,
  };
}

async function build() {
  const mine = ++sequence;
  $('build-state').textContent = 'building …';
  showError(null);
  try {
    const result = await api.networkBuild(request());
    if (mine !== sequence) return;
    $('build-state').textContent = '';
    state.from = state.to = null;
    state.blocked.clear();
    state.sources.clear();
    $('path').innerHTML = '';
    $('supply').innerHTML = '';
    $('rules-result').innerHTML = '';
    $('kbs-label').textContent = result.kbs === 'local' ? 'Local planar' : `CRS ${result.kbs}`;
    if (!result.ok) {
      state.network = null;
      renderInput();
      renderResult();
      showConflicts(result);
      return;
    }

    state.network = result;
    renderInput();
    renderResult();
    showBuild(result);
    fit();
  } catch (e) {
    if (mine !== sequence) return;
    $('build-state').textContent = '';
    showError(e.message);
  }
}

// ---------- Result ----------

function renderResult() {
  resultLayer.clearLayers();
  supplyLayer.clearLayers();
  pathLayer.clearLayers();
  ruleLayer.clearLayers();
  $('legend').innerHTML = '';
  const net = state.network;
  if (!net) return;

  for (const e of net.edges) {
    if (!e.geometry) continue;
    L.geoJSON(e.geometry, { pmIgnore: true, style: { color: EDGE_COLORS[e.type] ?? NEUTRAL, weight: 4, opacity: 0.9 } })
      .bindTooltip(`${e.id} · ${e.typeName} ${e.type} · ${fmt(e.length)} ${net.unit ?? ''} (${fmt(e.fromMeasure)} to ${fmt(e.toMeasure)} on ${e.source})`.trim(), { sticky: true })
      .addTo(resultLayer);
  }

  for (const n of net.nodes) {
    const at = latlng(n.geometry);
    const marker = n.helper
      ? L.circleMarker(at, { radius: 5, color: NEUTRAL, weight: 2, fillColor: '#fff', fillOpacity: 1, pmIgnore: true })
      : L.circleMarker(at, { radius: 9, color: '#fff', weight: 2, fillColor: NODE_COLORS[n.type] ?? NEUTRAL, fillOpacity: 1, pmIgnore: true });
    marker.bindTooltip(`${n.id} · ${n.typeName}${n.helper ? '' : ' ' + n.type}`);
    marker.on('click', ev => {
      if ($('source-mode').checked && !n.helper) toggleSource(n.id);
      else if (ev.originalEvent.shiftKey) toggleBlocked(n.id);
      else pick(n.id);
    });
    marker.on('contextmenu', () => toggleBlocked(n.id));
    marker.nodeId = n.id;
    marker.addTo(resultLayer);
  }

  decorateNodes();
  const usedNodes = [...new Set(net.nodes.filter(n => !n.helper).map(n => n.type))];
  const usedEdges = [...new Set(net.edges.map(e => e.type))];
  $('legend').innerHTML =
    usedEdges.map(t => `<span><span class="sw" style="background:${EDGE_COLORS[t] ?? NEUTRAL}"></span> ${escape(net.edges.find(e => e.type === t).typeName)}</span>`).join('') +
    usedNodes.map(t => `<span><span class="sw round" style="background:${NODE_COLORS[t] ?? NEUTRAL}"></span> ${escape(net.nodes.find(n => n.type === t).typeName)}</span>`).join('') +
    '<span><span class="sw round" style="border:2px solid #64748b;background:#fff"></span> Helper node</span>';
}

/** Start, end and blocked nodes get their own outline. */
function decorateNodes() {
  resultLayer.eachLayer(l => {
    if (!l.nodeId) return;
    if (l.nodeId === state.from) l.setStyle({ color: '#16a34a', weight: 4 });
    else if (l.nodeId === state.to) l.setStyle({ color: '#f59e0b', weight: 4 });
    else if (state.blocked.has(l.nodeId)) l.setStyle({ color: '#b91c1c', weight: 4 });
    else if (state.sources.has(l.nodeId)) l.setStyle({ color: '#facc15', weight: 5 });
    else l.setStyle({ color: l.options.fillColor === '#fff' ? NEUTRAL : '#fff', weight: 2 });
  });
}

function showBuild(r) {
  const items = [
    ...r.diagnostics.map(d => ({ cls: '', text: `${d.kind}: ${d.message}`, at: d.location })),
  ];
  const helpers = r.nodes.filter(n => n.helper).length;
  $('result').innerHTML = `<div class="big">${r.nodeCount} nodes, ${r.edgeCount} edges</div>
    <div class="muted">${r.nodeCount - helpers} real, ${helpers} helper nodes</div>
    ${r.warnings.map(w => `<div class="warn">${escape(w)}</div>`).join('')}
    ${items.length ? `<h3>Diagnostics (${items.length})</h3>` : '<div class="muted">No diagnostics.</div>'}
    <ul>${items.map((d, i) => `<li data-i="${i}">${escape(d.text)}</li>`).join('')}</ul>
    <div class="muted">Click two nodes for the shortest path; Shift-click or right-click blocks a node. Under Supply, mark sources to see who supplies the customers.</div>`;
  $('result').querySelectorAll('li').forEach(li => (li.onclick = () => flash(items[li.dataset.i].at)));
  $('path').innerHTML = '';
}

function showConflicts(r) {
  const items = r.conflicts;
  $('result').innerHTML = `<div class="bad">${escape(r.error)}</div>
    <h3>Conflicts (${items.length})</h3>
    <ul>${items.map((c, i) => `<li data-i="${i}"><b>${escape(c.kind)}</b> ${escape(c.sources.join(', '))}</li>`).join('')}</ul>`;
  $('result').querySelectorAll('li').forEach(li => (li.onclick = () => flash(items[li.dataset.i].location)));
  for (const c of items) {
    L.circleMarker(latlng(c.location), { radius: 12, color: '#dc2626', weight: 3, fill: false, pmIgnore: true, interactive: false }).addTo(resultLayer);
  }
  if (items.length) flash(items[0].location);
}

function showError(message) {
  $('error').hidden = !message;
  $('error').textContent = message ?? '';
}

// ---------- Shortest path ----------

function pick(id) {
  if (state.from && !state.to && id !== state.from) state.to = id;
  else {
    state.from = id;
    state.to = null;
  }

  decorateNodes();
  findPath();
}

function toggleBlocked(id) {
  if (state.blocked.has(id)) state.blocked.delete(id);
  else state.blocked.add(id);
  decorateNodes();
  findPath();
  if (state.sources.size) showSupply();
}

async function findPath() {
  pathLayer.clearLayers();
  const box = $('path');
  if (!state.from) {
    box.innerHTML = '';
    return;
  }

  const blocked = state.blocked.size ? `<div class="muted">Blocked: ${[...state.blocked].map(escape).join(', ')}</div>` : '';
  if (!state.to) {
    box.innerHTML = `<div class="muted">Start ${escape(state.from)} – click the end node.</div>${blocked}`;
    return;
  }

  const mine = ++sequence;
  try {
    const r = await api.networkPath({ ...request(), from: state.from, to: state.to, blocked: [...state.blocked] });
    if (mine !== sequence) return;
    if (!r.ok) return showError(r.error);
    if (!r.found) {
      box.innerHTML = `<div class="bad">No path from ${escape(r.from)} to ${escape(r.to)}.</div>${blocked}`;
      return;
    }

    if (r.geometry) {
      L.geoJSON(r.geometry, { pmIgnore: true, interactive: false, style: { color: '#0f172a', weight: 10, opacity: 0.35, lineCap: 'round' } }).addTo(pathLayer);
      L.geoJSON(r.geometry, { pmIgnore: true, interactive: false, style: { color: '#facc15', weight: 5, lineCap: 'round' } }).addTo(pathLayer);
    }

    box.innerHTML = `<div class="big">${escape(r.from)} → ${escape(r.to)}: ${fmt(r.cost)} ${escape(r.unit ?? '')}</div>
      <div class="muted">${r.edges.length} edges: ${r.edges.map(escape).join(' > ')}</div>${blocked}`;
  } catch (e) {
    if (mine === sequence) showError(e.message);
  }
}

const fmt = v => (Number.isFinite(v) ? Number(v.toFixed(3)).toString() : String(v));

// ---------- Supply (ADR 0051) ----------

function toggleSource(id) {
  if (state.sources.has(id)) state.sources.delete(id);
  else state.sources.add(id);
  decorateNodes();
  showSupply();
}

/** Runs the trace from the marked sources: colours each supply area and marks the customers nobody or several sources supply. */
async function showSupply() {
  supplyLayer.clearLayers();
  const box = $('supply');
  if (!state.network || state.sources.size === 0) {
    box.innerHTML = '';
    return;
  }

  const mine = ++supplySequence;
  try {
    const r = await api.networkSupply({ ...request(), sources: [...state.sources], blocked: [...state.blocked] });
    if (mine !== supplySequence) return;
    if (!r.ok) return showError(r.error);
    showError(null);

    const colorOf = new Map(r.sources.map((s, i) => [s.id, AREA_COLORS[i % AREA_COLORS.length]]));
    const edges = new Map(state.network.edges.map(e => [e.id, e]));
    for (const s of r.sources) {
      for (const id of s.edges) {
        const geometry = edges.get(id)?.geometry;
        if (geometry) L.geoJSON(geometry, { pmIgnore: true, interactive: false, style: { color: colorOf.get(s.id), weight: 11, opacity: 0.45, lineCap: 'round' } }).addTo(supplyLayer);
      }
    }

    const none = [], many = [];
    for (const c of r.sinks) {
      const at = latlng(c.geometry);
      const tooltip = c.sources.length ? `${c.id} · supplied by ${c.sources.join(', ')}` : `${c.id} · not supplied`;
      let style;
      if (c.sources.length === 0) {
        none.push(c);
        style = { radius: 8, color: '#dc2626', weight: 3, fillColor: '#fee2e2', fillOpacity: 1 };
      } else if (c.sources.length > 1) {
        many.push(c);
        style = { radius: 8, color: '#0f172a', weight: 3, fillColor: '#f97316', fillOpacity: 1 };
      } else {
        style = { radius: 6, color: '#fff', weight: 2, fillColor: colorOf.get(c.sources[0]), fillOpacity: 1 };
      }

      L.circleMarker(at, { pmIgnore: true, className: 'supply-sink', ...style }).bindTooltip(tooltip).addTo(supplyLayer);
    }

    const row = (c, why) => `<li data-id="${escape(c.id)}">${escape(c.id)} ${why}</li>`;
    const sinks = new Map(r.sinks.map(c => [c.id, c]));
    box.innerHTML = `<div class="big">${r.sources.length} source${r.sources.length === 1 ? '' : 's'}, ${r.sinks.length} customers</div>
      <div>${r.sources.map(s => `<span><span class="swatch" style="background:${colorOf.get(s.id)}"></span>${escape(s.id)}${s.blocked ? ' (blocked)' : ''}</span>`).join(' ')}</div>
      <div class="muted">${r.sinks.length - none.length - many.length} supplied once, ${none.length} not supplied, ${many.length} multiply supplied</div>
      ${none.length || many.length ? `<ul>${none.map(c => row(c, '· not supplied')).join('')}${many.map(c => row(c, `· ${escape(c.sources.join(' + '))}`)).join('')}</ul>` : ''}`;
    box.querySelectorAll('li').forEach(li => (li.onclick = () => flash(sinks.get(li.dataset.id).geometry)));
  } catch (e) {
    if (mine === supplySequence) showError(e.message);
  }
}

// ---------- Rules (ADR 0052) ----------

/** The rule table: one block per rule with the plain values of a NetworkRuleDefinition; every edit is kept in state.rules. */
function renderRules() {
  const box = $('rules');
  box.innerHTML = '';
  state.rules.forEach((rule, i) => {
    const div = document.createElement('div');
    div.className = 'rule';
    const select = (field, values) => `<select data-f="${field}">${values.map(v => `<option${v === rule[field] ? ' selected' : ''}>${v}</option>`).join('')}</select>`;
    div.innerHTML = `
      <div class="rule-head"><input data-f="id" value="${escape(rule.id)}" title="Rule id" aria-label="Rule id"> ${select('severity', ['Error', 'Warning'])} <button title="Remove rule">×</button></div>
      <label>Check ${select('subject', ['Node', 'Edge'])} <input data-f="subjectTypes" value="${escape(rule.subjectTypes)}" placeholder="pattern, e.g. Kasten | Trafo"></label>
      <label class="ends">Ends ${select('endFilter', ['All', 'NodeIs', 'NodeIsNot'])} <input data-f="endTypes" value="${escape(rule.endTypes)}" placeholder="node pattern, e.g. Kasten or untyped"></label>
      <label><span class="count-label">Count edges</span> <input data-f="countTypes" value="${escape(rule.countTypes)}" placeholder="edge pattern, e.g. Kabel | Freileitung"></label>
      <label class="range">Min <input data-f="min" type="number" min="0" step="1" value="${rule.min}"> Max <input data-f="max" type="number" min="0" step="1" value="${rule.max}" placeholder="none"></label>`;
    const refresh = () => {
      const edge = rule.subject === 'Edge';
      div.querySelector('.ends').hidden = !edge;
      div.querySelector('[data-f=endTypes]').disabled = rule.endFilter === 'All';
      div.querySelector('.count-label').textContent = edge ? 'Count other edges' : 'Count edges';
    };
    div.querySelectorAll('[data-f]').forEach(input => {
      input.oninput = () => {
        rule[input.dataset.f] = input.type === 'number' ? (input.value === '' ? '' : Number(input.value)) : input.value;
        if (input.dataset.f === 'subject' && rule.subject === 'Node') rule.endFilter = 'All';
        if (input.dataset.f === 'endFilter' && rule.endFilter === 'All') rule.endTypes = '';
        refresh();
      };
    });
    div.querySelector('button').onclick = () => {
      state.rules.splice(i, 1);
      renderRules();
    };
    refresh();
    box.append(div);
  });
}

/** The rows as the server wants them: an empty Max is no upper limit, end types only with an end filter. */
function ruleRows() {
  return state.rules.map(r => ({
    id: r.id, severity: r.severity, subject: r.subject, subjectTypes: r.subjectTypes,
    endFilter: r.subject === 'Edge' ? r.endFilter : 'All', endTypes: r.subject === 'Edge' && r.endFilter !== 'All' ? r.endTypes : null,
    countTypes: r.countTypes, min: r.min === '' ? 0 : r.min, max: r.max === '' ? null : r.max,
  }));
}

/** Checks the rules over the whole network and marks every violation on the map. */
async function checkRules() {
  ruleLayer.clearLayers();
  const box = $('rules-result');
  box.innerHTML = '';
  const mine = ++ruleSequence;
  showError(null);
  try {
    const r = await api.networkValidate({ ...request(), rules: ruleRows() });
    if (mine !== ruleSequence) return;
    if (!r.ok) {
      showConflicts(r);
      return;
    }

    const rangeOf = v => (v.max === null ? `at least ${v.min}` : v.min === v.max ? `exactly ${v.min}` : `${v.min} to ${v.max}`);
    const items = r.violations.map(v => ({
      v,
      text: `${v.rule} (${v.severity}): ${v.kind === 'Edge' ? 'edge ' + v.edge + ' at node ' : 'node '}${v.node} · ${v.typeName} · ${v.count} found, ${rangeOf(v)} allowed`,
    }));
    for (const { v, text } of items) {
      const color = v.severity === 'Warning' ? '#f59e0b' : '#dc2626';
      L.circleMarker(latlng(v.geometry), { radius: 14, color, weight: 3, fill: false, pmIgnore: true })
        .bindTooltip(text).addTo(ruleLayer);
    }

    const errors = items.filter(i => i.v.severity === 'Error').length;
    const plural = (n, word) => `${n} ${word}${n === 1 ? '' : 's'}`;
    box.innerHTML = `<div class="big">${plural(items.length, 'violation')}</div>
      <div class="muted">${plural(errors, 'error')}, ${plural(items.length - errors, 'warning')} · ${plural(r.rules.length, 'rule')}</div>
      <ul>${items.map((i, n) => `<li data-i="${n}" class="${i.v.severity === 'Warning' ? 'warning' : 'error'}">${escape(i.text)}</li>`).join('')}</ul>
      <details><summary class="muted">The rules</summary><ul class="plain">${r.rules.map(x => `<li>${escape(x.text)}</li>`).join('')}</ul></details>`;
    box.querySelectorAll('li[data-i]').forEach(li => (li.onclick = () => flash(items[li.dataset.i].v.geometry)));
  } catch (e) {
    if (mine === ruleSequence) showError(e.message);
  }
}

// ---------- Start ----------

function drawButton(id, type) {
  $(id).onclick = () => {
    const on = !$(id).classList.contains('on');
    map.pm.disableDraw();
    document.querySelectorAll('[id^=draw-]').forEach(b => b.classList.remove('on'));
    if (on) {
      $(id).classList.add('on');
      map.pm.enableDraw(type === 'point' ? 'Marker' : 'Line', { markerStyle: { draggable: false } });
    }
  };
}

async function start() {
  state.types = await api.networkTypes();
  const options = types => types.map(t => `<option value="${t.id}">${escape(t.name)} ${t.id}</option>`).join('');
  $('point-type').innerHTML = options(state.types.nodes);
  $('line-type').innerHTML = options(state.types.edges);
  $('point-type').value = '15';

  setMode(false);
  renderElements();
  drawButton('draw-point', 'point');
  drawButton('draw-line', 'line');
  $('mode').onchange = e => {
    state.points = [];
    state.lines = [];
    setMode(e.target.value === 'local');
    inputChanged();
  };
  $('load-wkt').onclick = loadWkt;
  $('load-file').onchange = e => e.target.files[0] && loadFile(e.target.files[0]).then(() => (e.target.value = ''));
  $('example').onclick = loadExample;
  $('clear').onclick = () => {
    state.points = [];
    state.lines = [];
    inputChanged();
    $('kbs-label').textContent = '';
  };
  $('build').onclick = build;
  state.rules = DEFAULT_RULES();
  renderRules();
  $('rule-add').onclick = () => {
    state.rules.push({ id: `RULE-${state.rules.length + 1}`, severity: 'Error', subject: 'Node', subjectTypes: '*', endFilter: 'All', endTypes: '', countTypes: '*', min: 0, max: '' });
    renderRules();
  };
  $('rules-default').onclick = () => {
    state.rules = DEFAULT_RULES();
    renderRules();
  };
  $('rules-run').onclick = checkRules;
  $('supply-run').onclick = showSupply;
  $('sources-clear').onclick = () => {
    state.sources.clear();
    decorateNodes();
    showSupply();
  };
}

start().catch(e => showError(e.message));
