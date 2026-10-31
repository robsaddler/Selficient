# Selficient v1 — build plan

*Draft 1, 29 October 2026. Governs the build. Companion documents: [spec](../product/spec-v1.md), [design direction](../product/design-direction.md), [naming](../product/naming-and-domains.md), [resilience plan](household-resilience-plan.md) (the content seed).*

## 1. Governing requirements

| Requirement | How this plan meets it |
|---|---|
| Built the SiteWise way | .NET 10, Razor Pages + minimal APIs, vanilla ES modules and token CSS, no front-end build, Dapper not EF, Serilog, xUnit, GitHub Actions to IIS/Plesk on the existing VPS |
| SOLID | Section 4: each layer has one reason to change; behaviour added by new strategies and rules, not by editing existing ones; ports are small role interfaces; adapters are substitutable; the Web layer depends on abstractions |
| Orthogonal architecture | Section 2: strict inward dependency rule enforced by architecture tests; Domain has zero references; Infrastructure and Web never reference each other |
| Design patterns | Section 4 catalogue, each with the reason it earns its place |
| 100% test coverage | Section 7: coverlet thresholds at 100% line and branch per project, build fails below; exclusions listed and justified |
| Defence in depth | Section 5 matrix: every layer has its own control, none relies on another |
| Adversarial self-attack per feature | Section 8: the feature workflow starts with an abuse list and ends with a security review; nothing merges without both |
| Blazingly fast under concurrent load | Section 6: patterns and published p95 targets; stress tests in CI against staging |
| Fit for purpose, appealing, reassuring | Design direction document; Days Ready signature; copy rules; accessibility floor |

## 2. Architecture

### 2.1 Solution layout

```
Selficient.slnx
Directory.Build.props            net10.0, nullable, implicit usings, warnings as errors, analyzers
Directory.Packages.props         central package management, every version pinned
global.json                      SDK pin
src/
  Selficient.Domain/             pure C#: value objects, entities, rules, calculators. No references.
  Selficient.Application/        use cases, ports (interfaces), DTOs, validation. References Domain.
  Selficient.Infrastructure/     adapters: HTTP clients (postcodes.io, open-meteo, PVGIS, Google Solar,
                                 Anthropic), kit catalogue, image processing, caching, later Dapper. References Application.
  Selficient.Web/                Razor Pages, minimal endpoints, ViewComponents, service worker,
                                 security middleware. References Application; Infrastructure only in Program.cs composition.
tests/
  Selficient.Domain.Tests/
  Selficient.Application.Tests/
  Selficient.Infrastructure.Tests/     fake HttpMessageHandlers + WireMock.Net for contract tests
  Selficient.Web.Tests/                WebApplicationFactory: pages, endpoints, headers, CSP, service worker
  Selficient.Architecture.Tests/       NetArchTest dependency and naming rules
  Selficient.Benchmarks/               BenchmarkDotNet for the rules engine
  Selficient.LoadTests/                NBomber scenarios, run against staging
  Selficient.E2E/                      Playwright smoke journeys (not in coverage)
docs/                            architecture decision records, threat models per feature
content/                         kit catalogue JSON, plan rule data, copy
```

### 2.2 Dependency rule

Web → Application → Domain. Infrastructure → Application. Nothing references Web. Nothing references Infrastructure except the composition root. Enforced by `Selficient.Architecture.Tests` (NetArchTest), which fails the build on any violation, on any public type in Domain that references `System.Net`, `System.IO` or `Microsoft.Extensions`, and on any Razor Page that references Infrastructure.

### 2.3 Request flow

```
Browser ── POST /plan ──▶ Razor PageModel (validates, maps to command)
                          └─▶ GeneratePlanHandler (Application)
                                ├─▶ IGeocoder, IElevationProvider (ports)  ◀── Infrastructure adapters, cached
                                └─▶ PlanBuilder (Domain): RiskAssessors → PlanRules → DaysReadyCalculator
                          ◀── PlanDto
Razor renders the plan; ES modules add ticking, Days Ready animation, export.
Tick state and profile live in the browser (localStorage + IndexedDB). No server-side personal data in v1.
```

## 3. Domain model

Value objects (immutable records with validation in the constructor): `Postcode`, `Litres`, `KilowattHours`, `Money` (GBP, pence integer), `Days`, `Azimuth`, `Pitch`, `Elevation`, `HouseholdSize`.

Entities and aggregates: `Household` (people, needs, pets), `Property` (type, storeys, heating, hob, garden features, roof segments), `RiskAssessment` (ranked `Risk` list with likelihood, impact, rationale), `Plan` (ordered `Tier`s of `PlanAction`s, each with cost range, "why", the `Risk`s it mitigates, and the `Capability` it adds), `Inventory` (what the household already has, in capability units), `SolarEstimate`.

Calculators (pure, deterministic, benchmarked): `DaysReadyCalculator` (min over water, no-cook food, light-hours, boiler-hours, cash, scaled by household), `SolarYieldCalculator` (PVGIS yield × kWp × self-consumption model), `PaybackCalculator`, `TariffSelector`.

Rules: `IRiskAssessor` strategies (StormPowerCut, WaterInterruption, WinterAccess, CommsLoss, FallingTree, GardenFire, Flooding, RegionalDisruption) and `IPlanRule` strategies that each contribute actions when their `Specification` is satisfied. Adding a risk or an action is a new class plus its tests; nothing existing changes (open/closed).

## 4. Patterns catalogue

| Pattern | Where | Why it earns its place |
|---|---|---|
| Ports and adapters | Application interfaces, Infrastructure implementations | Orthogonality; every upstream (PVGIS, Google, Anthropic) is swappable and fakeable |
| Strategy | `IRiskAssessor`, `IPlanRule`, `ITariffStrategy` | Open/closed growth of risks, actions and tariffs |
| Specification | `HouseholdSpecification` (e.g. `HasVulnerablePerson`, `HasGasBoiler`, `IsFlat`) | Composable eligibility logic, tested once, reused by rules and UI |
| Builder | `PlanBuilder` | Assembles tiers from rule contributions deterministically and sorts by time horizon |
| Pipeline / chain | `RiskAssessmentPipeline` runs assessors, then `PlanRulePipeline` | Order-independent contributions, easy to trace which rule produced which action |
| Result type | `Result<T, Error>` in Domain | No exceptions for expected failures (bad postcode, no coverage); forces callers to handle both paths, which is also what makes 100% branch coverage honest |
| Decorator | `CachedGeocoder : IGeocoder`, `ResilientPvgisClient` | Caching and resilience are added without touching the adapter |
| Options + validation | `IOptions<T>` with `ValidateDataAnnotations().ValidateOnStart()` | Misconfiguration fails at boot, not at first request |
| Typed HttpClient + Polly | Infrastructure | Pooling, timeouts, retries, circuit breakers per upstream |
| Null object | `NoSolarCoverageProvider` | Solar estimator degrades to the compass picker without branches in the page |
| Outbox (later) | Sync when accounts arrive | Proven in SiteWise; deliberately not in v1 |
| Template method | `ThreatModelledFeatureTests` base class | Every feature's test class must implement the abuse-case methods |

Anti-patterns we refuse: service locator, static mutable state, exceptions as control flow, god handlers, logic in Razor, inline script or style, magic numbers (tokens only).

## 5. Defence in depth

| Layer | Controls |
|---|---|
| Edge (Plesk/IIS, Cloudflare optional) | TLS termination, HSTS preload, request size limits, WebDAV module removed (SiteWise lesson) |
| HTTP headers | CSP with per-request nonce (no inline anything, which the house style already forbids), `frame-ancestors 'none'`, `X-Content-Type-Options`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` (camera only on the photo page, geolocation off), COOP/CORP; verified by a Web test on every page |
| Rate limiting | Fixed-window per IP on `/api/*` and on plan generation; stricter on photo and AI endpoints; 429 with no detail |
| Input | Value-object validation in Domain; DataAnnotations on PageModels; anti-forgery on every POST; model-binding limits; JSON import validated against a schema with size caps; postcodes normalised and validated before any outbound call |
| Outbound | Allowlisted hosts only; URLs built from typed parameters, never user strings (SSRF impossible by construction); timeouts, circuit breakers, response size caps; responses deserialised into strict DTOs |
| Images | Re-encoded with SkiaSharp (MIT); EXIF stripped; pixel and byte caps; content-type sniffed not trusted; processed in memory; never written to disk |
| AI | Output schema-validated; no tool access; system prompt treats image and user text as data; spend caps per IP and global; keys in env vars; BYOK key never transits our server |
| Service worker | Never caches `/api/*`; versioned by asset hash; scope-restricted; offline page lists only cached routes |
| Data | No server-side personal data in v1; logs contain outward postcode district only; Serilog destructuring policy redacts |
| Secrets and supply chain | Env vars only; `dotnet list package --vulnerable` and Dependabot in CI; central package management; vendored JS pinned and hashed; no CDN |
| Affiliate links | `rel="sponsored noopener noreferrer"`, outbound allowlist, disclosure on every page that shows them |
| Operations | `/health` returns status only; no dev dashboards in v1; startup checks fail fast on missing config |

## 6. Performance

Targets on a single VPS vCPU pair. There is no staging environment (Rob, 29 Oct 2026: test locally). NBomber runs against a Release build of the app that the load-test project starts itself on Kestrel, locally and on the CI runner, and the numbers are recorded per commit so regressions show:

| Scenario | Target |
|---|---|
| Plan generation POST (cached geocode) | p95 < 150 ms at 200 req/s |
| Plan generation POST (cold geocode) | p95 < 600 ms at 50 req/s |
| Static shell (first load) | < 120 KB transferred, LCP < 1.5 s on 4G |
| Solar estimate (PVGIS cached) | p95 < 200 ms |
| Rules engine (BenchmarkDotNet) | < 1 ms, < 10 KB allocated per plan |

Patterns: stateless web tier; `HybridCache` for geocode, elevation, PVGIS and Solar lookups keyed on normalised inputs with 30-day TTL; output caching for marketing pages; response compression (Brotli); static assets fingerprinted and `immutable`; typed HttpClients with pooled handlers; `ValueTask` on hot paths; `IAsyncEnumerable` only where it helps; rules engine allocation-measured; no per-request reflection; Razor Pages compiled; thread-pool minimums set; Kestrel limits tuned; no synchronous IO.

## 7. Test strategy

1. **Unit (Domain, Application):** every rule, calculator and handler; property-based tests (FsCheck) for calculators (monotonicity: adding water never lowers Days Ready; invariants: Days Ready ≥ 0).
2. **Contract (Infrastructure):** each adapter against recorded upstream responses and WireMock.Net fault injection (timeouts, 500s, malformed JSON, oversized bodies).
3. **Integration (Web):** WebApplicationFactory with fake ports; every page renders; every endpoint; anti-forgery; headers and CSP on every route; rate limit behaviour; service worker completeness test (SiteWise's pattern).
4. **Architecture:** NetArchTest rules from section 2.2.
5. **Adversarial:** per feature, the abuse list from section 8 encoded as tests in a `*.Adversarial.Tests` class.
6. **Accessibility:** axe-core via Playwright on every page, zero violations.
7. **E2E:** Playwright smoke journeys: postcode to plan, tick to Days Ready change, export and import, install prompt, offline reload.
8. **Performance:** BenchmarkDotNet baselines committed; NBomber load runs in the PR workflow against a self-hosted Release build on Kestrel, failing on target breach; the same script runs locally with one command.
9. **Coverage:** coverlet in every test project with `Threshold=100`, `ThresholdType=line,branch`, `ThresholdStat=total`; ReportGenerator HTML artifact on every CI run. Exclusions, documented in `docs/coverage-exclusions.md`: `Program.cs` top-level statements, generated Razor code, `[ExcludeFromCodeCoverage]` only on composition-root extension methods that wire DI, each with a justification comment.

## 8. Feature workflow (definition of done)

For every feature, in order:

1. **Threat and abuse list** in `docs/threat-models/<feature>.md`: STRIDE pass plus "how would I break this as a user, a bot, a competitor, a malicious upstream?" Minimum eight cases.
2. **Failing tests first:** abuse cases and the happy path as tests before implementation.
3. **Implement** behind ports; no feature code in PageModels beyond mapping.
4. **Coverage gate** at 100% for touched projects.
5. **Adversarial pass:** run `/security-review` on the diff, plus a manual attempt at each listed abuse case against the running app; record outcomes in the threat model file.
6. **Design pass:** screenshot on phone width and desktop, check against the design direction, copy rules, axe clean, reduced-motion respected.
7. **Benchmark** if the rules engine or a hot path changed.
8. **PR** to `development` with the threat model linked; squash-merge; `main` deploys.

## 9. CI/CD

No staging environment. Testing is local (`./build/run-tests.ps1`, one command, same gate as CI) and `main` deploys straight to production, as every other app in the shop does.

- `pr-build-and-test.yml` on PRs to `development`/`main`: SiteWise's shape plus CoachApp's UTF-8 mojibake guard, SmartReminder's per-assembly 100% coverage gate (`build/run-tests.ps1` + `build/coverage-report.mjs`, extended to branch coverage), architecture tests, `dotnet list package --vulnerable`, `tsc --checkJs` and ESLint, Playwright smoke and axe, NBomber against a self-hosted Release build, coverage artifact.
- `build-test-and-deploy.yml` on push to `main`: SiteWise's file with the shop's standard points of difference substituted and nothing else changed: solution `Selficient.slnx`; web project `src/Selficient.Web/Selficient.Web.csproj`; no secondary publish (no DevTools); no SQL service container (no database in v1); runner label `[self-hosted-selficient, windows]`; `APP_POOL_NAME: "selficient.app(domain)(4.0)(pool)"`; `DEPLOY_PATH: "C:\inetpub\vhosts\selficient.app\httpdocs"`; `HEALTH_URL: "https://selficient.app/health"`; robocopy `/MIR /XD logs App_Data tiles`; the guarded Zoomstack basemap fetch kept as the last, non-gating step for M5. Concurrency group `deploy-production`, `cancel-in-progress: false`.
- The deploy job cannot run until the vhost, app pool and runner label exist on the VPS; until then the workflow still builds, tests and uploads the publish artifact on every push to `main`.
- Branch strategy as SiteWise: `development` integration, `main` release.
- No secrets in workflows; env vars on the runner.

## 10. Reuse inventory

### Not reused: VanillaUIBaseApp / AlacrityBaseApp
Excluded on Rob's instruction (29 Oct 2026): built for a client, never taken further, not well tested, and it ships no tests at all. Two things still apply from reviewing it: rotate and strip the Syncfusion key committed in both repos' `.mcp.json`, and keep the one idea worth keeping, a living `/styleguide` page that renders every component so design drift is visible.

### Front end written fresh to 2026 platform standards
No framework, no bundler, no transpiler, no preprocessor. The browser platform as of 2026 provides what those used to, and Selficient uses it directly so there is nothing to upgrade, nothing to break, and the shipped bytes are exactly what we wrote.

**CSS**
- `@layer reset, tokens, base, components, utilities, pages` declared once, so specificity is governed by layer order rather than selector weight; no `!important` anywhere.
- Native nesting; `oklch()` colours derived from the design-direction palette with `color-mix()` for states, so every tint is one computed step from a token; `light-dark()` with `color-scheme` for theming, plus `[data-theme]` override for the explicit toggle.
- Container queries for every component (`container-type: inline-size`), media queries only for the page grid and `prefers-*`; `:has()` for state-driven layout; logical properties throughout; `clamp()` fluid type and space; `@property` for the animatable Days Ready number; `view-transition` for page-to-page continuity in the wizard; scroll-driven animation only for the contour band, gated by `prefers-reduced-motion`.
- Native `<dialog>` for modals, `popover` attribute for menus and tooltips, `<details name="…">` for exclusive accordions, `<progress>`/`<meter>` for progress, `field-sizing: content`, `text-wrap: balance` on headings and `pretty` on body, `scroll-margin` for anchors, `:focus-visible` ring from one token, `inert` for offscreen panels.
- Fonts self-hosted, variable, `font-display: swap`, subset to Latin.

**JavaScript**
- ES modules with an import map, `type="module"` deferred by default, one entry per page plus a shared `app.js`; no globals.
- Behaviour attached by data attributes and custom elements: small Web Components (`<days-ready>`, `<plan-tier>`, `<compass-picker>`, `<kit-photo>`) using `ElementInternals` for form participation and `adoptedStyleSheets` for scoped styles, with light DOM where SEO or styling needs it.
- Event delegation on `document`, `AbortController` on every listener and fetch, `signal: AbortSignal.timeout()` on every request, `navigator.locks` to serialise storage writes, `structuredClone` for state snapshots, `Intl` for every number, date and currency, `IntersectionObserver` for lazy work, `requestIdleCallback` for non-urgent work, `BroadcastChannel` so two tabs stay in step.
- Storage: IndexedDB through a tiny promise wrapper (SiteWise's `idb.js` as the seed) with a versioned schema and migrations; `localStorage` only for one-byte preferences; everything typed by JSDoc and checked with `tsc --checkJs` in CI so we get the type safety without a build.
- Progressive enhancement is a rule, not a slogan: every form posts and renders without JavaScript; JS upgrades the experience (instant Days Ready, no reload, offline). Every page is tested with JS disabled.
- Reliability floor: the service worker never serves a stale API response; `online`/`offline` events plus a health probe drive the status pill; errors are caught at the module boundary and surfaced in the interface's voice, never swallowed.

**Testing the front end:** Web Test Runner or Playwright component tests for each custom element; Playwright journeys; axe on every page; `tsc --checkJs` and ESLint (flat config, no plugins beyond `eslint:recommended`) in CI. A living `/styleguide` page renders every component and token in both themes and is screenshot-compared on PRs.

**ADR-001:** plan state lives in IndexedDB and localStorage, not cookies, because it is kilobytes of personal data that must never be sent to the server.

### From sitewise.app
- `wwwroot/manifest.webmanifest`, `js/app/install.js` and the iOS install dialog markup.
- `wwwroot/sw.source.js`, `Controllers/ServiceWorkerController.cs`, `Services/ShellVersionService.cs`, `js/app/shell.js` build-skew reload, `tests/.../ServiceWorkerTests.cs`.
- `js/app/idb.js` (hand-rolled IndexedDB wrapper) for photos and plan state.
- MapLibre vendored lib, `TilesApiController`, the MBTiles basemap fetch step from the deploy workflow, and the map modules under `js/map`.
- Serilog configuration, `EnvironmentStartupChecks`, `/health` controller, `web.config` WebDAV removal, data-protection keys outside the robocopy mirror, rate-limiter setup, `OrgStampFilter`-style defence pattern for anti-forgery.
- Both workflow files as the base.
- Later: `js/app/store.js`/`sync.js` outbox and `SyncApiController` when accounts arrive; `MigrationRunner` and Dapper patterns when a database arrives.

### From other repos (cross-repo search, 29 Oct 2026)

**Build and test baseline: SmartReminder.** The strictest and newest in the shop; start the solution from these files.
- `SmartReminder/Directory.Build.props`: warnings as errors, `EnforceCodeStyleInBuild`, `AnalysisLevel=latest-Recommended`, deterministic, invariant globalisation, artifacts output, trim/AOT analyzers, unsafe blocks off, automatic `InternalsVisibleTo` for `*.Tests`.
- `SmartReminder/Directory.Packages.props` (central package management with transitive pinning), `global.json` (SDK 10.0.400, Microsoft.Testing.Platform runner), `.editorconfig` (enforced in build).
- `SmartReminder/tests/Directory.Build.props`: xunit v3 + Shouldly + NSubstitute + `Microsoft.Testing.Extensions.CodeCoverage` auto-referenced for every test project.
- `SmartReminder/.github/workflows/ci.yml` and `build/run-tests.ps1`: asserts **100% line coverage per production assembly**, not an average, and uploads cobertura artefacts. Selficient extends this to branch coverage.
- `SmartReminder/tests/SmartReminder.Architecture.Tests` and the `SmartReminder.TestKit` project (`TestClock`, builders, recording presenters): the shared test-double library pattern. Selficient gets `Selficient.TestKit`.
- `SmartReminder.Core/Guards/Ensure.cs` and `Time/IClock.cs`: the guard and clock abstractions to adopt.
- `SmartReminder.Rules` (conditions, `ConditionEvaluationEngine`, `TriggerGate`, `QuietHours`, `ReminderJournal`): a platform-neutral reminder decision engine. Reused for the rotation calendar and, when push arrives, reminder scheduling with quiet hours and de-duplication.

**Postcode and geography: MapTools and SiteWise.**
- `MapTools.Console/Services/PostcodeService.cs`, `Models/PostcodeApiResponse.cs`, `Utilities/PostcodeUtility.cs`: a postcodes.io client, a DTO already exposing country, region, district, ward, LSOA and outcode (exactly what nation and region routing needs), and a `GeneratedRegex` normaliser. Reworked into a typed client returning `Result`, cached by decorator, with a format validator added.
- `SiteWise.Domain/Services/GridReference.cs` + `wwwroot/js/calc/grid.js` + `GridVectorTests.cs`: OSGB36 to lat/lon in C# and JS with a shared fixture. Needed for anything that touches OS or Environment Agency data.
- `StockConditionSurveys` postcode geocode cache table: the precedent for a persistent lookup cache when a database arrives.

**Visualisation: Recovri.**
- `Recoveri.Web/wwwroot/js/app/charts.js`: a dependency-free SVG trend chart driven by a `data-series` attribute and CSS custom properties, theme-aware. Reused for PV yield by month and Days Ready over time; retokened to the design direction.
- Recovri's glyph modules: the "one renderer, declaratively wired" pattern for animated SVG, applied to the contour band.
- **GezzyMerch's Chart.js wiring (correction, 29 Oct 2026):** `GezzyMerch.Web/wwwroot/lib/chartjs/chart.umd.js` (v4.4.6, MIT, vendored), `wwwroot/js/admin/dashboard-charts.js` (no inline script; config and data as `<script type="application/json">` islands; palette read from `:root` custom properties at runtime), `Areas/Admin/Pages/Shared/_ChartCard.cshtml` (canvas plus a `<details>` accessible description) and `_Sparkline.cshtml` (aria-hidden canvas with a screen-reader summary). This is CSP-clean and accessible by construction. Decision for M6: Recovri's SVG chart for the simple trend lines; the GezzyMerch pattern with Chart.js if the PV-yield-by-month chart needs tooltips and stacked series. Not CoachApp's inline `new Chart()` blocks.

**Security and quality tests: Recovri and GezzyMerch.**
- `Recoveri.Web.Tests/TestWebApplicationFactory.cs` and `AuthAttackFixture.cs`: the host and attack fixture shape.
- `Recoveri.Domain.Tests/DomainPurityTests.cs`, `CoverageTests.cs`, `Recoveri.Application.Tests/LayeringTests.cs`, `Recoveri.Web.Tests/EndpointSurfaceGuardTests.cs`, `Security/PolicyMatrixTests.cs`, `ClientStringsTests.cs`: reflection-based architecture and surface guards with no extra dependency. Adopted alongside NetArchTest, or instead of it if they cover the rules.
- `Recoveri.Web.Tests/LegalContactAddressTests.cs`: pins the legal address on live pages; copied for the privacy and disclosure pages.
- `GezzyMerch.Web/TagHelpers/NonceTagHelper.cs` and Recovri's `Middleware/NonceTagHelper.cs`: CSP nonce tag helper.
- `Recovri/.github/workflows/product-pr-build-and-test.yml`: the UTF-8 mojibake guard step, copied verbatim.

**Email, outbox and SEO: GezzyMerch.** Not needed in v1 but the slices to lift when accounts and reminders arrive.
- `GezzyMerch.Application/Notifications` (outbox entries, dispatcher, dead-letter requeue, idempotency key, metrics) and `IEmailSender` with Postmark, SMTP and a dev-aware sender, plus the template renderer and classification.
- `GezzyMerch.Infrastructure/Seo/SitemapGenerator.cs`, `RobotsTxtProvider.cs`, the JSON-LD partial and their tests: reused in M9 with `FAQPage`, `HowTo` and `Organization` structured data.
- `GezzyMerch.Domain/Common/Result.cs`: the result type to adopt.
- `PerceptenceWebsite` `robots.txt`, `sitemap.xml`, `llms.txt`: the static SEO set including an `llms.txt`.
- `Almanac` privacy and terms pages: the legal page skeletons.

**Shop-wide rule adopted:** SiteWise's `CLAUDE.md` directive that every user-visible string is a whole explicit sentence, pinned by tests. It matches the design direction's copy rules and becomes Selficient's `CLAUDE.md`.

**Licensing finding:** `PDFTools` depends on SixLabors.ImageSharp, which is under the Six Labors Split Licence and needs a paid licence for commercial use above a small-business threshold. Selficient's photo pipeline therefore uses **SkiaSharp (MIT)** for re-encoding and EXIF stripping instead. For a future plan PDF, QuestPDF (MIT below the revenue threshold) rather than inheriting ImageSharp.

**Confirmed greenfield:** the PVGIS client, Google Solar client, all solar and battery maths, `.ics` generation, affiliate link handling and disclosure, a consent step for the Google panel, and the Anthropic adapter. No code in the shop touches any of these.

## 11. Milestones

| # | Milestone | Deliverables | Acceptance |
|---|---|---|---|
| M0 | Foundation | Solution, CPM, analyzers, test projects, architecture tests, coverage gate, both workflows, health, security headers, CSS layers and tokens from the design direction, fonts, import map and `app.js`, first custom element (`<days-ready>`), `/styleguide` page, `tsc --checkJs` and ESLint in CI, ADR-001 | CI green at 100% on an empty-but-real app; headers test passes; styleguide renders in both themes; `./build/run-tests.ps1` passes locally; publish artifact produced on `main` |
| M1 | Rules engine | Domain value objects, risks, rules, Days Ready, plan builder, property tests, benchmarks | Your house's plan reproduces the committed resilience plan tier by tier |
| M2 | Profile and plan | Wizard pages (progressive enhancement, works without JS), geocode and elevation adapters, plan page, contour hero, Days Ready animation | Postcode to plan on a phone in under a minute; offline reload of a generated plan |
| M3 | Checklist | Tick state, progress per tier, rotation calendar, `.ics` export, JSON export/import with schema | Ticking water moves Days Ready; import rejects hostile JSON |
| M4 | Kit catalogue | Catalogue JSON with provenance, recommendations per action and tier, affiliate links and disclosure | Every action shows picks with price and date seen |
| M5 | Map | Zoomstack tiles self-hosted, house confirmation, footprint outline, contours | Works offline after first view |
| M6 | Solar estimator | PVGIS adapter, Google Solar adapter behind consent, compass fallback, tariff strategies, payback | Your roof returns SW segments and a figure within 10% of the committed review |
| M7 | PWA | Manifest, install prompt, service worker, skew reload, offline page | Lighthouse PWA installable; service worker completeness test |
| M8 | Photos and AI | SkiaSharp pipeline, kit inventory analysis, boiler and consumer-unit reading, plan narrative, metering, BYOK setting | Cupboard photo pre-ticks items; abuse cases (polyglot image, prompt-injection text in image, oversized upload) all blocked by tests |
| M9 | Marketing and legal | Landing page, privacy, terms, affiliate disclosure, "not professional advice", SEO, OG image | Copy passes the design-direction rules |
| M10 | Hardening and launch | Full adversarial sweep, axe, Lighthouse ≥ 95 all categories, NBomber targets met, production deploy, domains live | Launch checklist in the spec complete |
| M11 | Design pass | The desktop the design direction drew: roofline header and mark, two-column hero, plan as panel beside sheet, screenshot journey at both widths and themes | Every page looks designed at 1440 px and 390 px in both themes; `artifacts/screens` on every E2E run |
| M12 | Engine v2 | Utilities, scenarios, needs on rungs, providers with sources, dependencies, the day-by-day simulation; Days Ready as the first Survive failure | Rob's house stands a day in a storm with nothing done; FsCheck invariants hold; the board under 1 ms |
| M13 | The board | Scenario cards, sources and seasons on the plan, the fragment redraw, what you rely on, the newer wizard questions | A tick moves the figure and the cells without a reload; a dependency gets a row and survives a reload |
| M14 | The look | Night hero and glow, the strip, the SVG marks and still lifes, one orchestrated moment | The landing figure and strip are the engine's; every need and source has a mark; Lighthouse holds at 99 |
| M15 | Sweep and handover | Adversarial and load pass over M13 and M14, the board's own rate bucket, one sheet a request, the harness fixed | All five load targets met; audits clean; handover current |
| M18 | The catalogue as a system | SQLite store through Dapper, the nightly verifier with rule and model judges, the hosted worker, `/out` click counting, the operator's reviewer; Phase A of `docs/design/catalogue-system.md` | Every shown link was opened by the server in the last day and judged to land; a failing one hides itself before a household sees it; what earns is counted without a cookie |

Sequence is strict for M0–M2; M3–M9 can interleave; M10 last of the launch build. M11 to M15 followed Rob's review of the launched build on 30 October 2026.

### Milestone log

- **M1 done, 30 Oct 2026.** Rules engine: value objects, nine risk assessors, sixteen plan rules over four tiers, deterministic builder, Days Ready calculator. Domain 105 tests, Application 9, all gated assemblies at 100% lines and branches. Golden test reproduces Rob's committed plan tier by tier. Benchmark 7.3 µs and 22.9 KB per plan (allocation target revised to 50 KB, `docs/benchmarks.md`). Security review: no findings; closed-set enum check added on its robustness note.
- **M2 done, 30 Oct 2026.** Wizard, plan page, postcodes.io and Open-Meteo adapters with caching and resilience, deterministic fakes behind `Upstreams:UseFakes` (refused in Production), rate limiting, browser-side storage with export and import, Days Ready moving on ticks. Lessons pinned by tests: the 429 re-execution reaches the error page as a POST; Razor attributes hidden tag-helper branches to the enclosing `@if` line, so page states became partials; a `const decimal` hides an unrunnable static constructor; WireMock timeouts must be generous under coverage; the model-binding and implicit-required messages are fragments unless every value-type field carries its own sentence. `ReadFrom.Services` in Serilog did not see the test host's services, so a small explicit `LogTap` replaced it. Phone-width check still owed to the Playwright suite.
- **M3 done, 30 Oct 2026.** Upkeep schedule (clock-change weekends, monthly checks), the upkeep panel on the plan, and an `.ics` export built by hand with folding and escaping (no calendar library, no server state). Security review of M2 and M3 found the HttpClient factory logging postcode URLs at Information; fixed with `RemoveAllLoggers()` plus Serilog category overrides, and pinned by a test that runs the real adapters against a loopback stub and asserts the postcode never reaches the log.
- **M4 done, 30 Oct 2026.** Kit catalogue: `KitPick` value object, embedded JSON catalogue validated at startup (https only, host allowlist, real action ids, dated prices), Amazon tag appended once on an exact host match, picks under each action with the disclosure. 303 tests, all gated assemblies at 100% lines and branches. Adversarial pass in `docs/threat-models/kit-catalogue.md`: look-alike hosts, empty tags and undated entries are all refused with a sentence rather than a stack trace.
- **M5 done, 30 Oct 2026.** House on a map: Dapper over a read-only SQLite MBTiles archive (`MbTilesBasemap`, status re-probed each minute while absent), `HybridCache` decorator, tile and status endpoints under their own rate-limit bucket, MapLibre 6.4.1 vendored from SiteWise with the Open Sans glyphs, a Selficient-palette style in light and dark, the pin kept in IndexedDB (store version 2, export and import carry it), plain panel without a basemap or in Northern Ireland, and the guarded Zoomstack fetch as the last deploy step. Verified in Chrome against the real archive at NP12 1GW in both themes. 349 tests, every gated assembly at 100%. Outcomes in `docs/threat-models/map.md`. Stage A's "footprint outline and long axis" is deferred to M6, where it feeds the roof orientation.
- **M6 done, 30 Oct 2026.** Solar and battery estimator at `/plan/solar`: value objects for aspect, pitch, panels, battery and demand; a deterministic monthly model (`SolarEstimator`) with four tariff strategies, cost breakdown and payback; PVGIS 5.2 adapter with cache and fake; Google Solar Building Insights adapter behind a consent handler, a key-shaped option and a null object; grants by nation with review dates; a pure-CSS compass picker and a server-rendered SVG chart; the plan links to it for any home with its own roof. Rob's roof reproduces the review's saving band and payback. 450 tests, every gated assembly at 100%. Outcomes in `docs/threat-models/solar.md`. Not done from the spec: the Google panel over aerial imagery (table of segments instead) and the footprint long axis from the map (the aerial survey gives the aspect directly, which is better).
- **M7 done, 30 Oct 2026.** Installable app: manifest with drawn icons (pine, sky, lantern), the service worker generated from a template with the precache list and version from a scan of the shell directories (`ShellVersion`, SiteWise's pattern), network-first pages with a release-stamped slow-connection fallback, cache-first libraries, fonts and tiles, never-cached API routes, the last plan POST kept under `/plan` for offline, an offline page, the honest offline banner, build-skew self-heal, and the install button with the iOS how-to. Verified in Chrome: worker installed with every shell entry, plan page cached with the release header. 463 tests, every gated assembly at 100%. Outcomes in `docs/threat-models/pwa.md`. Owed to M10: the real offline reload and Lighthouse installability in Playwright.
- **M8 done, 30 Oct 2026.** Photos and AI: SkiaSharp sanitiser (re-encode to a bounded JPEG, metadata gone, orientation applied, byte and pixel caps before decode); three photo moments on `/plan/photos` with server-rendered answers and a browser-side tick-up; Claude Opus 5 at low effort through the official SDK with structured JSON output, a cached system prompt and candidate-only action ids; a deterministic fake, a null object, an in-memory meter (five a month per household, five hundred a day for the site) and an AI rate-limit bucket that counts posts only; "explain my plan" on `/plan/explain` from the re-posted profile; the household's own key in the browser, with the prompts published at `/api/ai/prompts` and the CSP widened to Anthropic on that page alone. 520 tests, every gated assembly at 100%. Outcomes in `docs/threat-models/photos-and-ai.md`. Verified in Chrome end to end against the fakes.
- **M10 done, 30 Oct 2026.** Hardening and launch: fifteen Playwright journeys in Chromium at phone width (postcode to plan, ticks and reload, export and import, the saved plan offline, the solar estimate, the install button, axe on every public page and on a built plan); NBomber scenarios with targets that fail the script when missed (`./build/run-load.ps1`, all met at 450 requests a second with p99 under 15 ms); Lighthouse 99/100/100/100 on six pages after response compression, stylesheets linked side by side rather than imported in a chain, WOFF2 font subsets with narrowed axes, the install button moved to the footer and two contrast fixes (`--c-lantern-ink`, a lighter dark pine with a dark button label); Kestrel limits and a thread-pool floor; the dependency audit clean; the CI concurrency group moved to the deploy job because the workflow-level group had been cancelling every build since M4; `docs/configuration.md`. The journeys found four real defects the unit tests could not (first-visit worker reload, ticks before the script listened, no import on the empty state, lantern text contrast). 545 gated tests, every gated assembly at 100%. Outcomes in `docs/threat-models/launch.md`; what remains is Rob's, in `docs/handover.md`.
- **M9 done, 30 Oct 2026.** Marketing, legal and search: the landing page beyond the hero (the pitch, the four numbered steps, the privacy strip, what it covers, five questions), privacy and terms pages fed from one `LegalCopy`, canonical and share metadata with a drawn share image, `noindex` on every personal or transient page, a hand-kept sitemap, `robots.txt` with the private routes disallowed, `llms.txt`, and JSON-LD (WebApplication, Organization, FAQPage) serialised from the page's own questions. A copy-rules test refuses exclamation marks, apologies and "Submit" on every public page. 539 tests, every gated assembly at 100%. Outcomes in `docs/threat-models/marketing.md`.

- **M13 the board, 30 Oct 2026.** The engine reaches the page. `BoardComposer` (Application) builds one set per household and chosen scenario: a card per scenario ranked by the house's risks, a board per candidate source (power banks, the two power stations, rooftop solar and battery, a generator, the car's output where it has one) and per season, what the house holds as the current board, and for every untaken plan action the cells it would turn. The plan page's hero figure is now the board's Days Ready for the chosen scenario, with the lede naming it. The board itself is a `<details>` row per need with a cell per horizon, in Held, Reduced, Not enough and Lost with the day count where a cell changed, the reason as a whole sentence, a key, and the flat's note about pumped water. Scenario cards, source and season pills redraw in place; a tick, a card or a new dependency posts to `/plan/board` and swaps the fragment in, debounced, with the plan page's own board as the no-script answer. "What you rely on" lets a household add up to twelve devices, medicines or supplies, kept in IndexedDB (store version 3; export carries them as `selficient: 2`) and re-posted with the profile. The wizard asks the newer questions (car kind, floor, chest freezer, wood stove, water butt, growing, infants, medicine in stock) with defaults so older posts still work. Rob's rename applied: "A storm takes the power out", because a storm can come in any season. 617 gated tests, 23 journeys, every gated assembly at 100%; threat model and outcomes in `docs/threat-models/board.md`. Coverage lessons pinned in `CLAUDE.md`: the "current board" is stored on the set rather than searched for, because a compound predicate over an ordered list leaves a branch no input can reach.
- **M14 the look, 30 Oct 2026.** The design direction v2 built: the landing hero on the night ground, bleeding to the viewport edges by container units rather than a viewport trick, with the glow behind the figure, the contour band and the kitchen-table still life; the plan's instrument panel as the same night with its glow and the strip. The strip is Dark Sky's form for the board: a bar per need (water, food, warmth, light) from now to where it runs out against the 3, 7 and 30 marks, hatched lantern where the need is met by a lesser means, and it follows the board on every redraw. The landing figure and strip are the engine's for a typical house (four people, a semi, a gas boiler, nothing put by) rather than a typed number, so the copy changed from half a day to what the engine says. An illustration system in SVG: thirty-five marks (one per need, one per source, one per scenario, the house, the generic dependency and the general action) in one sprite referenced by `<use>`, drawn into the board rows, the source pills, the scenario cards and the plan's actions; three still lifes (the kitchen table, the larder shelf, the hill) as inline partials that recolour with the theme. Motion as one orchestrated moment: the glow comes in over 600 ms, the figure counts up from nothing over 900 ms, the strip's bars draw left to right a step apart, the scenario cards settle 80 ms apart, and a tick pulses the lantern once; all of it behind `prefers-reduced-motion`. A sprite test holds the drawing to the catalogue: every need and every source the composer can offer has a mark, and nothing is drawn for nothing. 628 gated tests, 23 journeys, every gated assembly at 100%. Lessons in `CLAUDE.md`: a full-bleed band by box-shadow or clip margin widens the page on a phone, `<use>` of an external sprite cannot carry a stylesheet under the CSP, and a ternary in a Razor view is a branch the gate counts.
- **M15 sweep, 30 Oct 2026.** The adversarial and performance pass over M13 and M14. Dependencies clean (no vulnerable or deprecated packages, `npm audit` clean). Found and fixed: the board fragment shared the plan POST's bucket of 30 a minute, so a household ticking its way down the plan would have gone silently stale; it has its own bucket of 120 (`RateLimits:BoardPerMinute`). Found by the load run, which the fragment joined at 100 a second: every target missed by two orders of magnitude. Two causes. The plan page rendered twelve sheets (every source, both seasons) at 342 KB of HTML a request; it now renders the one sheet in view (95 KB) and a switch posts for the next, which also cut the set's compose from 3 ms and 3.25 MB to 2 ms and 2.25 MB. And the load harness, a console project, ran the workstation collector where the site runs the server collector, so the allocation serialised everything; the harness now declares what production has. Plan POST p50 7 ms and p95 60 ms at 200 a second with the fragment at 100 a second alongside; all five targets met. Lighthouse after the look: 99/100/100/100 on the landing page and the wizard. The load script takes a rate scale so one request can be costed on an idle server. Twelve-row cap in the browser now says so rather than ignoring the thirteenth. 628 gated tests, 23 journeys, every gated assembly at 100%. Outcomes in `docs/threat-models/board.md` and `docs/benchmarks.md`; what is Rob's is in `docs/handover.md`.
- **M16 Rob's review, 30 Oct 2026.** Four things from using the site. A way to clear the plan: a folded control on the plan page with its sentence and an ember button that empties the browser's stores and returns to the landing page. The kit picks linked to shop homepages; every link is now the shop's own search for the named item, so it lands on the thing and never goes stale, the Amazon tag still appends, and a catalogue test refuses a bare homepage. Cost first: picks were already cheapest first; the first of several now says "Lowest price that does the job" and the list says how it is ordered and how the links work. "Use my location" beside every postcode box (landing, wizard, solar): the browser's position, rounded to about ten metres, posted (never a query string, so no log on the proxy gets it) to a new endpoint that asks postcodes.io for the nearest postcode within two kilometres, with a bounding-box refusal before any upstream call, no caching, and a sentence for every failure; a fake that knows Rob's house, the sea and an outage; `Permissions-Policy` opened to the page itself; the privacy table names the flow. Threat model in `docs/threat-models/location.md`. Also the landing hero's example postcode was Rob's own and read as preloaded; it is a Cardiff one. The monetisation question is answered in the conversation and summarised in the handover: affiliate links and solar lead generation rather than an ad network, which the privacy promise and the CSP both rule out.
- **M17 the plan page flow, 30 Oct 2026.** Rob found himself bouncing between the ticks and the figure. The page is one column now, in the order the questions come (`docs/design/plan-page-flow.md`): the verdict with the seven scenarios as chips beside it; the plan, with each tier header saying where that stretch takes the house (one cumulative simulation per tier, `TierOutcome` on the set); the board with its pills; what you rely on under the board it adds rows to; the three risks that order the plan with the rest folded; the map; upkeep; the tools. A verdict bar (figure, scenario, the strip in miniature) fixes to the top of the viewport once the hero has scrolled away, by an intersection observer rather than a scroll listener, so a tick anywhere answers where the eye is. Found by the journeys: moving the scenario form out of the fragment removed the only antiforgery token the redraw script read, and every tick failed silently until the script read the token from the page. 25 journeys, every gated assembly at 100%.
- **M18 the catalogue as a system, Phase A, 31 Oct 2026.** The first pick Rob opened did not land, and the whole commercial model is the links. After the Chromium link check and the Monday workflow (`docs/threat-models/kit-catalogue.md`, second pass), the catalogue became a system (`docs/design/catalogue-system.md`): a SQLite database through Dapper seeded from the shipped JSON and read into memory for the pages (the shipped picks stand in until the store has answered once); a hosted worker that wakes every half hour and, once a night after the hour, opens every shown offer over HTTP (bounded read, 25 s, no browser), judges it by rules (dead, robot wall, no results, nothing named) and then by Claude against a JSON schema where a key is set (advisory on the verdict, never un-hiding, price read or null), records a check, and hides an offer after two failures running; `/out/{offer}` counting one click per offer per day with no cookie and no-store before the redirect; and `/admin/catalogue` behind `Operator:Key` with a Data Protection cookie, showing every offer with its last check and its 30-day clicks, with hide, show and verify-now. 21-case threat model in `docs/threat-models/catalogue-system.md`. Found by the tests: SQLite hands Dapper `long` for every INTEGER and the row records had said `int`; the host runs a background service's `ExecuteAsync` off the calling thread, so a timer created after the first load lost the tick that arrived during it. Then Phase C and D the same day: "Found it cheaper?" under every action takes a reader's link and nothing else (https, a shop on the allowlist, one per link, fifty waiting at most), the worker opens it on its next wake and the reviewer approves it; `/deals`, the cheapest pick per action with the quarter's drops first; and the share card, `/share/{scenario}/{days}` with a Skia-drawn PNG, from "Share this figure" on the plan. Phase B (Awin and Amazon feeds, link builders, live prices) waits on Rob's accounts, listed in the handover.
- **M0 done, 29 Oct 2026.** Gate green: 59 tests, 100% lines and branches on Domain, Infrastructure and Web; tsc and ESLint clean; both themes verified in Chrome at desktop width. Three things found by the gate and the design pass, now pinned by tests or notes: Razor's attribute encoder mangles standard base64 nonces (URL-safe base64 used); the framework's import-map tag helper strips `type="importmap"` (element opted out with `<!script>`); CSP blocks inline `style` attributes, so swatches use data-attribute selectors. Carried to M2: the phone-width check runs in the Playwright suite, because the browser extension cannot resize a maximised window; `Selficient.Application` joins the coverage gate with its first use case.

## 12. Decisions (answered 29 Oct 2026)

1. **Plan approved with changes** (the changes being items 3 and 4 below).
2. **Repo:** code lives in this repo under `src/` and `tests/`; private GitHub remote created at `Dev-Studios-Ltd/Selficient` alongside SiteWise.
3. **Hosting:** no staging; test locally. Deploy workflow is SiteWise's with the shop's standard points of difference (section 9). Rob provisions the vhost, app pool and `self-hosted-selficient` runner label when ready.
4. **API keys:** Rob sets up the Google Cloud project (Solar API) and the Anthropic key before M6 and M8; both arrive as env vars on the runner; the build proceeds against fakes until then.
5. **Fonts:** Bricolage Grotesque, Atkinson Hyperlegible and IBM Plex Mono as in the design direction unless Rob objects (all SIL Open Font Licence, self-hosted).
- **M11 design pass, 30 Oct 2026.** Rob opened the site at desktop width and found a phone column pinned to the left of an empty page with no mark on it: the two-column desktop wireframe in `product/design-direction.md` had never been built, and every check from M0 to M10 ran at 390 px. Built in this pass: the slate roofline header and footer with the contour-ring mark; the landing hero as a two-column panel with a postcode box that hands over to the wizard and a contour-map card drawn from the house's elevation; the landing sections set across the width (four-step row, privacy band, two-column questions, a roofline closing band with the lantern button); the plan as a sticky instrument panel beside a two-column sheet with the risks in a grid and the later tiers folded; the wizard as intro-beside-form with two-column fieldsets; the solar form two by two; the photo moments three across. A screenshot journey now captures every page at phone and desktop widths in both themes into `artifacts/screens` on every E2E run, so the next drift is seen in the run rather than by a person. 21 journeys, 549 gated tests, every gated assembly at 100%.
- **M12 engine v2, 30 Oct 2026.** The household resilience model in `Selficient.Domain.Resilience`: eleven utilities that can stop, seven scenarios as timelines of what stops and what comes back (a storm, the water stopping, snow, a fortnight of regional disruption, months of shortages, a heatwave, staying at home), nineteen needs on four rungs with their demand rules, sixty providers (fixtures of the house, stocks, flows, energy sources and enablers) each with its figures and source, household-added dependencies (devices, medicines, supplies) bounded and validated, and one deterministic day-by-day simulation that hands energy out rung by rung, carries each load once a day, and draws every cell as held, reduced (met by a lesser means), rationed (not enough) or lost. Days Ready becomes the first day anything on the Survive rung is rationed or lost, with blankets counting as surviving except for a frail person or a baby (Rob's option A). The probe before the tests found and fixed four modelling faults; the edge cases handled and the ones still open are in `docs/design/edge-cases.md`. 43 engine tests including five FsCheck invariants; 594 gated tests; every gated assembly at 100% lines and branches; the board benchmark is in `docs/benchmarks.md`.
