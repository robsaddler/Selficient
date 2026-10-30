# Threat model: use my location (M16)

## What it is

A "Use my location" button beside every postcode box (the landing page, the wizard, the solar estimator). The browser asks the person for their position; the script rounds it to four decimal places (about ten metres) and posts it to `/api/geo/nearest`; the server asks postcodes.io for the nearest postcode within two kilometres and answers with it; the script puts the postcode in the box and says so. Nothing is stored, cached or logged on the way.

## What we protect

- The person's location, which is the most personal datum the site has ever handled: it must not be kept, cached, logged or sent anywhere but postcodes.io.
- The upstream's goodwill and the server's time: a lookup is a free external call.
- The promise on the privacy page, which now names this flow.

## Abuse and failure cases

| # | Case | Control | Test |
|---|---|---|---|
| 1 | The coordinates land in a request log, ours or the proxy's | A POST with a JSON body, never a GET with a query string; request logging records the path only; the typed client has `RemoveAllLoggers()`; the endpoint logs nothing | `The_full_postcode_is_never_logged` pattern; `Nearest_is_a_post_with_no_store` |
| 2 | The coordinates are cached or kept | `Cache-Control: no-store` on the answer; the reverse geocoder is registered without the cache decorator the forward geocoder has; nothing is written to the store in the browser | `Nearest_is_a_post_with_no_store`, composition test |
| 3 | A point outside the United Kingdom is sent upstream (a wasted call, or a probe of what the server will relay) | A bounding box check before any upstream call; 400 with a sentence | `A_point_outside_the_united_kingdom_is_refused_before_the_upstream` |
| 4 | A malformed body, NaN, infinity or a string | Model binding to a record of two doubles; `IsPlausible` refuses non-finite values; 400 with a sentence | `A_point_outside_the_united_kingdom_is_refused_before_the_upstream` |
| 5 | Hammering the endpoint to use the site as a free reverse geocoder | The API rate-limit bucket (60 a minute per address) | `Rate_limit_returns_the_polite_429` (shared policy) |
| 6 | Upstream answers garbage, slowly or not at all | The standard resilience handler, a 64 KB body cap, strict DTOs; a failure is a 503 with no body and the button says to type the postcode | `Nearest_failures_are_faults` (Infrastructure) |
| 7 | The nearest postcode is far away (at sea, on a moor) | The upstream search radius is two kilometres; no result is a 404 with "We couldn't find a postcode where you are. Type it in instead." | `No_postcode_nearby_is_said_as_a_sentence` |
| 8 | A page in a frame asks for the location | `Permissions-Policy: geolocation=(self)`: the page itself may ask, no frame may; `frame-ancestors 'none'` already refuses framing | `SecurityHeadersTests` |
| 9 | The button works without the person's consent | It does nothing until pressed; the browser's own permission prompt stands between the press and the position; a refusal is a sentence, not a retry | `Use_my_location_fills_the_postcode_box` (E2E, with permission granted); the refusal branch in `locate.js` |
| 10 | Cross-site request forgery against the endpoint | The endpoint changes nothing and returns only what postcodes.io would give anyone; antiforgery is deliberately off for it, as for the other JSON endpoints | — |

## Outcomes (adversarial pass, 30 Oct 2026)

- Every case has a passing test or a control with no input to test. The privacy page names the flow in its table of what leaves the browser.
- Decided against: caching reverse lookups (a cache of where people stood is the one thing this must never build), and a GET (coordinates in a query string reach the IIS log on the vhost whatever we do here).
