// Map: WGS84 with OSM tiles, or local planar with L.CRS.Simple (ADR 0010); drawing with Geoman.

/* global L */

export const COLORS = { a: '#2563eb', b: '#ea580c', r: '#16a34a' };

/** Colours for list results (e.g. Cut): every part has its own colour and a number. */
const PART_COLORS = ['#16a34a', '#9333ea', '#0891b2', '#db2777', '#ca8a04', '#4f46e5', '#dc2626', '#059669'];

const DRAW_MODES = { Point: 'Marker', MultiPoint: 'Marker', LineString: 'Line', Polygon: 'Polygon' };

export class MapView {
  /**
   * @param {HTMLElement} element
   * @param {(key: string, geometry: object) => void} onInput A new or edited input.
   */
  constructor(element, onInput) {
    this.element = element;
    this.onInput = onInput;
    this.local = null;
    this.drawTarget = 'a';
    this.show = { a: true, b: true, r: true };
    this.inputs = {};
    this.result = null;
  }

  /** Rebuilds the map when the mode changes. */
  setMode(local) {
    if (this.local === local && this.map) return;
    this.local = local;
    this.map?.remove();
    this.element.classList.toggle('local', local);
    this.map = L.map(this.element, local
      ? { crs: L.CRS.Simple, minZoom: -20, maxZoom: 20, zoomSnap: 0.25, attributionControl: false }
      : { minZoom: 1, maxZoom: 22 });
    if (local) {
      this.map.setView([0, 0], 0);
    } else {
      L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 22, maxNativeZoom: 19, attribution: '© OpenStreetMap contributors',
      }).addTo(this.map);
      this.map.setView([47.07, 15.44], 13);
    }

    this.map.pm.setLang('en');
    this.map.pm.addControls({
      position: 'topleft', drawMarker: false, drawCircle: false, drawCircleMarker: false, drawText: false,
      drawPolyline: false, drawPolygon: false, drawRectangle: false, cutPolygon: false, rotateMode: false,
      editMode: true, dragMode: true, removalMode: false,
    });
    this.map.on('pm:create', e => {
      const geometry = this.#merge(this.drawTarget, e.layer.toGeoJSON().geometry);
      e.layer.remove();
      this.onInput(this.drawTarget, geometry);
    });

    this.inputLayer = L.featureGroup().addTo(this.map);
    this.resultLayer = L.featureGroup().addTo(this.map);
    this.#renderInputs();
    this.#renderResult();
  }

  /** Starts drawing a type for input a or b. */
  draw(target, type) {
    this.drawTarget = target;
    this.drawType = type;
    this.map.pm.enableDraw(DRAW_MODES[type], {
      templineStyle: { color: COLORS[target] }, hintlineStyle: { color: COLORS[target], dashArray: '5,5' },
      pathOptions: { color: COLORS[target] },
    });
  }

  /** Several drawn points make a MultiPoint for MultiPoint inputs. */
  #merge(target, geometry) {
    const current = this.inputs[target];
    if (this.drawType !== 'MultiPoint' || geometry.type !== 'Point') return geometry;
    if (current?.type === 'MultiPoint') return { type: 'MultiPoint', coordinates: [...current.coordinates, geometry.coordinates] };
    if (current?.type === 'Point') return { type: 'MultiPoint', coordinates: [current.coordinates, geometry.coordinates] };
    return { type: 'MultiPoint', coordinates: [geometry.coordinates] };
  }

  setInputs(inputs, fit) {
    this.inputs = { ...inputs };
    if (!this.map) return;
    this.#renderInputs();
    if (fit) this.fit();
  }

  setResult(result) {
    this.result = result;
    if (this.map) this.#renderResult();
  }

  setVisible(key, visible) {
    this.show[key] = visible;
    this.#renderInputs();
    this.#renderResult();
  }

  fit() {
    const bounds = L.featureGroup([...this.inputLayer.getLayers(), ...this.resultLayer.getLayers()]).getBounds();
    if (bounds.isValid()) this.map.fitBounds(bounds.pad(0.25), { maxZoom: this.local ? 20 : 19 });
  }

  #renderInputs() {
    this.inputLayer.clearLayers();
    for (const key of ['b', 'a']) {
      const geometry = this.inputs[key];
      if (!geometry || !this.show[key]) continue;
      const layer = L.geoJSON(geometry, {
        style: { color: COLORS[key], weight: 2, fillOpacity: 0.08 },
        pointToLayer: (_, latlng) => L.circleMarker(latlng, { radius: 5, color: COLORS[key] }),
      });
      // Editing (Geoman edit/drag) writes the input back.
      layer.on('pm:edit pm:dragend', () => this.onInput(key, combine(layer)));
      layer.addTo(this.inputLayer);
    }
  }

  #renderResult() {
    this.resultLayer.clearLayers();
    const r = this.result;
    if (!r || !this.show.r) return;
    const style = color => ({ color, weight: 2.5, fillOpacity: 0.35, pmIgnore: true });
    const add = (geometry, color, label) => {
      const layer = L.geoJSON(geometry, {
        pmIgnore: true, style: style(color),
        pointToLayer: (_, latlng) => L.circleMarker(latlng, { radius: 6, color, fillOpacity: 0.8, pmIgnore: true }),
      }).addTo(this.resultLayer);
      if (label !== undefined) {
        layer.bindTooltip(String(label), { permanent: true, direction: 'center', className: 'part-label' });
      }
    };

    if (r.kind === 'geometry' && r.geometry) add(r.geometry, COLORS.r);
    if (r.kind === 'geometries') r.geometries.forEach((g, i) => add(g, PART_COLORS[i % PART_COLORS.length], i + 1));
    if (r.kind === 'validation') {
      r.issues.filter(i => i.location).forEach(i => {
        L.geoJSON(i.location, {
          pmIgnore: true,
          pointToLayer: (_, latlng) => L.circleMarker(latlng, { radius: 7, color: '#b91c1c', fillOpacity: 0.9, pmIgnore: true }),
        }).bindTooltip(i.kind).addTo(this.resultLayer);
      });
    }
  }
}

/** GeoJSON geometry of an edited layer (which may have several parts). */
function combine(layer) {
  const features = layer.toGeoJSON().features ?? [];
  return features.length === 1 ? features[0].geometry : { type: 'GeometryCollection', geometries: features.map(f => f.geometry) };
}
