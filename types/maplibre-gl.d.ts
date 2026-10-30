/**
 * The slice of MapLibre GL JS 6.4.1 that Selficient uses, typed by hand so `tsc --checkJs` can
 * check the callers without the library's own 400 KB declaration file. The runtime module is the
 * vendored `wwwroot/lib/maplibre/maplibre-gl.mjs`, reached through the import map.
 */
declare module "maplibre-gl" {
  export type LngLatLike = [number, number] | { lng: number; lat: number };

  export class LngLat {
    lng: number;
    lat: number;
  }

  export interface MapOptions {
    container: HTMLElement | string;
    style: Record<string, unknown>;
    center?: LngLatLike;
    zoom?: number;
    minZoom?: number;
    maxZoom?: number;
    attributionControl?: boolean | Record<string, unknown>;
    dragRotate?: boolean;
    pitchWithRotate?: boolean;
  }

  export interface MapMouseEvent {
    lngLat: LngLat;
  }

  export type ControlPosition = "top-left" | "top-right" | "bottom-left" | "bottom-right";

  export interface IControl {
    onAdd(map: Map): HTMLElement;
    onRemove(map: Map): void;
  }

  export class Map {
    constructor(options: MapOptions);
    addControl(control: IControl, position?: ControlPosition): this;
    getCenter(): LngLat;
    easeTo(options: { center?: LngLatLike; zoom?: number }): this;
    setStyle(style: Record<string, unknown>): this;
    on(type: "click", listener: (event: MapMouseEvent) => void): this;
    remove(): void;
  }

  export class Marker {
    constructor(options?: { draggable?: boolean; color?: string });
    setLngLat(lngLat: LngLatLike): this;
    getLngLat(): LngLat;
    addTo(map: Map): this;
    on(type: "dragend", listener: () => void): this;
    remove(): this;
  }

  export class AttributionControl implements IControl {
    constructor(options?: { compact?: boolean; customAttribution?: string | string[] });
    onAdd(map: Map): HTMLElement;
    onRemove(map: Map): void;
  }

  export class NavigationControl implements IControl {
    constructor(options?: { showCompass?: boolean; showZoom?: boolean });
    onAdd(map: Map): HTMLElement;
    onRemove(map: Map): void;
  }

  export class ScaleControl implements IControl {
    constructor(options?: { unit?: "metric" | "imperial" | "nautical"; maxWidth?: number });
    onAdd(map: Map): HTMLElement;
    onRemove(map: Map): void;
  }
}
