/**
 * Selficient service worker. A TEMPLATE: the server serves it at /sw.js with the version and
 * shell list substituted. The shell list is every static asset on disk and the version is a
 * fingerprint of them all, so a new page's assets are precached the moment the file exists and
 * any asset change rolls every device forward. Nothing here is maintained by hand.
 *
 * Lifted from SiteWise (Oct 2026) and cut down to what a household's plan needs:
 * - Precache the shell (css, js, lib, fonts, img, manifest).
 * - Pages: network-first, the cached page when the network fails, is slow, or answers 5xx.
 * - Code: network-first with conditional requests, stamped with the release it came from.
 * - Libraries, fonts, images: cache-first.
 * - Tiles: cache-first, because the server says they are immutable; the status probe is
 *   network-first with the last known answer as the fallback.
 * - Every other /api/ route, the health probe and the calendar download: never cached.
 * - The plan itself is a POST. The last successful plan page is kept under the plan address so
 *   a GET of /plan with no network opens the plan rather than the empty state.
 */

const VERSION = '__VERSION__';
const SHELL = __SHELL__;
const RUNTIME = 'sw-runtime';
const PLAN_PATH = '/plan';
const OFFLINE_PATH = '/offline';

const SLOW_CODE_MS = 1500;
const SLOW_PAGE_MS = 3000;

const RELEASE_HEADER = 'X-SW-Release';

function stamped(response) {
  const headers = new Headers(response.headers);
  headers.set(RELEASE_HEADER, VERSION);
  return new Response(response.body, { status: response.status, statusText: response.statusText, headers });
}

const fromThisRelease = (cached) => cached && cached.headers.get(RELEASE_HEADER) === VERSION;

function fromCache(request) {
  return caches.match(request).then((cached) => cached || caches.match(request, { ignoreSearch: true }));
}

/**
 * Network-first, but never slower than a copy from this release. Falls back to the cache on
 * failure, on a 5xx, and on a connection that is merely too slow.
 */
function freshOrCached(request, cacheName, slowMs) {
  return fromCache(request).then((cached) => {
    const network = fetch(request).then((response) => {
      if (response.ok) {
        if (!(response.headers.get('Content-Disposition') || '').includes('attachment')) {
          const copy = stamped(response.clone());
          caches.open(cacheName).then((cache) => cache.put(request, copy));
        }
        return response;
      }
      if (response.status >= 500 && cached) return cached.clone();
      return response;
    }).catch(() => cached ? cached.clone() : offlineFallback(request));

    if (!cached || !fromThisRelease(cached)) return network;

    return Promise.race([
      network,
      new Promise((resolve) => setTimeout(() => resolve(cached.clone()), slowMs)),
    ]);
  });
}

/** A navigation with nothing cached gets the offline page; anything else fails as it would have. */
function offlineFallback(request) {
  if (request.mode === 'navigate') {
    return caches.match(OFFLINE_PATH).then((page) => page || Response.error());
  }
  return Response.error();
}

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(VERSION)
      // Per-file and failure-tolerant: the all-or-nothing add is atomic, and one flaky request would
      // abandon the whole shell. Gaps heal on activate and on every online page load.
      .then((cache) => Promise.allSettled(
        [...SHELL, OFFLINE_PATH].map((url) => cache.add(new Request(url, { cache: 'reload' })).catch(() => {}))))
      .then(() => self.skipWaiting()));
});

function fillShellGaps() {
  return caches.open(VERSION).then((cache) =>
    cache.keys().then((keys) => {
      const have = new Set(keys.map((r) => new URL(r.url).pathname));
      return Promise.allSettled(
        [...SHELL, OFFLINE_PATH].filter((url) => !have.has(url))
          .map((url) => cache.add(new Request(url, { cache: 'reload' })).catch(() => {})));
    }));
}

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== VERSION && k !== RUNTIME).map((k) => caches.delete(k))))
      .then(() => fillShellGaps())
      .then(() => self.clients.claim()));
});

self.addEventListener('message', (event) => {
  if (event.data === 'sw:verify-shell') event.waitUntil(fillShellGaps());
  if (event.data === 'sw:version?') event.ports[0]?.postMessage(VERSION);
});

self.addEventListener('fetch', (event) => {
  const url = new URL(event.request.url);
  if (url.origin !== location.origin) return;

  // The plan is built by a POST. Keep the last successful one under the plan's own address so
  // an offline GET opens the plan. Nothing else that is posted is ever cached.
  if (event.request.method === 'POST') {
    if (url.pathname === PLAN_PATH) {
      event.respondWith(
        fetch(event.request).then((response) => {
          if (response.ok) {
            const copy = stamped(response.clone());
            caches.open(RUNTIME).then((cache) => cache.put(PLAN_PATH, copy));
          }
          return response;
        }).catch(() => caches.match(PLAN_PATH).then((plan) => plan || offlineFallback(event.request))));
    }
    return;
  }

  if (event.request.method !== 'GET') return;

  if (url.pathname === '/api/tiles/status') {
    event.respondWith(
      fetch(event.request).then((response) => {
        if (response.ok) {
          const copy = response.clone();
          caches.open(RUNTIME).then((cache) => cache.put(event.request, copy));
        }
        return response;
      }).catch(() => caches.match(event.request).then((cached) =>
        cached || new Response(JSON.stringify({ available: false, minZoom: 0, maxZoom: 0, attribution: '' }), { headers: { 'Content-Type': 'application/json' } }))));
    return;
  }

  if (url.pathname.startsWith('/api/tiles/')) {
    event.respondWith(
      caches.match(event.request).then((cached) => cached || fetch(event.request).then((response) => {
        if (response.ok && response.status !== 204) {
          const copy = response.clone();
          caches.open(RUNTIME).then((cache) => cache.put(event.request, copy));
        }
        return response;
      }).catch(() => new Response('', { status: 204 }))));
    return;
  }

  // Never cache other API answers, the health probe, the worker itself, or the calendar download.
  if (url.pathname.startsWith('/api/') || url.pathname === '/health' || url.pathname === '/sw.js' || url.pathname.endsWith('.ics')) return;

  if (url.pathname.startsWith('/js/') || url.pathname.startsWith('/css/')) {
    event.respondWith(freshOrCached(new Request(event.request, { cache: 'no-cache' }), VERSION, SLOW_CODE_MS));
    return;
  }

  const isShellAsset = url.pathname.startsWith('/lib/') || url.pathname.startsWith('/img/') ||
    url.pathname.startsWith('/fonts/') || url.pathname === '/manifest.webmanifest';
  if (isShellAsset) {
    event.respondWith(
      caches.match(event.request).then((cached) => {
        const network = fetch(event.request).then((response) => {
          if (response.ok) {
            const copy = response.clone();
            caches.open(VERSION).then((cache) => cache.put(event.request, copy));
          }
          return response;
        }).catch(() => cached || caches.match(event.request, { ignoreSearch: true }));
        return cached || network;
      }));
    return;
  }

  // A GET of the plan with no network opens the last plan that was built, not the empty state.
  if (url.pathname === PLAN_PATH && event.request.mode === 'navigate') {
    event.respondWith(
      fetch(event.request).catch(() => caches.match(PLAN_PATH).then((plan) => plan || offlineFallback(event.request))));
    return;
  }

  event.respondWith(freshOrCached(event.request, RUNTIME, SLOW_PAGE_MS));
});
