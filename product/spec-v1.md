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

## Out of scope for v1
Accounts and sync (profile and progress live in the browser's local storage; export/import as JSON), push notifications, native store listings, community features, live price scraping, payments.

## Monetisation
v1: affiliate links. v2 candidate paid tier: multi-property, family sharing and sync, PDF plan export, push reminders, installer quote matching.

## Tech
*Revised 29 Oct 2026 after reviewing the house conventions in sitewise.app and VanillaUIBaseApp. The earlier Next.js/Vercel proposal is withdrawn: SiteWise already has a proven PWA pattern on the stack the team runs.*

- **.NET 10, Razor Pages + minimal-API endpoints**, started from the VanillaUIBaseApp template (vanilla ES modules, token-based BEM CSS, no front-end build step, no UI framework). Solution in `.slnx` format with `Directory.Build.props`.
- **Projects:** `Selficient.Domain` (plan rules, risk ranking, solar maths as pure C#), `Selficient.Web`, `tests/Selficient.Domain.Tests` and `tests/Selficient.Web.Tests` on xUnit + NSubstitute + `Mvc.Testing`.
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
- Domains registered and pointed at Vercel
- Privacy page (no personal data leaves the browser except postcode lookups), affiliate disclosure, "not professional advice" note
- Lighthouse PWA installable, works offline for the saved plan
- Share image and copy for the launch post
