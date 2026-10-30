# MapLibre GL JS — vendored

**Version 6.4.1**, from npm `maplibre-gl@6.4.1`. BSD-3-Clause (see `LICENSE.txt`).

## Why vendored rather than a CDN

The field app must work **fully disconnected**, and a CDN is a third-party runtime
dependency in the request path — a key, a quota, an outage or a privacy question we do
not need. Self-hosting removes all four. It also means the service worker can cache the
library like any other asset, which is what makes the map available offline at all.

## What is here, and what is not

Only the four production files the browser actually loads:

| File | Raw | Gzipped |
|---|---|---|
| `maplibre-gl.mjs` | 567 KB | **138 KB** |
| `maplibre-gl-shared.mjs` | 482 KB | **130 KB** |
| `maplibre-gl-worker.mjs` | 19 KB | **6 KB** |
| `maplibre-gl.css` | 83 KB | **10 KB** |

**283 KB gzipped in total, fetched once and then cached.** Deliberately excluded: the
`-dev` builds, every `.map` sourcemap (~10 MB of them) and `maplibre-gl.d.ts` — none are
loaded by a browser and shipping them would quadruple the folder for nothing.

## Upgrading

Take the same four files plus `LICENSE.txt` from a fresh `npm install maplibre-gl@<version>`,
update the version above, and re-check the gzipped sizes. Do not add the sourcemaps back.
