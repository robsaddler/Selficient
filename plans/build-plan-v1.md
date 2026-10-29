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

Targets on a single VPS vCPU pair, measured by NBomber against staging:

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
8. **Performance:** BenchmarkDotNet baselines committed; NBomber load runs in the deploy workflow against staging, failing on target breach.
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

- `pr-build-and-test.yml` on PRs to `development`/`main`: restore, build (warnings as errors), test with coverage thresholds, architecture tests, `dotnet list package --vulnerable`, Playwright smoke in a container, ReportGenerator artifact.
- `build-test-and-deploy.yml` on push to `main`: as above, publish, artifact, self-hosted Windows runner deploys to the **staging** vhost first (stop pool, robocopy `/MIR` excluding `logs App_Data`, start pool, `/health` poll), runs NBomber against staging, then the same to production. Concurrency group `deploy-production`.
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
| M0 | Foundation | Solution, CPM, analyzers, test projects, architecture tests, coverage gate, both workflows, staging vhost, health, security headers, CSS layers and tokens from the design direction, fonts, import map and `app.js`, first custom element (`<days-ready>`), `/styleguide` page, `tsc --checkJs` and ESLint in CI, ADR-001 | CI green at 100% on an empty-but-real app; headers test passes; styleguide renders in both themes; deploys to staging |
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

Sequence is strict for M0–M2; M3–M9 can interleave; M10 last.

## 12. Decisions needed from Rob before M0

1. Repo home: keep this repo and add `src/` and `tests/` beside `plans/`, `product/`, `research/`? (Recommended: yes, docs and code together.)
2. GitHub: create the remote now so the workflows and self-hosted runner can be wired at M0?
3. Staging and production vhost names on the VPS, and whether the SiteWise self-hosted runner can serve Selficient or needs its own label.
4. Google Cloud project and Anthropic API key ownership for F5 stage B and F7 (env vars on the runner).
5. Confirm the font choices in the design direction (self-hosted Bricolage Grotesque, Atkinson Hyperlegible, IBM Plex Mono; all SIL Open Font Licence).
