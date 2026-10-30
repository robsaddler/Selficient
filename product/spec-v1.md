# Selficient v1 — product spec

*29 October 2026. Decisions: name Selficient; web app / PWA first at selficient.app; free with affiliate links, freemium kept open; all four features in v1.*

## One-liner

Selficient turns the government's generic "three days" advice into a plan for *your* house: enter your postcode, people and heating, get a risk-ranked, costed, tickable resilience plan with kit picks and a solar-and-battery estimate.

## Who it is for

UK households prompted by the Be Ready campaign (22 Oct 2026) who want to do it properly without becoming preppers. Secondary: people pricing rooftop solar who want the resilience angle included.

## v1 features

### F1. Household profile and risk-ranked plan
Inputs: postcode; adults, children, anyone with medical or mobility needs, pets; house type (flat, terrace, semi, detached, bungalow); storeys; heating (gas boiler, electric, heat pump, oil, LPG, wood); hob (gas, electric); car; garden/decking/trees near house; whether they run a business from home.
Derived automatically: lat/lon and elevation (postcodes.io + open-meteo elevation), region and DNO, nation (for flood-check link and grants), PVGIS yield for S/SW/SE/E/W.
Output: ranked risk register (storm power cut, water interruption, winter road access, comms loss, falling tree, garden fire, flooding where relevant, two-week disruption) and a four-tier plan with costs, adapted to the inputs (e.g. no boiler-plug action for electric heating; flat removes solar and water butt; medical needs raise the Priority Services Register action to tier 0).

### F2. Tickable checklist with reminders
Each plan action has a tick box, cost, and "why". Progress bar per tier. A rotation calendar: water at the clock changes, battery top-ups quarterly, alarm tests monthly, deck clearance autumn/spring, "warning received" checklist. Reminders via calendar export (.ics) in v1; push notifications later.

### F3. Kit recommendations with prices
Per action, 1-3 product picks per budget tier with price seen, date seen, retailer and link. Prices held in a JSON catalogue in the repo, reviewed monthly; affiliate tags added where programmes exist (Amazon Associates UK, Awin for Argos/Currys, EcoFlow/Bluetti partner programmes). Clear "we may earn a commission" disclosure.

### F4. Solar and battery estimator
Inputs: roof aspect (compass picker), pitch, available panel count or roof width/height, annual kWh (or estimate from household size), tariff situation (EV, heat pump). Output: generation (PVGIS via server route), self-consumption with and without battery, export income, overnight-charging saving, cost range, payback, backup-gateway note, grants by nation (Green Homes Wales 0% loan, etc.), VAT deadline. Links to MCS installer finder and group-buying schemes.

### F5. House on a map (added 29 Oct 2026)
**Stage A, always on:** self-hosted OS Open Zoomstack vector tiles via MapLibre (SiteWise's tiles controller, MBTiles basemap and map modules reused). After the postcode, show the street; the user taps "this is my house"; the footprint is outlined with elevation and contours, and the footprint's long axis seeds roof orientation. No third-party calls; works offline once cached.
**Stage B, opt-in inside the solar estimator:** Google Solar API Building Insights (UK high-quality coverage since May 2026; free to 10k calls/month, then ~£8/1k) returns per-roof-segment azimuth, pitch, area, panel count and yearly kWh, shown over aerial imagery on a Google-backed panel behind an explicit consent step. Compass picker remains the fallback. Later: OS NGD buildings for storeys and heights (Data Hub premium, £1,000/month free allowance).

### F6. Guided photos (added 29 Oct 2026)
Three photo moments, not a general upload: (1) "Photograph your kit cupboard" → inventory counted → items pre-ticked → Days Ready updates; (2) boiler data plate → model, wattage, plug-conversion feasibility; (3) consumer unit → spare ways for a backup board. No tree or deck assessment (liability). Images are re-encoded server-side (SkiaSharp, MIT licence), EXIF/GPS stripped, size and pixel caps enforced, processed in memory and discarded; stored only client-side in IndexedDB unless an account exists and the user opts in. Analysis output is schema-validated JSON treated as data.

### F7. AI assistance (added 29 Oct 2026)
Our key by default, metered: a free monthly allowance of analyses per household, unlimited in the paid tier (the first freemium hook). Model: Claude Opus 5 at low effort (≈1.5p per photo analysis, ≈4p per plan narrative; Haiku 4.5 is the cheaper option if volume justifies it, decided on usage data). Uses: kit-photo inventory, boiler/consumer-unit reading, "your plan explained" narrative, Q&A about the plan. **BYOK** offered as an advanced setting for privacy-maximalist users, with the browser calling Anthropic directly so the key never touches our server. Rules: the deterministic rules engine owns Days Ready and the plan; AI explains, counts and answers, never computes. Structured outputs, prompt caching of the catalogue, spend caps, rate limits, no image content logged.

## Out of scope for v1
Accounts and sync (profile and progress live in the browser's local storage; export/import as JSON), push notifications, native store listings, community features, live price scraping, payments.

## Monetisation
v1: affiliate links plus a metered free allowance of AI analyses. Paid tier candidates: unlimited AI analyses, multi-property, family sharing and sync, PDF plan export, push reminders, installer quote matching.

## Standing engineering requirements (set 29 Oct 2026)
- SOLID throughout; orthogonal layers (Domain → Application ports → Infrastructure adapters → Web), enforced by architecture tests.
- Dapper only; never EF Core.
- 100% line and branch coverage on Domain, Application, Infrastructure and Web, enforced in CI; exclusions limited to the Program entry point and documented.
- Defence in depth in every layer: see the build plan's security section.
- Adversarial self-attack on every feature before merge: threat and abuse list written first, encoded as failing tests, plus a security review pass.
- Blazingly fast under concurrent load: stateless web tier, caching of every upstream lookup, pooled resilient HttpClients, compression, immutable hashed assets, a benchmarked rules engine, and stress tests with published p95 targets against staging.

## Tech
*Revised 29 Oct 2026 after reviewing the house conventions in sitewise.app and VanillaUIBaseApp. The earlier Next.js/Vercel proposal is withdrawn: SiteWise already has a proven PWA pattern on the stack the team runs.*

- **.NET 10, Razor Pages + minimal-API endpoints**, a clean solution (not the VanillaUIBaseApp template, excluded 29 Oct 2026) using SmartReminder's build and test baseline and SiteWise's conventions; front end written fresh to 2026 platform standards with no framework or build step. Full detail in [the build plan](../plans/build-plan-v1.md).
- **Projects:** `Selficient.Domain`, `Selficient.Application`, `Selficient.Infrastructure`, `Selficient.Web`, plus Domain, Application, Infrastructure, Web, Architecture, Benchmark, Load and E2E test projects on xunit v3 + Shouldly + NSubstitute + `Mvc.Testing`.
- **PWA, the SiteWise way:** `manifest.webmanifest` (standalone), the install prompt script with the iOS Add-to-Home-Screen dialog, the server-generated service worker (`sw.source.js` template + `ServiceWorkerController` + `ShellVersionService` hashing every static asset) and the shell build-skew reload. Precache the shell; network-first for pages; never cache `/api/*`.
- **Offline data (v1):** profile, generated plan and tick state in `localStorage`/IndexedDB, exported and imported as JSON. No sync API, SignalR or outbox until accounts exist; when they do, copy SiteWise's outbox + `/api/sync/up` batch pattern.
- **Server endpoints:** `/api/geo/postcode` (postcodes.io), `/api/geo/elevation` (open-meteo), `/api/solar/pvgis` (PVGIS v5.2) behind `HttpClient` with output caching; health at `/health`.
- **Data:** none in v1. SQL Server + Dapper + embedded `.sql` migrations (SiteWise `MigrationRunner` pattern) when freemium accounts arrive.
- **Config and logging:** environment variables, no secrets in the repo; Serilog console + rolling file.
- **CI/CD:** `pr-build-and-test.yml` on PRs to `development`/`main`; `build-test-and-deploy.yml` on push to `main`: restore, build Release, test, publish, artifact, then a self-hosted Windows runner stops the IIS app pool, robocopies into the Plesk vhost for selficient.app, restarts and polls `/health`. selficient.co.uk and selficient.uk redirect at the Plesk level.
- **Analytics:** none at launch, or self-hosted Umami on an existing VPS if wanted; no third-party cookies.

## Content sources
The plan rules and kit catalogue are seeded from `research/2026-10-29-household-resilience-review.md` and `plans/household-resilience-plan.md`; the government checklist from beready.campaign.gov.uk.

## Launch checklist

Built and verified (30 Oct 2026):
- Privacy page (no personal data leaves the browser except postcode lookups), affiliate disclosure, "not professional advice" note: `/privacy`, `/terms`, fed from one `LegalCopy`
- Installable, works offline for the saved plan: proven in Chromium by the E2E journeys; Lighthouse 99/100/100/100 on six pages
- Share image and metadata: `/img/og.png`, canonical and Open Graph tags, JSON-LD
- Security headers, rate limits, Kestrel limits, dependency audit clean, load targets met (`docs/threat-models/launch.md`, `docs/benchmarks.md`)

Rob's, before the site is public (`docs/handover.md`):
- Domains registered; `.app` on the Plesk vhost, the other two redirecting (the plan said Vercel; the decision was the house way on the VPS)
- Vhost, app pool and the self-hosted runner so the deploy job runs; environment variables from `docs/configuration.md`
- Google Solar and Anthropic keys, or launch without them (the pages say so)
- The three legal placeholders confirmed; copy for the launch post
- The testing list in the handover walked through on the live site
