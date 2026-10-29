# Coverage exclusions

The gate is 100% line and branch coverage per production assembly (`build/run-tests.ps1`). These are the only exclusions, each with its reason. Anything not listed here is covered or the build fails.

| What | Why |
|---|---|
| Source-generated files (`*.g.cs`, anything under a `Generators` path) | Covering `LoggerMessage` delegate caches would be covering the SDK, not the application. |
| `Program.cs` in `Selficient.Web` (the whole file, which holds only the entry point and Kestrel limits) | The composition root wires real adapters and starts Kestrel. Its behaviour is covered by the Web integration tests through `WebApplicationFactory`, which replaces the host entry point rather than executing it. The gate script excludes a file literally named `Program.cs` and nothing else. |
| `[ExcludeFromCodeCoverage]` on composition-root `ServiceCollectionExtensions` methods | Each carries a `Justification` naming the integration test that exercises the wiring. The attribute is forbidden anywhere else by an architecture test. |
