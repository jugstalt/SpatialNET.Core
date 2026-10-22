// Example strip: preview cards of the fixtures (SVG from the GeoJSON inputs).

import { COLORS } from './map.js';

const W = 164, H = 64, PAD = 5;

function lines(geometry) {
  switch (geometry?.type) {
    case 'Point': return [[geometry.coordinates]];
    case 'MultiPoint': return geometry.coordinates.map(c => [c]);
    case 'LineString': return [geometry.coordinates];
    case 'MultiLineString': case 'Polygon': return geometry.coordinates;
    case 'MultiPolygon': return geometry.coordinates.flat();
    default: return [];
  }
}

const escape = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);

export function thumbnail(fixture) {
  const parts = [['a', fixture.a], ['b', fixture.b]].filter(([, g]) => g);
  const coords = parts.flatMap(([, g]) => lines(g).flat()).filter(c => Number.isFinite(c[0]) && Number.isFinite(c[1]));
  if (!coords.length) return `<svg viewBox="0 0 ${W} ${H}"></svg>`;
  const xs = coords.map(c => c[0]), ys = coords.map(c => c[1]);
  const minX = Math.min(...xs), minY = Math.min(...ys);
  const s = Math.min((W - 2 * PAD) / ((Math.max(...xs) - minX) || 1), (H - 2 * PAD) / ((Math.max(...ys) - minY) || 1));
  const P = c => `${(PAD + (c[0] - minX) * s).toFixed(1)},${(H - PAD - (c[1] - minY) * s).toFixed(1)}`;
  const svg = parts.map(([key, g]) => {
    const color = COLORS[key], polygon = /Polygon/.test(g.type);
    return lines(g).map(l => l.length === 1
      ? `<circle cx="${P(l[0]).split(',')[0]}" cy="${P(l[0]).split(',')[1]}" r="2.5" fill="${color}"/>`
      : `<polyline points="${l.map(P).join(' ')}" fill="${polygon ? color : 'none'}" fill-opacity="0.15" stroke="${color}" stroke-width="1.2"/>`).join('');
  }).join('');
  return `<svg viewBox="0 0 ${W} ${H}">${svg}</svg>`;
}

/** Renders the strip; a click on a card loads the fixture. */
export function renderStrip(element, fixtures, selectedId, onSelect) {
  element.innerHTML = fixtures.length
    ? fixtures.map(f => `
      <div class="card ${f.id === selectedId ? 'sel' : ''}" data-id="${escape(f.id)}" title="${escape(f.description ?? f.name)}">
        ${thumbnail(f)}<b>${escape(f.name)}</b>
        <div class="tags">${escape(f.local ? 'local' : f.crs)} · ${f.tags.map(escape).join(' ')}</div>
      </div>`).join('')
    : '<div class="card muted">No examples – draw or upload GeoJSON.</div>';
  element.querySelectorAll('[data-id]').forEach(card => card.onclick = () => onSelect(fixtures.find(f => f.id === card.dataset.id)));
}

export { escape };
