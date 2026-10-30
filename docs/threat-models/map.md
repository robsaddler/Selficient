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

*Filled in at the adversarial pass.*
