# ADR-001: Plan state lives in the browser, not in cookies and not on the server

**Status:** accepted, 29 October 2026.

## Context

The shop's front-end convention persists small preferences in cookies so the server can render first paint correctly. Selficient's household profile, generated plan, tick state and photos are kilobytes to megabytes of personal data about where someone lives and what they keep in the house.

## Decision

- Profile, plan and tick state are stored in IndexedDB (versioned schema with migrations) through a small promise wrapper seeded from SiteWise's `idb.js`; `localStorage` holds only one-byte preferences such as theme.
- No cookie carries plan data. The only cookies are the anti-forgery token and the theme preference.
- Nothing personal is sent to the server except the postcode, used for geocoding and never stored or logged beyond its outward district.
- Export and import are explicit user actions producing schema-validated JSON.

## Consequences

- First paint of a saved plan is client-rendered from IndexedDB; the server renders the empty state. A `<plan-sheet>` custom element hydrates from storage.
- Two tabs stay consistent through `BroadcastChannel` and storage writes are serialised with `navigator.locks`.
- When accounts arrive, SiteWise's outbox pattern syncs the same data; the local-first model does not change.
