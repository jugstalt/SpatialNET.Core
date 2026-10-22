// SpatialNET Playground, Variante „Karte zuerst“ (ADR 0011). Die UI kennt keinen Algorithmus fest:
// Palette, Parameter und Eingaben entstehen aus GET /api/algorithms.

import * as api from './api.js';
import { MapView } from './map.js';
import { renderStrip, escape } from './strip.js';

const KBS = [
  ['auto', 'Auto-UTM'], ['EPSG:3857', 'EPSG:3857 (Web Mercator)'], ['EPSG:25832', 'EPSG:25832 (ETRS89/UTM 32N)'],
  ['EPSG:25833', 'EPSG:25833 (ETRS89/UTM 33N)'], ['EPSG:31256', 'EPSG:31256 (MGI/GK Ost)'], ['free', 'freier EPSG-Code …'],
];
const DEBOUNCE_MS = 300;

const state = {
  algorithms: [],
  algorithm: null,
  params: {},
  inputs: {},
  wkt: {},        // WKT-Text je Eingabe, der gilt (eingefügt oder aus einem Beispiel mit M); Zeichnen und Hochladen verwerfen ihn
  shown: {},      // WKT-Text zur Anzeige für Eingaben ohne eigenen WKT-Text (vom Server aus dem GeoJSON erzeugt)
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
  delete state.wkt[key]; // eine gezeichnete oder verschobene Geometrie hat kein M mehr
  state.fixture = null;
  view.setInputs(state.inputs, false);
  renderInputs();
  renderKbs();
  renderStripState();
  schedule();
});

// ---------- Rechnen (Auto-Run mit Entprellung, veraltete Antworten verwerfen) ----------

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
    $('result').innerHTML = `<div class="muted">Eingabe ${missing.map(i => escape(i.label)).join(', ')} fehlt – zeichnen, hochladen oder Beispiel wählen.</div>`;
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
    showResult(state.last, e.message); // Fehler als Chip, das letzte Ergebnis bleibt stehen
  }
}

// ---------- Ergebnis-Chips ----------

const fmt = v => typeof v === 'number' ? v.toLocaleString('de-DE', { maximumFractionDigits: 6 }) : escape(v);
// sehr kleine Abstände (Rundungsgröße) nicht zu 0 runden
const fmtSmall = v => v !== 0 && Math.abs(v) < 1e-4 ? v.toExponential(2).replace('.', ',') : fmt(v);

function showResult(result, error) {
  $('error').hidden = !error;
  $('error').textContent = error ? `⚠ ${error}` : '';
  view.setResult(result);
  $('kbs-label').textContent = result ? `Rechen-KBS: ${result.kbs}${result.unit ? ` · Einheit ${result.unit}` : ''}` : '';
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
      if (typeof result.distance === 'number') html += `<div class="muted">Abstand A–B: ${fmtSmall(result.distance)}${u}</div>`;
      if (result.nearlyTouching) {
        html += '<div class="warn">Die Geometrien liegen praktisch aufeinander, aber nicht exakt: Prädikate rechnen exakt auf den Koordinaten. '
          + 'Ein gezeichneter Punkt „auf“ einer Linie liegt rechnerisch fast nie genau darauf. Für „ungefähr“ dient IsWithinDistance.</div>';
      }

      if (result.alternative) {
        html += `<div class="muted">Mit Toleranz t: <code>${escape(result.alternative)}</code></div>`;
      }

      break;
    case 'matrix': html = `<div class="value" style="font-family:ui-monospace,monospace">${escape(result.value)}</div><div class="muted">DE-9IM</div>`; break;
    case 'none': html = `<div class="muted">${escape(result.text)}</div>`; break;
    case 'validation':
      html = `<div class="value">${result.value ? 'gültig' : 'ungültig'}</div>`
        + result.issues.map(i => `<div class="warn">${escape(i.kind)}</div>`).join('');
      break;
    default:
      if (result.text) html += `<div class="value">${escape(result.text)}</div>`;
      if (result.kind === 'geometries') html += `<div class="muted">${result.geometries.length} Teile (nummeriert)</div>`;
  }

  if (result.stats) {
    const s = result.stats;
    html += `<table>
      <tr><td>Fläche</td><td>${fmt(s.area)}${u2}</td></tr>
      <tr><td>Länge</td><td>${fmt(s.length)}${u}</td></tr>
      <tr><td>Vertices</td><td>${fmt(s.vertices)}</td></tr>
      <tr><td>Teile</td><td>${fmt(s.parts)}</td></tr></table>`;
  }

  html += (result.warnings ?? []).map(w => `<div class="warn">⚠ ${escape(w)}</div>`).join('');
  const wktText = result.wkts ? result.wkts.join('\n') : result.wkt;
  if (wktText) {
    html += `<details class="wkt" ${state.resultWktOpen ? 'open' : ''}><summary>WKT</summary>
      <textarea readonly rows="4" spellcheck="false">${escape(wktText)}</textarea>
      <button data-copy>Kopieren</button></details>`;
  }

  $('result').innerHTML = html;
  const box = $('result').querySelector('details.wkt');
  if (box) {
    box.ontoggle = () => { state.resultWktOpen = box.open; };
    box.querySelector('[data-copy]').onclick = async e => {
      await navigator.clipboard?.writeText(wktText);
      e.target.textContent = 'Kopiert';
      setTimeout(() => { e.target.textContent = 'Kopieren'; }, 1200);
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
  return state.local ? 'Einheiten' : (state.last?.unit ?? 'KBS-Einheit');
}

function renderParams() {
  const alg = state.algorithm, box = $('params');
  box.innerHTML = '';
  if (!alg.parameters.length) {
    box.innerHTML = '<div class="muted">keine Parameter</div>';
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

// ---------- Eingaben: Zeichnen (Ziel A/B), Upload, Leeren ----------

const TYPE_LABELS = { Point: 'Punkt', MultiPoint: 'Punkte', LineString: 'Linie', Polygon: 'Polygon' };

function describe(geometry) {
  return geometry ? `<span class="muted">${escape(geometry.type)}</span>` : '<span class="muted">leer</span>';
}

function renderInputs() {
  const alg = state.algorithm;
  $('inputs').innerHTML = alg.inputs.map(i => `
    <div class="input-row">
      <span class="name ${i.key}">${escape(i.label)}</span>
      ${i.types.map(t => `<button data-draw="${i.key}:${t}" title="${TYPE_LABELS[t]} zeichnen">✎ ${TYPE_LABELS[t]}</button>`).join('')}
      <button data-upload="${i.key}" title="GeoJSON hochladen">⇪</button>
      <button data-clear="${i.key}" title="leeren">✕</button>
      ${describe(state.inputs[i.key])}
    </div>
    <details class="wkt" data-wkt-box="${i.key}" ${state.wktOpen ? 'open' : ''}>
      <summary title="Als WKT lesen und schreiben, auch mit Z und M. Auf der Karte bearbeiten verwirft M.">WKT</summary>
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

/** Ein eingefügter WKT-Text wird zur Eingabe: der Server liest ihn, die Karte bekommt das GeoJSON dazu. */
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

/** Zeigt für Eingaben ohne eigenen WKT-Text das WKT, das der Server aus dem GeoJSON erzeugt. */
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
    } catch { /* die Anzeige ist nur Beigabe */ }
  }
}

function upload(target) {
  const picker = document.createElement('input');
  picker.type = 'file';
  picker.accept = '.json,.geojson,application/geo+json';
  picker.onchange = async () => {
    try {
      const { geometries } = await api.importGeoJson(picker.files[0]);
      if (!geometries.length) throw new Error('Die Datei enthält keine Geometrie.');
      // Zwei Geometrien in einer Datei füllen A und B, sonst nur das Ziel.
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

// ---------- Modus und Rechen-KBS ----------

KBS.forEach(([v, label]) => $('kbs').add(new Option(label, v)));

function renderKbs() {
  const locked = state.local || !!state.fixture?.crs;
  $('kbs').disabled = locked;
  $('kbs').title = state.local ? 'lokal planar: keine Projektion' : state.fixture?.crs ? `durch Fixture festgelegt (${state.fixture.crs})` : '';
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
  // Moduswechsel: Koordinaten passen nicht mehr zur Karte, Eingaben werden geleert.
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
    showResult(state.last, `Fixture nicht darstellbar: ${fixture.error}`);
    return;
  }

  state.fixture = fixture;
  state.inputs = {};
  if (fixture.a) state.inputs.a = fixture.a;
  if (fixture.b) state.inputs.b = fixture.b;
  // Beispiele mit M kommen mit ihrem WKT, denn GeoJSON trägt kein M (ADR 0015)
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

// ---------- Algorithmus wählen ----------

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
  // Eingaben bleiben, soweit der neue Algorithmus sie nimmt.
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
