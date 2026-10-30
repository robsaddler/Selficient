// @ts-check
/**
 * The MapLibre style for the self-hosted OS Open Zoomstack basemap, in Selficient's own palette:
 * the sky ground, pine for woodland, contour grey for buildings and the contour lines themselves,
 * and lantern only on the roads that matter. Adapted from SiteWise's basemap (Oct 2026) so the
 * layer and field names are known to be right: the place name field is `name1`, not `name`.
 *
 * MapLibre styles take hex colours, not CSS tokens, so the two palettes are written out here and
 * chosen by theme. Nothing in this file talks to the network except through the tile URLs it
 * returns, which are always same-origin.
 */

/** @typedef {{ available: boolean, minZoom?: number, maxZoom?: number, attribution?: string }} BasemapStatus */
/** @typedef {Record<string, unknown>} StyleSpecification */

const LIGHT = {
  land: "#eef2f1",
  sea: "#b9d0d9",
  water: "#b9d0d9",
  foreshore: "#e3e6e2",
  woodland: "#cfe0d2",
  park: "#dde9df",
  green: "#e3ede4",
  urban: "#e6eae8",
  building: "#d3dad8",
  buildingEdge: "#b7c2c0",
  contour: "#c9d3d2",
  rail: "#9aa5a3",
  boundary: "#a8a1b8",
  motorway: "#e9b96a",
  motorwayEdge: "#c8974a",
  aRoad: "#f2d696",
  aRoadEdge: "#d6b86d",
  bRoad: "#ffffff",
  roadEdge: "#c3cccb",
  minor: "#ffffff",
  label: "#2f3b40",
  labelHalo: "#f6f8f8",
  roadNumber: "#5f5240",
  waterLabel: "#3d6a7f",
};

const DARK = {
  land: "#1e2a30",
  sea: "#17313c",
  water: "#17313c",
  foreshore: "#243036",
  woodland: "#243b32",
  park: "#223530",
  green: "#22322f",
  urban: "#26323a",
  building: "#36454c",
  buildingEdge: "#4b5b63",
  contour: "#3b4a52",
  rail: "#6c7a80",
  boundary: "#6d6482",
  motorway: "#b98b3f",
  motorwayEdge: "#8c6628",
  aRoad: "#a08c57",
  aRoadEdge: "#7d6c3e",
  bRoad: "#4a5a62",
  roadEdge: "#2b373d",
  minor: "#45545c",
  label: "#e4ebec",
  labelHalo: "#1e2a30",
  roadNumber: "#d9c9a3",
  waterLabel: "#8fc0d6",
};

/**
 * @param {BasemapStatus} basemap
 * @param {"light" | "dark"} theme
 * @returns {StyleSpecification}
 */
export function buildStyle(basemap, theme) {
  const palette = theme === "dark" ? DARK : LIGHT;
  return {
    version: 8,
    // MapLibre cannot draw one character without a glyphs URL. These are served from this site.
    glyphs: `${location.origin}/fonts/glyphs/{fontstack}/{range}.pbf`,
    sources: {
      basemap: {
        type: "vector",
        tiles: [`${location.origin}/api/tiles/{z}/{x}/{y}.pbf`],
        minzoom: basemap.minZoom ?? 0,
        maxzoom: basemap.maxZoom ?? 14,
        attribution: basemap.attribution ?? "",
      },
    },
    // Land is the ground, so a house beyond the archive's edge still sits on something.
    layers: [{ id: "bg", type: "background", paint: { "background-color": palette.land } }, ...baseLayers(palette)],
  };
}

/**
 * @param {typeof LIGHT} p
 * @returns {Record<string, unknown>[]}
 */
function baseLayers(p) {
  const src = { source: "basemap" };
  /** @param {number} z0 @param {number} w0 @param {number} z1 @param {number} w1 */
  const width = (z0, w0, z1, w1) => ["interpolate", ["linear"], ["zoom"], z0, w0, z1, w1];
  return [
    { id: "sea", type: "fill", ...src, "source-layer": "sea", paint: { "fill-color": p.sea } },
    { id: "foreshore", type: "fill", ...src, "source-layer": "foreshore", paint: { "fill-color": p.foreshore } },
    { id: "national-parks", type: "fill", ...src, "source-layer": "national_parks", paint: { "fill-color": p.park, "fill-opacity": 0.6 } },
    { id: "woodland", type: "fill", ...src, "source-layer": "woodland", paint: { "fill-color": p.woodland } },
    { id: "urban", type: "fill", ...src, "source-layer": "urban_areas", paint: { "fill-color": p.urban } },
    { id: "greenspaces", type: "fill", ...src, "source-layer": "greenspaces", minzoom: 9, paint: { "fill-color": p.green } },
    { id: "surfacewater", type: "fill", ...src, "source-layer": "surfacewater", paint: { "fill-color": p.water } },

    // The contour is the whole point of this map: it is the one on the household's own hero.
    { id: "contours", type: "line", ...src, "source-layer": "contours", minzoom: 12,
      paint: { "line-color": p.contour, "line-width": 0.8, "line-opacity": ["interpolate", ["linear"], ["zoom"], 12, 0.4, 14, 0.9] } },

    { id: "waterlines", type: "line", ...src, "source-layer": "waterlines", minzoom: 9, paint: { "line-color": p.water, "line-width": width(9, 0.5, 14, 2.5) } },
    { id: "buildings", type: "fill", ...src, "source-layer": "buildings", minzoom: 14, paint: { "fill-color": p.building, "fill-outline-color": p.buildingEdge } },
    { id: "boundaries", type: "line", ...src, "source-layer": "boundaries", paint: { "line-color": p.boundary, "line-width": 1, "line-dasharray": [3, 2], "line-opacity": 0.7 } },
    { id: "rail", type: "line", ...src, "source-layer": "rail", minzoom: 9, paint: { "line-color": p.rail, "line-width": 0.9, "line-dasharray": [4, 2] } },

    // Roads are drawn casing first so every class has an edge against the land.
    { id: "roads-minor-casing", type: "line", ...src, "source-layer": "roads", minzoom: 13,
      filter: ["in", ["get", "type"], ["literal", ["Minor", "Local Street", "Restricted", "Alley"]]],
      paint: { "line-color": p.roadEdge, "line-width": width(13, 1.6, 16, 6) } },
    { id: "roads-minor", type: "line", ...src, "source-layer": "roads", minzoom: 13,
      filter: ["in", ["get", "type"], ["literal", ["Minor", "Local Street", "Restricted", "Alley"]]],
      paint: { "line-color": p.minor, "line-width": width(13, 0.8, 16, 4) } },
    { id: "roads-b-casing", type: "line", ...src, "source-layer": "roads", minzoom: 9,
      filter: ["==", ["get", "type"], "B Road"], paint: { "line-color": p.roadEdge, "line-width": width(9, 1.4, 16, 8) } },
    { id: "roads-b", type: "line", ...src, "source-layer": "roads", minzoom: 9,
      filter: ["==", ["get", "type"], "B Road"], paint: { "line-color": p.bRoad, "line-width": width(9, 0.7, 16, 5.5) } },
    { id: "roads-a-casing", type: "line", ...src, "source-layer": "roads",
      filter: ["in", ["get", "type"], ["literal", ["A Road", "Primary"]]],
      paint: { "line-color": p.aRoadEdge, "line-width": ["interpolate", ["linear"], ["zoom"], 6, 1.2, 10, 3, 16, 11] } },
    { id: "roads-a", type: "line", ...src, "source-layer": "roads",
      filter: ["in", ["get", "type"], ["literal", ["A Road", "Primary"]]],
      paint: { "line-color": p.aRoad, "line-width": ["interpolate", ["linear"], ["zoom"], 6, 0.6, 10, 1.8, 16, 8] } },
    { id: "roads-motorway-casing", type: "line", ...src, "source-layer": "roads",
      filter: ["==", ["get", "type"], "Motorway"],
      paint: { "line-color": p.motorwayEdge, "line-width": ["interpolate", ["linear"], ["zoom"], 6, 2, 10, 4.5, 16, 13] } },
    { id: "roads-motorway", type: "line", ...src, "source-layer": "roads",
      filter: ["==", ["get", "type"], "Motorway"],
      paint: { "line-color": p.motorway, "line-width": ["interpolate", ["linear"], ["zoom"], 6, 1.2, 10, 3, 16, 10] } },

    { id: "road-numbers", type: "symbol", ...src, "source-layer": "roads", minzoom: 11,
      filter: ["all", ["has", "number"], ["in", ["get", "type"], ["literal", ["Motorway", "A Road", "Primary", "B Road"]]]],
      layout: { "symbol-placement": "line", "text-field": ["get", "number"], "text-font": ["Open Sans Semibold"], "text-size": 10, "symbol-spacing": 220 },
      paint: { "text-color": p.roadNumber, "text-halo-color": p.labelHalo, "text-halo-width": 1.4 } },

    // Greenspace names are dropped: at street zoom they fill the screen with the layer's own
    // category words. Suburban areas stay, because that is how people say where they live.
    { id: "placenames", type: "symbol", ...src, "source-layer": "names",
      filter: ["in", ["get", "type"], ["literal", ["Country", "Capital", "City", "Town", "Village", "Hamlet", "Small Settlements", "National Park", "Suburban Area", "Sites"]]],
      layout: {
        "text-field": ["coalesce", ["get", "name1"], ["get", "name2"]],
        "text-font": ["Open Sans Semibold"],
        "text-size": ["match", ["get", "type"], "Country", 20, "Capital", 17, "City", 15, "Town", 13, "National Park", 12, "Village", 11, "Suburban Area", 11, "Hamlet", 10, "Sites", 10, 10],
        "text-max-width": 8,
        "text-padding": 3,
        "symbol-sort-key": ["match", ["get", "type"], "Country", 0, "Capital", 1, "City", 2, "Town", 3, "National Park", 4, "Village", 5, "Suburban Area", 6, "Hamlet", 7, "Sites", 8, 9],
      },
      paint: { "text-color": p.label, "text-halo-color": p.labelHalo, "text-halo-width": 1.6 } },
    { id: "water-names", type: "symbol", ...src, "source-layer": "names", minzoom: 10,
      filter: ["==", ["get", "type"], "Water"],
      layout: { "text-field": ["coalesce", ["get", "name1"], ["get", "name2"]], "text-font": ["Open Sans Regular"], "text-size": 11, "text-max-width": 9, "text-padding": 2 },
      paint: { "text-color": p.waterLabel, "text-halo-color": p.labelHalo, "text-halo-width": 1.4 } },
  ];
}

/**
 * Whether the archive is installed. `{ available: false }` when the probe fails for any reason,
 * so the page keeps its plain panel rather than a map that spins.
 * @returns {Promise<BasemapStatus>}
 */
export async function fetchBasemap() {
  try {
    const response = await fetch("/api/tiles/status", { headers: { Accept: "application/json" }, signal: AbortSignal.timeout(5000) });
    if (!response.ok) {
      return { available: false };
    }
    const body = /** @type {unknown} */ (await response.json());
    return isStatus(body) ? body : { available: false };
  } catch {
    return { available: false };
  }
}

/**
 * @param {unknown} value
 * @returns {value is BasemapStatus}
 */
function isStatus(value) {
  return typeof value === "object" && value !== null && typeof (/** @type {{ available?: unknown }} */ (value)).available === "boolean";
}
