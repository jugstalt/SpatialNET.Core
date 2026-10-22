// SpatialNET Playground, variant "map first" (ADR 0011). The UI knows no algorithm by name:
// palette, parameters and inputs are built from GET /api/algorithms.

import * as api from './api.js';
import { MapView } from './map.js';
import { renderStrip, escape } from './strip.js';

const KBS = [
  ['auto', 'Auto-UTM'], ['EPSG:3857', 'EPSG:3857 (Web Mercator)'], ['EPSG:25832', 'EPSG:25832 (ETRS89/UTM 32N)'],
  ['EPSG:25833', 'EPSG:25833 (ETRS89/UTM 33N)'], ['EPSG:31256', 'EPSG:31256 (MGI/GK Ost)'], ['free', 'free EPSG code …'],
];
const DEBOUNCE_MS = 300;

const state = {
  algorithms: [],
  algorithm: null,
  params: {},
  inputs: {},
  wkt: {},        // the WKT text of an input that counts (pasted, or from an example with M); drawing and uploading drop it
  shown: {},      // WKT text to show for inputs without a WKT text of their own (made by the server from the GeoJSON)
  wktOpen: false,
  resultWktOpen: false,
  fixtures: [],
  fixture: null,
  allFixtures: false,
  local: false,
  kbs: 'auto',
  last: null,
  fitOnResult: false,
};

const $ = id => document.getElementById(id);
const view = new MapView($('map'), (key, geometry) => {
  state.inputs[key] = geometry;
  delete state.wkt[key]; // a drawn or moved geometry has no M any more
  state.fixture = null;
  view.setInputs(state.inputs, false);
  renderInputs();
  renderKbs();
  renderStripState();
  schedule();
});

// ---------- Computing (auto-run with debounce, stale answers are dropped) ----------

let timer = 0, sequence = 0;
function schedule() {
  clearTimeout(timer);
  timer = setTimeout(compute, DEBOUNCE_MS);
}

async function compute() {
  const alg = state.algorithm;
  const missing = alg?.inputs.filter(i => !state.inputs[i.key]) ?? [];
  if (!alg || missing.length) {
    sequence++;
    showResult(null, null);
    $('result').innerHTML = `<div class="muted">Input ${missing.map(i => escape(i.label)).join(', ')} is missing – draw, upload or pick an example.</div>`;
    return;
  }

  const id = ++sequence;
  const inputs = Object.fromEntries(alg.inputs.map(i => [i.key, state.inputs[i.key]]));
  const wkt = Object.fromEntries(alg.inputs.filter(i => state.wkt[i.key]).map(i => [i.key, state.wkt[i.key]]));
  const kbs = state.fixture?.crs ?? (state.kbs === 'free' ? $('kbs-free').value.trim() || 'auto' : state.kbs);
  try {
    const result = await api.run({ algorithm: alg.id, inputs, wkt, params: state.params, kbs, local: state.local });
    if (id !== sequence) return;
    state.last = result;
    showResult(result, null);
    if (state.fitOnResult) {
      state.fitOnResult = false;
      view.fit();
    }
  } catch (e) {
    if (id !== sequence) return;
    showResult(state.last, e.message); // the error is shown as a chip, the last result stays
  }
}

// ---------- Result chips ----------

const fmt = v => typeof v === 'number' ? v.toLocaleString('en-US', { maximumFractionDigits: 6 }) : escape(v);
// do not round very small distances (rounding size) to 0
const fmtSmall = v => v !== 0 && Math.abs(v) < 1e-4 ? v.toExponential(2) : fmt(v);

function showResult(result, error) {
  $('error').hidden = !error;
  $('error').textContent = error ? `⚠ ${error}` : '';
  view.setResult(result);
  $('kbs-label').textContent = result ? `Computation CRS: ${result.kbs}${result.unit ? ` · unit ${result.unit}` : ''}` : '';
  if (!result) {
    $('result').innerHTML = '';
    return;
  }

  const u = result.unit ? ` ${result.unit}` : '', u2 = result.unit ? ` ${result.unit}²` : '';
  let html = '';
  switch (result.kind) {
    case 'number': html = `<div class="value">${result.value === null ? escape(result.text) : fmt(result.value)}</div>`; break;
    case 'bool':
      html = `<div class="value">${result.value ? 'true' : 'false'}</div>`;
      if (typeof result.distance === 'number') html += `<div class="muted">Distance A–B: ${fmtSmall(result.distance)}${u}</div>`;
      if (result.nearlyTouching) {
        html += '<div class="warn">The geometries lie practically on each other, but not exactly: predicates compute exactly on the coordinates. '
          + 'A point drawn "on" a line is almost never exactly on it. For "approximately" use IsWithinDistance.</div>';
      }

      if (result.alternative) {
        html += `<div class="muted">With a tolerance t: <code>${escape(result.alternative)}</code></div>`;
      }

      break;
    case 'matrix': html = `<div class="value" style="font-family:ui-monospace,monospace">${escape(result.value)}</div><div class="muted">DE-9IM</div>`; break;
    case 'none': html = `<div class="muted">${escape(result.text)}</div>`; break;
    case 'validation':
      html = `<div class="value">${result.value ? 'valid' : 'invalid'}</div>`
        + result.issues.map(i => `<div class="warn">${escape(i.kind)}</div>`).join('');
      break;
    default:
      if (result.text) html += `<div class="value">${escape(result.text)}</div>`;
      if (result.kind === 'geometries') html += `<div class="muted">${result.geometries.length} parts (numbered)</div>`;
  }

  if (result.stats) {
    const s = result.stats;
    html += `<table>
      <tr><td>Area</td><td>${fmt(s.area)}${u2}</td></tr>
      <tr><td>Length</td><td>${fmt(s.length)}${u}</td></tr>
      <tr><td>Vertices</td><td>${fmt(s.vertices)}</td></tr>
      <tr><td>Parts</td><td>${fmt(s.parts)}</td></tr></table>`;
  }

  html += (result.warnings ?? []).map(w => `<div class="warn">⚠ ${escape(w)}</div>`).join('');
  const wktText = result.wkts ? result.wkts.join('\n') : result.wkt;
  if (wktText) {
    html += `<details class="wkt" ${state.resultWktOpen ? 'open' : ''}><summary>WKT</summary>
      <textarea readonly rows="4" spellcheck="false">${escape(wktText)}</textarea>
      <button data-copy>Copy</button></details>`;
  }

  $('result').innerHTML = html;
  const box = $('result').querySelector('details.wkt');
  if (box) {
    box.ontoggle = () => { state.resultWktOpen = box.open; };
    box.querySelector('[data-copy]').onclick = async e => {
      await navigator.clipboard?.writeText(wktText);
      e.target.textContent = 'Copied';
      setTimeout(() => { e.target.textContent = 'Copy'; }, 1200);
    };
  }
}

// ---------- Palette (Strg+K) ----------

function renderList() {
  const q = $('search').value.trim().toLowerCase();
  const items = state.algorithms.filter(a => !q || a.name.toLowerCase().includes(q) || a.group.toLowerCase().includes(q) || a.id.includes(q));
  $('list').innerHTML = items.map(a => `<div class="item ${a === state.algorithm ? 'sel' : ''}" data-id="${a.id}"><span>${escape(a.name)}</span><span class="muted">${escape(a.group)}</span></div>`).join('');
  $('list').querySelectorAll('[data-id]').forEach(el => el.onmousedown = e => { e.preventDefault(); choose(el.dataset.id); });
}

function openList(open) {
  $('list').hidden = !open;
  if (open) renderList();
}

$('search').addEventListener('focus', () => openList(true));
$('search').addEventListener('blur', () => openList(false));
$('search').addEventListener('input', renderList);
$('search').addEventListener('keydown', e => {
  if (e.key === 'Enter') {
    const first = $('list').querySelector('[data-id]');
    if (first) choose(first.dataset.id);
  } else if (e.key === 'Escape') {
    $('search').blur();
  }
});
document.addEventListener('keydown', e => {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
    e.preventDefault();
    $('search').focus();
    $('search').select();
  }
});

// ---------- Parameter aus dem Deskriptor ----------

function unitLabel(p) {
  if (p.unit !== 'kbs') return '';
  return state.local ? 'units' : (state.last?.unit ?? 'CRS unit');
}

function renderParams() {
  const alg = state.algorithm, box = $('params');
  box.innerHTML = '';
  if (!alg.parameters.length) {
    box.innerHTML = '<div class="muted">no parameters</div>';
    return;
  }

  for (const p of alg.parameters) {
    const row = document.createElement('label');
    row.className = 'field';
    row.innerHTML = `<span>${escape(p.label)} <span class="muted">${escape(unitLabel(p))}</span></span>`;
    let input;
    if (p.type === 'enum') {
      input = document.createElement('select');
      p.values.forEach(v => input.add(new Option(v, v)));
      input.value = state.params[p.key] ?? p.default;
      input.onchange = () => set(p.key, input.value);
    } else if (p.type === 'bool') {
      input = document.createElement('input');
      input.type = 'checkbox';
      input.checked = !!(state.params[p.key] ?? p.default);
      input.onchange = () => set(p.key, input.checked);
    } else {
      input = document.createElement('input');
      input.type = 'number';
      input.step = p.type === 'int' ? '1' : 'any';
      input.placeholder = p.default === null ? 'auto' : '';
      input.value = state.params[p.key] ?? p.default ?? '';
      input.oninput = () => set(p.key, input.value === '' ? null : Number(input.value));
    }

    row.appendChild(input);
    box.appendChild(row);
  }
}

function set(key, value) {
  state.params[key] = value;
  schedule();
}

// ---------- Inputs: drawing (target A/B), upload, clear ----------

const TYPE_LABELS = { Point: 'Point', MultiPoint: 'Points', LineString: 'Line', Polygon: 'Polygon' };

function describe(geometry) {
  return geometry ? `<span class="muted">${escape(geometry.type)}</span>` : '<span class="muted">empty</span>';
}

function renderInputs() {
  const alg = state.algorithm;
  $('inputs').innerHTML = alg.inputs.map(i => `
    <div class="input-row">
      <span class="name ${i.key}">${escape(i.label)}</span>
      ${i.types.map(t => `<button data-draw="${i.key}:${t}" title="draw ${TYPE_LABELS[t].toLowerCase()}">✎ ${TYPE_LABELS[t]}</button>`).join('')}
      <button data-upload="${i.key}" title="upload GeoJSON">⇪</button>
      <button data-clear="${i.key}" title="clear">✕</button>
      ${describe(state.inputs[i.key])}
    </div>
    <details class="wkt" data-wkt-box="${i.key}" ${state.wktOpen ? 'open' : ''}>
      <summary title="Read and write as WKT, also with Z and M. Editing on the map drops M.">WKT</summary>
      <textarea data-wkt="${i.key}" rows="3" spellcheck="false" placeholder="LINESTRING ZM (0 0 0 0, 10 0 10 100)">${escape(state.wkt[i.key] ?? state.shown[i.key] ?? '')}</textarea>
      <div class="muted" data-wkt-msg="${i.key}"></div>
    </details>`).join('');
  $('inputs').querySelectorAll('[data-draw]').forEach(b => b.onclick = () => { const [k, t] = b.dataset.draw.split(':'); view.draw(k, t); });
  $('inputs').querySelectorAll('[data-upload]').forEach(b => b.onclick = () => upload(b.dataset.upload));
  $('inputs').querySelectorAll('details.wkt').forEach(d => d.ontoggle = () => { state.wktOpen = d.open; });
  $('inputs').querySelectorAll('[data-wkt]').forEach(t => t.onchange = () => applyWkt(t.dataset.wkt, t.value.trim()));
  refreshShown();
  $('inputs').querySelectorAll('[data-clear]').forEach(b => b.onclick = () => {
    delete state.inputs[b.dataset.clear];
    delete state.wkt[b.dataset.clear];
    delete state.shown[b.dataset.clear];
    state.fixture = null;
    view.setInputs(state.inputs, false);
    renderInputs();
    schedule();
  });
}

/** A pasted WKT text becomes the input: the server reads it, and the map gets the GeoJSON for it. */
async function applyWkt(key, text) {
  const message = $('inputs').querySelector(`[data-wkt-msg="${key}"]`);
  if (!text) {
    delete state.inputs[key];
    delete state.wkt[key];
    delete state.shown[key];
    state.fixture = null;
    view.setInputs(state.inputs, false);
    renderInputs();
    renderStripState();
    schedule();
    return;
  }

  try {
    const { geometry } = await api.convert({ wkt: text });
    state.inputs[key] = geometry;
    state.wkt[key] = text;
    state.fixture = null;
    view.setInputs(state.inputs, true);
    renderInputs();
    renderStripState();
    schedule();
  } catch (e) {
    if (message) message.innerHTML = `<span class="warn">⚠ ${escape(e.message)}</span>`;
  }
}

/** Shows, for inputs without a WKT text of their own, the WKT that the server makes from the GeoJSON. */
let shownSequence = 0;
async function refreshShown() {
  const id = ++shownSequence;
  for (const input of state.algorithm.inputs) {
    const key = input.key, geometry = state.inputs[key];
    if (!geometry || state.wkt[key]) continue;
    try {
      const { wkt } = await api.convert({ geometry });
      if (id !== shownSequence) return;
      state.shown[key] = wkt;
      const box = $('inputs').querySelector(`[data-wkt="${key}"]`);
      if (box && document.activeElement !== box) box.value = wkt;
    } catch { /* the display is only a bonus */ }
  }
}

function upload(target) {
  const picker = document.createElement('input');
  picker.type = 'file';
  picker.accept = '.json,.geojson,application/geo+json';
  picker.onchange = async () => {
    try {
      const { geometries } = await api.importGeoJson(picker.files[0]);
      if (!geometries.length) throw new Error('The file contains no geometry.');
      // Two geometries in one file fill A and B, otherwise only the target.
      const keys = geometries.length > 1 && state.algorithm.inputs.length > 1 ? state.algorithm.inputs.map(i => i.key) : [target];
      if (state.local) setMode(false);
      keys.forEach((k, i) => { state.inputs[k] = geometries[i]; delete state.wkt[k]; });
      state.fixture = null;
      view.setInputs(state.inputs, true);
      renderInputs();
      renderStripState();
      schedule();
    } catch (e) {
      showResult(state.last, e.message);
    }
  };
  picker.click();
}

// ---------- Mode and computation CRS ----------

KBS.forEach(([v, label]) => $('kbs').add(new Option(label, v)));

function renderKbs() {
  const locked = state.local || !!state.fixture?.crs;
  $('kbs').disabled = locked;
  $('kbs').title = state.local ? 'local planar: no projection' : state.fixture?.crs ? `set by the example (${state.fixture.crs})` : '';
  $('kbs').value = state.kbs;
  $('kbs-free').hidden = state.kbs !== 'free' || locked;
  $('mode').value = state.local ? 'local' : 'geo';
}

function setMode(local) {
  state.local = local;
  view.setMode(local);
  renderKbs();
  if (state.algorithm) renderParams();
}

$('mode').onchange = () => {
  // Mode change: the coordinates no longer fit the map, the inputs are cleared.
  state.inputs = {};
  state.wkt = {};
  state.shown = {};
  state.fixture = null;
  state.last = null;
  setMode($('mode').value === 'local');
  view.setInputs({}, false);
  renderInputs();
  renderStripState();
  schedule();
};
$('kbs').onchange = () => { state.kbs = $('kbs').value; renderKbs(); schedule(); };
$('kbs-free').onchange = schedule;
document.querySelectorAll('[data-show]').forEach(c => c.onchange = () => view.setVisible(c.dataset.show, c.checked));

// ---------- Fixtures ----------

$('all-fixtures').onchange = async () => { state.allFixtures = $('all-fixtures').checked; await loadFixtures(); };

async function loadFixtures() {
  state.fixtures = await api.fixtures(state.algorithm.id, state.allFixtures);
  renderStripState();
}

function renderStripState() {
  renderStrip($('strip'), state.fixtures, state.fixture?.id, loadFixture);
}

function loadFixture(fixture) {
  if (fixture.error) {
    showResult(state.last, `Example cannot be shown: ${fixture.error}`);
    return;
  }

  state.fixture = fixture;
  state.inputs = {};
  if (fixture.a) state.inputs.a = fixture.a;
  if (fixture.b) state.inputs.b = fixture.b;
  // examples with M come with their WKT, because GeoJSON carries no M (ADR 0015)
  state.wkt = {};
  if (fixture.aWkt) state.wkt.a = fixture.aWkt;
  if (fixture.bWkt) state.wkt.b = fixture.bWkt;
  state.shown = {};
  state.params = { ...defaults(state.algorithm), ...fixture.params };
  setMode(fixture.local);
  view.setInputs(state.inputs, true);
  state.fitOnResult = true;
  renderParams();
  renderInputs();
  renderStripState();
  schedule();
}

// ---------- Choosing an algorithm ----------

const defaults = alg => Object.fromEntries(alg.parameters.map(p => [p.key, p.default]));

async function choose(id) {
  state.algorithm = state.algorithms.find(a => a.id === id);
  state.params = defaults(state.algorithm);
  state.last = null;
  history.replaceState(null, '', `#${id}`);
  $('search').value = '';
  $('search').blur();
  $('alg-name').textContent = state.algorithm.name;
  $('alg-group').textContent = state.algorithm.group;
  // the inputs stay as far as the new algorithm takes them.
  state.inputs = Object.fromEntries(state.algorithm.inputs.filter(i => state.inputs[i.key]).map(i => [i.key, state.inputs[i.key]]));
  state.wkt = Object.fromEntries(Object.entries(state.wkt).filter(([k]) => state.inputs[k]));
  state.shown = {};
  state.fixture = null;
  renderParams();
  renderInputs();
  await loadFixtures();
  if (!Object.keys(state.inputs).length && state.fixtures.length) {
    loadFixture(state.fixtures.find(f => !f.error) ?? state.fixtures[0]);
  } else {
    view.setInputs(state.inputs, false);
    schedule();
  }
}

// ---------- Start ----------

state.algorithms = await api.algorithms();
setMode(true);
await choose(state.algorithms.some(a => a.id === location.hash.slice(1)) ? location.hash.slice(1) : 'buffer');
window.addEventListener('hashchange', () => {
  const id = location.hash.slice(1);
  if (id !== state.algorithm?.id && state.algorithms.some(a => a.id === id)) choose(id);
});
