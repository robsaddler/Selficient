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

## Conventions

- .NET 10, `Selficient.slnx`, central package management, warnings as errors, analyzers at latest-Recommended.
- Tests: xunit v3 on Microsoft.Testing.Platform, Shouldly, NSubstitute. Name tests as prose: `Adding_water_never_lowers_days_ready`.
- Commits: imperative subject, body explains why, trailer `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` when Claude authored.
- Branches: `development` for integration, `main` deploys to production. No staging; test locally.
