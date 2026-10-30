# Threat model: profile wizard, geography lookups and the plan page (M2)

*30 October 2026. Written before the code.*

## What it is

The first screens with user input: a form that collects the household and home facts plus a postcode, two server-side lookups (postcodes.io for coordinates and nation, Open-Meteo for elevation), and the plan page that renders the engine's output with tick boxes. Profile and ticks live in the browser (ADR-001). The server stores nothing.

## Assets

- The postcode (the only personal datum the server ever sees).
- Upstream credit and availability (both APIs are free and keyless; abuse would get the site blocked).
- The integrity of the rendered plan (no injected content, no wrong household's plan).
- Server availability under load.

## Abuse and failure cases

| # | Case | Control | Test |
|---|---|---|---|
| 1 | Postcode field carries injection payloads into the upstream URL or the page | `Postcode` value object: normalised, validated against the UK format, maximum 8 characters; the URL is built from the validated value with `Uri.EscapeDataString`; Razor encodes everything rendered | `Postcode_refuses_anything_that_is_not_a_uk_postcode`, `The_geocoder_escapes_the_postcode_in_the_path` |
| 2 | SSRF: attacker steers a lookup to another host | Hosts are fixed in options and validated at startup; no part of the URL other than the postcode segment comes from input | `Options_refuse_a_non_https_or_wrong_host_base_url` |
| 3 | Upstream returns garbage, huge bodies or wrong types | Source-generated strict DTOs; `MaxResponseContentBufferSize` 64 KB; non-success status and malformed JSON become a `Fault`, never an exception to the user | `Malformed_upstream_json_is_a_fault`, `An_oversized_body_is_refused` |
| 4 | Upstream slow or down | 5 s timeout, standard resilience handler (retry, circuit breaker); elevation failure degrades to "unknown"; geocode failure is a whole-sentence error | `Elevation_failure_degrades_to_unknown`, `Geocode_timeout_is_a_fault` |
| 5 | Cache poisoning via case or spacing variants of the same postcode | Cache key is the normalised postcode; coordinates rounded to 4 dp for elevation keys | `Variants_of_a_postcode_share_one_cache_entry` |
| 6 | Postcode ends up in logs or error reports | Serilog request logging has no body logging; the handler logs nothing; the plan page shows the outcode only; the unit test asserts no log sink receives the full postcode | `The_full_postcode_is_never_logged` |
| 7 | Abuse of the geocode path to use the site as a free proxy | Fixed-window rate limiter per IP on the plan POST and `/api/*`; 429 with the polite page | `Rate_limit_returns_the_polite_429` |
| 8 | CSRF on the plan POST | Razor Pages anti-forgery on by default; verified by a test that a POST without the token is rejected | `A_post_without_the_antiforgery_token_is_rejected` |
| 9 | Tampered client-side profile re-posted from storage | The server re-validates every field through the same value objects; a tampered enum or count is a 400 with the sentence, never a crash | `Tampered_profile_values_are_rejected_with_sentences` |
| 10 | XSS through upstream strings (region, outcode) rendered on the page | Razor encodes; no `Html.Raw`; CSP forbids inline script anyway; test renders a region containing `<script>` from the fake and asserts it is encoded | `Upstream_strings_are_encoded_on_the_page` |
| 11 | Enumeration of which postcodes are "known" | Not sensitive: postcodes.io is public data. No control needed beyond rate limiting. | — |
| 12 | Plan page cached by a shared cache or the browser after logout-like scenarios | `Cache-Control: no-store` on the plan page | `The_plan_page_is_never_cached` |
| 13 | Fakes leaking into production | Fakes are registered only when `Upstreams:UseFakes` is true; startup check refuses that in Production | `Fakes_are_refused_in_production` |

## Outcomes (adversarial pass, 30 Oct 2026)

- All thirteen cases have passing tests. Web suite 61+, Infrastructure 47+, all gated assemblies at 100% lines and branches.
- **Security review (Claude, security-review skill) found one Medium issue, now fixed and pinned:** the HttpClient factory's default handlers logged every upstream request URL at Information, and the URL carries the postcode, so real lookups would have written postcodes to the console and the 14-day file log. Case 6's test could not catch it because the test host used the fakes. Fixes: `RemoveAllLoggers()` on both typed clients, Serilog category overrides for `System.Net.Http.HttpClient` and `Polly`, loopback HTTP allowed in the options so the test can run the real adapters against a WireMock stub, and the test now asserts the escaped form (`NP12%201GW`) and the coordinates are absent too.
- Checked hardest and found clean: upstream URL injection and SSRF (regex-validated postcode, escaped segment, fixed https hosts), the import path in `plan-store.js` (key regex excludes `__proto__`, values re-validated server-side), XSS via upstream strings (Razor-encoded, no raw HTML anywhere), CSRF (antiforgery on both hidden forms).
- Found by the tests themselves: the 429 re-execution reached the error page as a POST and rendered an empty heading; framework binding messages are fragments unless every value-type field (including bools, whose checkboxes carry an implicit required) has its own sentence.
