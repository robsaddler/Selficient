# Threat model: hardening and launch (M10)

*30 October 2026. The whole application, read end to end before it goes live.*

## What it is

Not a feature but a sweep: every surface the earlier milestones added, re-read as an attacker, a slow phone and a storm-warning crowd would see it. The sweep covers the dependencies, the transport, the limits, the headers, the compression that M10 adds, the browser journeys, and what the logs hold. Each earlier threat model keeps its own cases; this one records what the sweep found across them and what stays open for Rob.

## Assets

- The household's privacy: nothing personal on the server, nothing in the logs beyond an outward postcode district.
- Availability on the evening it matters: the site must answer under a burst and degrade to the saved plan when it cannot.
- The integrity of the advice: a page must be ours, unaltered in transit, with no third-party script in it.
- The build itself: a dependency, a workflow step or a runner that could be turned against the site.

## Abuse and failure cases

| # | Case | Control | Evidence |
|---|---|---|---|
| 1 | A known-vulnerable or abandoned package ships | `dotnet list package --vulnerable --include-transitive` and `--deprecated` on every project; `npm audit` on the front-end tooling; exact versions pinned centrally | Both clean on 30 Oct 2026: no vulnerable, no deprecated NuGet package across thirteen projects; npm 0 vulnerabilities |
| 2 | Oversized or malformed requests tie up the server | Kestrel limits: 10 MB body, 60 headers, 32 KB of headers, 8 KB request line, 30 s to send headers, 2 min keep-alive; the thread pool floor of 64 (or eight per core) so a burst never queues behind pool growth | `Program.cs`; load run at 450 requests a second with p99 under 15 ms (`docs/benchmarks.md`) |
| 3 | A crowd, or one script, exhausts the upstreams | Four rate-limit buckets by client address: plan 30 a minute, API 60, AI 6 (posts only), tiles 600; the 429 page is a whole sentence; upstream calls are cached and resilient | `RateLimitTests`, `AiPartition` tests, the polite page pinned |
| 4 | Compression over HTTPS opens a side channel (BREACH) | Brotli and gzip are on for HTTPS. The condition for BREACH is a static secret reflected next to attacker-chosen input in the same response. There is none: the antiforgery field is encrypted afresh on every render, the CSP nonce is new on every request, no cookie or token is written into a page, and the only reflected input is the household's own form, which is not a secret | `Text_responses_are_compressed_for_a_browser_that_asks`; `A_browser_that_does_not_ask_gets_the_page_as_it_is`; this reasoning |
| 5 | A script from elsewhere runs in the page | CSP with a per-request nonce, `script-src 'self' 'nonce-…'`, no inline styles, no CDN anywhere (a test refuses any page that references one); the connect-src widens to Anthropic on the photos page only, and only for the household's own key | `SecurityHeadersTests`, `No_page_references_a_cdn`, `CspPolicyTests` |
| 6 | Transport downgrade | HSTS one year with subdomains and preload, HTTPS redirect, `Server` header off | `SecurityHeadersTests` on every route |
| 7 | Personal data reaches a log | `RemoveAllLoggers()` on every HttpClient, Serilog category overrides, and a test that runs the real adapters against a loopback stub and asserts the postcode never reaches the log | `The_postcode_never_reaches_the_log` (M3) |
| 8 | The worker serves a stale or wrong-build page, or reloads a device in a loop | Release stamp on code, one reload per two minutes, and from M10 no reload at all on a device's first worker: the page it took over is already this build | `shell.js`; `The_saved_plan_opens_offline_with_the_honest_banner` in Chromium |
| 9 | A page is unusable on a phone or to a screen reader | Playwright journeys at 390 px with axe on every public page and on a built plan; Lighthouse accessibility 100 on six pages | `Selficient.E2E.Tests`; `docs/benchmarks.md` |
| 10 | The deploy runs code it did not build | The deploy job downloads the artifact the build job made in the same run, over the runner the workflow names; secrets are machine-level environment variables on the runner, never in the repository or the workflow | `.github/workflows/build-test-and-deploy.yml`; `docs/configuration.md` |
| 11 | A fake upstream reaches production | `Upstreams:UseFakes` is validated false in Production at startup | `UpstreamOptionsTests` |
| 12 | The basemap fetch on the server pulls something other than Ordnance Survey's archive | The step reads the OS Downloads API index over HTTPS, takes only the MBTiles build, checks the size, and discards anything short; a failure leaves the map on its fallback and never fails the deploy | Workflow step, with warnings rather than errors |

## Outcomes

*Sweep, 30 October 2026.*

- Cases 1 to 12 hold. The dependency audit was clean on the first run; nothing needed bumping.
- Found and fixed by the browser journeys, which ran for the first time in M10: the worker reloaded every device on its first visit (the `controllerchange` after `clients.claim()` was treated as a replacement), which also broke every axe audit mid-navigation; a tick made before the plan script was listening was lost, so the plan now says when it is listening (`data-plan-ready`) and the journeys wait for it; the empty plan state had no way to import a file, so a household moving devices had to build a plan first; the ticking journey had to raise light as well as water and food, because Days Ready is the weakest of the three.
- Found by axe on the styleguide, where the contour band does not hide the background: lantern amber as text on the light ground was 1.9:1 against the 3:1 the display size needs, and the same colour is used for every counted number at body size. Lantern is now a fill colour and `--c-lantern-ink` is lantern as text, deep enough for 4.5:1 in daylight and the lantern itself in the dark. Lighthouse found the dark-theme pine at 4.4:1 as link text and white-on-pine at 3.1:1 as a button; the dark pine is lighter now and the primary button's label is dark on it.
- Found by Lighthouse: no compression, a six-deep stylesheet import chain, 400 KB of fonts on the wire and a header that grew when the install button appeared. All four fixed; the numbers are in `docs/benchmarks.md`.
- Residual, for Rob: HSTS preload is declared but the domain is not submitted to the preload list (do it once the certificate and redirects are proven); the Plesk vhost should keep IIS dynamic compression off for this site so the application's own encoding is the only one; the self-hosted runner is a machine that can deploy to production and should be treated as such (no other workloads, OS patched, the runner service account limited to the vhost folder); the first ubuntu build in GitHub Actions since the concurrency fix needs watching for the SkiaSharp Linux native and the Playwright system dependencies.
- Not done: a web application firewall or bot management in front of the site. The rate limits and the absence of any account or payment surface make it unnecessary at launch; revisit when accounts arrive.
