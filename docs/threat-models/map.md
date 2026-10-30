# Threat model: house on a map, stage A (M5)

*30 October 2026. Written before the code.*

## What it is

A self-hosted Ordnance Survey Open Zoomstack vector map (MapLibre, vendored) under the plan page, centred on the postcode's coordinates, with a marker the person can drag to their own house. The tiles come from an MBTiles file on the server through `/api/tiles/{z}/{x}/{y}`, fetched by the deploy workflow's guarded step; no third-party map host is ever contacted. When the basemap is absent the page degrades to a plain panel with the coordinates and a sentence.

## Assets

- Privacy: no coordinates leave the site; the tile requests reveal only which part of the country was viewed, to our own server.
- Server disk and CPU: the MBTiles file is 2 to 3 GB and tile reads must be cheap.
- The reader's time: a map that spins forever is worse than no map.

## Abuse and failure cases

| # | Case | Control | Test |
|---|---|---|---|
| 1 | Path traversal or absurd tile coordinates | `z`, `x`, `y` bound as integers, `z` 0 to 14, `x` and `y` within `2^z`; anything else is 404; no string reaches the file system | `Tile_coordinates_outside_the_pyramid_are_404` |
| 2 | SQL injection into the MBTiles query | Parameterised Dapper query against a read-only SQLite connection opened with `Mode=ReadOnly` | `Tiles_are_read_with_a_parameterised_query_on_a_read_only_connection` |
| 3 | Tile endpoint used to exhaust disk IO | API rate limit; `Cache-Control: public, max-age=1 year, immutable` so browsers and the CDN keep them; `HybridCache` for the hottest tiles | `Tiles_are_immutable_and_cached` |
| 4 | Basemap missing or corrupt on the server | `/api/tiles/status` reports presence; the page shows the degraded panel; a corrupt file is detected at startup by a `SELECT` on the metadata table and reported through status, never an exception to a reader | `Missing_basemap_degrades_to_the_panel`, `Status_reports_the_basemap` |
| 5 | The vendored MapLibre bundle or styles pulled from a CDN | Vendored under `wwwroot/lib/maplibre` with a pinned version; CSP unchanged (`self` only); `worker-src 'self'` already allowed | `No_page_references_a_cdn` |
| 6 | Marker position persisted or sent to the server | Position lives in the browser profile alongside the plan; nothing is posted | `The_map_never_posts` (JS review) |
| 7 | Gzip-compressed vector tiles served without the header | The handler sets `Content-Encoding: gzip` when the blob is gzipped (MBTiles convention) and `application/x-protobuf` | `Tiles_carry_the_right_encoding_and_type` |

## Outcomes

*Adversarial pass, 30 October 2026, against SiteWise's real 2.85 GB Zoomstack archive locally and the tiny test archives in CI.*

- Case 1 holds and goes further than written: `TileAddress` can only be constructed inside the pyramid, so the store never sees a bad address; the route constraints reject non-integers, a `.png` suffix and a traversal suffix before the handler runs. All 404. Pinned by `Tile_coordinates_outside_the_pyramid_are_404`.
- Case 2 holds: Dapper with named parameters on a connection string that carries `Mode=ReadOnly`; a `DELETE` through that connection string throws. The TMS row flip is pinned by reading the raw row back from the archive, since getting it wrong serves the mirror image of the country without an error.
- Case 3: tiles get their own rate-limit bucket (600 a minute per address) because a single pan is a dozen requests and the 60-a-minute API bucket would have throttled an ordinary household. Hits are immutable for a year; the hottest tiles and misses sit in `HybridCache` for ten minutes. Pinned.
- Case 4: a missing or corrupt archive is `{ available: false }` and the page keeps its plain panel with the postcode centre. Found while reviewing the deploy order: the workflow fetches the archive *after* the site starts, so a status cached forever would have hidden the map until the next restart. The store now re-checks the disk every minute while the archive is absent and remembers it for good once present. Pinned with a hand-moved clock.
- Case 5 holds: MapLibre 6.4.1 vendored with its licence and provenance, loaded through the import map, worker same-origin; the CSP did not change. `No_page_references_a_cdn` walks every page. Found and fixed: the vendored stylesheet is unlayered and so beat the themed overrides in the `components` layer; the overrides moved to an unlayered file of their own.
- Case 6 holds: `house-map.js` contains no `fetch`, and the style module's only request is the status probe. The pin is refused unless it lies inside the United Kingdom, so an imported file cannot park the map on the far side of the world.
- Case 7 holds: gzip detected from the magic bytes and declared; raw tiles pass through untouched. Pinned with one of each.
- Also found: Northern Ireland is outside OS Open Zoomstack. A BT postcode now keeps the panel with a sentence saying why, rather than a blank canvas with a pin.
- Residual: the status endpoint reveals that a basemap exists, which is public knowledge once the map draws. Accepted. No path, size or release leaves the server.
