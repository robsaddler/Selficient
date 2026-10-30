# Selficient — working rules

Read `plans/build-plan-v1.md` before touching code. It governs architecture, patterns, security, performance, testing and the per-feature workflow. `product/design-direction.md` governs every visual and copy decision.

## Non-negotiables

- **Never caveman the app.** Every user-visible string is a whole, explicit sentence in the interface's voice (sentence case, plain verbs, no filler, no exclamation marks, no apologies). Pin wording with tests.
- **Dapper, never EF Core.** No database in v1 at all.
- **No framework, bundler, transpiler or preprocessor on the front end.** Modern platform CSS and JS only. No inline script or style; CSP nonces are the only exception and are injected by the tag helper.
- **100% line and branch coverage per production assembly.** `./build/run-tests.ps1` is the gate locally and in CI. Exclusions only as documented in `docs/coverage-exclusions.md`.
- **Every feature starts with a threat model** in `docs/threat-models/<feature>.md` and failing adversarial tests. Nothing merges without them and a security review pass.
- **Dependency rule:** Web → Application → Domain; Infrastructure → Application; Infrastructure is referenced only from the composition root. Architecture tests enforce it.
- **No personal data on the server in v1.** Postcodes are logged as outward district only. Photos are processed in memory and discarded.
- **Secrets are environment variables.** Never in the repo, never in workflows.
- **The rules engine is deterministic and owns Days Ready and the plan.** AI explains, counts and answers; it never computes.

## Gotchas already paid for

- A `<script type="importmap">` in Razor must be written `<!script …>` to opt out of the framework's import-map tag helper, which strips the type; stamp the nonce by hand from the injected `CspNonce`.
- Nonces are URL-safe base64 because Razor's attribute encoder rewrites `+`, `/` and `=`.
- No `style="…"` attributes anywhere, even in demos: CSP blocks them. Map state to data attributes and select on those.
- `color-mix(in oklab, …)` for tints; oklch interpolates hue towards white's hue of 0 and turns green tints pink.
- `const decimal` emits a static constructor the tests never run; use a static property.
- `dotnet test … --results-directory` must come before the `--` that hands arguments to the testing platform.
- A vendored stylesheet loaded by a plain `<link>` is unlayered and beats every `@layer`; overrides for it (the MapLibre chrome in `map-chrome.css`) must be unlayered too, imported last from `app.css`, and at least as specific as the library's own selector.
- A `try` block that returns from inside leaves its closing brace uncovered; assign inside the `try` and return after it.
- `tsc` refuses `declare module "/lib/…"` (rooted names count as relative); route vendored libraries through the import map by bare specifier and type them in `types/*.d.ts` with a `paths` entry.
- Shell `perl -0pi` substitutions break on `|` and on `$1` followed by a space and a digit; use the Edit tool for anything but one-line `sed`.
- Razor reserves `<text>`; an SVG `<text>` element must be emitted with `Html.Raw` from constant content (the month labels), never from user data.
- The Anthropic C# SDK (13.x) sends the key as `Authorization: Bearer`, not `x-api-key`, and its message types are `UserMessageParam` / `ImageBlockParam { Source = new Base64ImageSource }`; a stub must match the bearer header. Reflection on the SDK assembly from PowerShell 5.1 fails (net8 assembly); grep the DLL bytes or write a throwaway console probe in the scratchpad.
- SkiaSharp decodes a PNG with garbage pixel data as "incomplete" rather than failing; a palette PNG with no PLTE chunk is the input that opens as a codec and fails to decode.
- `.webmanifest` is not in the framework's content-type map; it is mapped in the pipeline. The worker template under `wwwroot` is never listed in the shell because the shell scan covers only `css`, `js`, `lib`, `img` and `fonts`.
- `WebApplicationFactory` finds the content root through the test project's manifest; a runner started from elsewhere (the load project, the E2E fixture) needs `UseSolutionRelativeContentRoot("src/Selficient.Web", "Selficient.slnx")`, fully qualified because two extension classes share the name. `StartOnKestrel()` on the factory gives a real port for Playwright and NBomber.
- Every option is range-validated at startup; a fixture that lifts a rate limit must stay inside the range (`AiPerMinute` tops out at 1,000) or the host refuses to start with a message that looks like a test failure.
- A service worker's first `controllerchange` on a device is not an update; reloading on it reloads every first visit and destroys any script context a test is evaluating in. `shell.js` only reloads when a worker is replaced.
- axe samples colours mid-animation; audit after `document.getAnimations()` has finished or the lifting Days Ready figure reports a contrast failure.
- Lantern is a fill colour. Lantern as text is `--c-lantern-ink`; the bright value fails 3:1 on the light ground and the plan page only passed because the contour band hides the background from axe.
- Stylesheets are linked from the layout one per layer, each wrapped in `@layer name { … }`, with `app.css` first to fix the order. Do not reintroduce `@import`: it chains round trips on a slow phone.
- Fonts are WOFF2 subsets made by `build/subset-fonts.mjs` from the foundries' files, which are not in the repo; `tokens.css` declares the narrowed weight and width ranges, so a new weight needs a new subset.
- `Short` is a type name to CA1720 and cannot be an enum member; the board uses `Rationed`. An enum `switch` expression compiles to a jump table with a branch no valid value reaches, so coverage never hits 100% on it: use a dictionary lookup for enum-to-value maps.
- A test that drives a simulation must be written from the engine's own output, not from the sentence in the design document: the probe in the scratchpad (`dotnet run` against the Domain project) is how the M12 goldens were read before they were pinned.
- `TimeProvider.TimestampFrequency` is the Stopwatch frequency: ten million a second on Windows, a thousand million on Linux. A test clock must override it (the TestKit `ManualClock` uses TimeSpan ticks) or an advance of a day is a hundred days on the ubuntu runner, which only CI sees.
- Look at the site, do not only test it. A desktop-width screenshot of every page is part of every E2E run (`artifacts/screens`); open them before calling a milestone done. The M0 to M10 build shipped a phone column on a 1500 px screen because nobody did.
- `dotnet run` on the web project locks `Selficient.Web.exe`; stop it before `dotnet build` or the build fails to copy the apphost.
- Shouldly's `ShouldContain(string, string)` second argument is the comparison, not the message; write `customMessage:` by name or the assertion silently compares the wrong thing.
- Fields a fragment endpoint re-posts must carry the page model's prefix (`Form.Postcode`, `Form.Dependencies[0].Name`); a bare `Postcode` binds to nothing and every redraw is a 400 with the first required-field sentence.
- A data attribute used as a selector (`data-action-id`) must name one thing; a second list that reused it broke the first's `querySelectorAll`. Give each list its own attribute.
- A compound predicate over an ordered list (`First(b => b.Source == house && b.Season == season)`) leaves a branch no input can reach when the matching item is always first; store the value instead of searching for it.
- A full-bleed band inside the centred column: `body` is an inline-size container and the band is `inline-size: 100cqi; margin-inline-start: calc(50% - 50cqi)`, padded back to the column. A `box-shadow: 0 0 0 100vmax` with `clip-path` does not widen the page but anything positioned past the box does, and `overflow-clip-margin` counts toward the scroll width; `overflow: clip` without a margin is the fence.
- An SVG sprite used through `<use href="/img/marks.svg#id">` cannot carry a `<style>` element (the CSP has no inline style allowance); put the stroke and fill as presentation attributes on each `<symbol>`. Custom properties do inherit into the `<use>` shadow tree, so `fill="var(--c-lantern, #E8A33D)"` follows the theme.
- A ternary in a Razor view is a branch the gate counts, and a view only renders what the tests post; put the choice in a helper (`BoardCopy.Unit`) and pin it there.
- The load harness hosts the site in a console project: declare `ServerGarbageCollection` there or it measures the workstation collector under the web server's allocation and every scenario collapses together.
- A page that pre-renders every variant for an in-place switch pays for all of them on every request; render the one in view and post for the next.
- Lighthouse runs from the scratchpad (`npm install lighthouse@12`, `CHROME_PATH` to Playwright's Chromium, `--ignore-certificate-errors` against the dev certificate); it is not in the gate because its scores are simulated.

## Conventions

- .NET 10, `Selficient.slnx`, central package management, warnings as errors, analyzers at latest-Recommended.
- Tests: xunit v3 on Microsoft.Testing.Platform, Shouldly, NSubstitute. Name tests as prose: `Adding_water_never_lowers_days_ready`.
- Commits: imperative subject, body explains why, trailer `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` when Claude authored.
- Branches: `development` for integration, `main` deploys to production. No staging; test locally.
