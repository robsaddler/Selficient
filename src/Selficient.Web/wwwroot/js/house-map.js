// @ts-check
/**
 * The house on the map. The server renders the postcode centre and a plain sentence; this module
 * asks whether the basemap is installed and, only then, loads MapLibre and draws the street with a
 * pin the household can move onto their own roof. The pin's position is kept in this browser and
 * nowhere else (ADR-001): the only request this file makes is the status probe.
 */
import * as store from "selficient/plan-store.js";
import { buildStyle, fetchBasemap } from "selficient/map/basemap-style.js";

const LANTERN = "#E0A458";
const STREET_ZOOM = 16;
const ROOF_ZOOM = 17;

/** @typedef {{ lat: number, lng: number }} Position */

/**
 * @param {Document} doc
 */
export async function initHouseMap(doc) {
  const frame = doc.querySelector("[data-house-map]");
  if (!(frame instanceof HTMLElement) || frame.dataset.covered !== "true") {
    return;
  }
  const canvas = frame.querySelector("[data-map-canvas]");
  const fallback = frame.querySelector("[data-map-fallback]");
  const tools = doc.querySelector("[data-map-tools]");
  const status = doc.querySelector("[data-map-status]");
  const centreButton = doc.querySelector("[data-map-centre]");
  const resetButton = doc.querySelector("[data-map-reset]");
  if (!(canvas instanceof HTMLElement) || !(fallback instanceof HTMLElement) || !(tools instanceof HTMLElement) || !(status instanceof HTMLElement)) {
    return;
  }

  const postcode = { lat: Number(frame.dataset.lat), lng: Number(frame.dataset.lng) };
  if (!Number.isFinite(postcode.lat) || !Number.isFinite(postcode.lng)) {
    return;
  }

  const basemap = await fetchBasemap();
  if (!basemap.available) {
    return;
  }

  // The library is a quarter of a megabyte: fetched only once the panel is near the viewport.
  await whenNear(frame);
  const maplibre = await loadMapLibre(doc);
  const saved = await store.getHouse().catch(() => null);
  const start = saved ?? postcode;

  fallback.hidden = true;
  canvas.hidden = false;
  tools.hidden = false;

  const map = new maplibre.Map({
    container: canvas,
    style: buildStyle(basemap, currentTheme(doc)),
    center: [start.lng, start.lat],
    zoom: saved ? ROOF_ZOOM : STREET_ZOOM,
    minZoom: 6,
    maxZoom: 19,
    attributionControl: false,
    dragRotate: false,
    pitchWithRotate: false,
  });
  map.addControl(new maplibre.AttributionControl({ compact: false, customAttribution: basemap.attribution ?? "" }));
  map.addControl(new maplibre.NavigationControl({ showCompass: false }), "top-right");
  map.addControl(new maplibre.ScaleControl({ unit: "metric" }), "bottom-left");

  const pin = new maplibre.Marker({ draggable: true, color: LANTERN }).setLngLat([start.lng, start.lat]).addTo(map);
  status.textContent = saved
    ? "Your pin is where you left it. Drag it, or tap the roof, to move it."
    : "Drag the pin onto your roof, or tap the roof. Only this browser remembers where it is.";

  /** @param {Position} position */
  const place = async (position) => {
    pin.setLngLat([position.lng, position.lat]);
    await store.saveHouse(position).catch(() => undefined);
    status.textContent = "Pin saved in this browser.";
  };

  pin.on("dragend", () => {
    const at = pin.getLngLat();
    void place({ lat: at.lat, lng: at.lng });
  });
  map.on("click", (event) => {
    void place({ lat: event.lngLat.lat, lng: event.lngLat.lng });
  });
  centreButton?.addEventListener("click", () => {
    const at = map.getCenter();
    void place({ lat: at.lat, lng: at.lng });
  });
  resetButton?.addEventListener("click", () => {
    pin.setLngLat([postcode.lng, postcode.lat]);
    map.easeTo({ center: [postcode.lng, postcode.lat], zoom: STREET_ZOOM });
    void store.clearHouse().catch(() => undefined);
    status.textContent = "Back at the centre of your postcode.";
  });

  watchTheme(doc, (theme) => map.setStyle(buildStyle(basemap, theme)));
}

/**
 * Resolves once the element is within a screen of the viewport.
 * @param {Element} element
 * @returns {Promise<void>}
 */
function whenNear(element) {
  return new Promise((resolve) => {
    if (!("IntersectionObserver" in window)) {
      resolve();
      return;
    }
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) {
        observer.disconnect();
        resolve();
      }
    }, { rootMargin: "100% 0px" });
    observer.observe(element);
  });
}

/**
 * Loads the vendored library and its stylesheet from this site.
 * @param {Document} doc
 */
async function loadMapLibre(doc) {
  if (!doc.querySelector('link[href="/lib/maplibre/maplibre-gl.css"]')) {
    const link = doc.createElement("link");
    link.rel = "stylesheet";
    link.href = "/lib/maplibre/maplibre-gl.css";
    doc.head.append(link);
  }
  return await import("maplibre-gl");
}

/**
 * @param {Document} doc
 * @returns {"light" | "dark"}
 */
function currentTheme(doc) {
  const chosen = doc.documentElement.dataset.theme;
  if (chosen === "dark" || chosen === "light") {
    return chosen;
  }
  return matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

/**
 * @param {Document} doc
 * @param {(theme: "light" | "dark") => void} onChange
 */
function watchTheme(doc, onChange) {
  let last = currentTheme(doc);
  const check = () => {
    const now = currentTheme(doc);
    if (now !== last) {
      last = now;
      onChange(now);
    }
  };
  new MutationObserver(check).observe(doc.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
  matchMedia("(prefers-color-scheme: dark)").addEventListener("change", check);
}
