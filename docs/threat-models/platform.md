# Threat model: the platform (M19)

*31 October 2026. The enterprise pieces under the site: the SQL Server database and its migrations, the keys and the cache that live in it, the health probes, the telemetry, the operator's audit trail. Appended as each piece lands; the adversarial pass follows each.*

## What it is

Selficient's first infrastructure beyond the web process. SQL Server, reached through Dapper only, holds the catalogue (offers, checks, clicks, runs), and from this milestone the Data Protection key ring and the distributed cache behind `HybridCache`. The schema is migrated at start from scripts embedded in the build, serialised by an application lock. Health probes answer liveness and readiness separately. Telemetry leaves through OpenTelemetry to wherever the environment points it. Every hand the operator takes on the reviewer is recorded.

## Assets

- The database: the catalogue is the business, the key ring signs the operator's session, the cache is the price of a cold start.
- The connection string: it is the one secret that opens all three.
- The schema: a migration that runs twice, or out of order, or half-way, is a broken site that looks healthy.
- The vhost's standing: health probes and telemetry are the first things a stranger reads about the server.
- The test server and the CI runner: a test that writes outside its own database, or leaves one behind, is a leak that compounds.

## Abuse and failure cases

| # | Case | Control | Test |
|---|---|---|---|
| 1 | SQL injection through any field that reaches the store | Dapper with named parameters on every statement; the only interpolated identifiers are the test kit's own GUID database names, suppressed with a written justification; no raw SQL takes a caller's text | The store tests write names with quotes and ampersands; `CA2100` is an error everywhere else |
| 2 | A migration runs twice, or two app-pool processes migrate at once | `schema_versions` records every script; `sp_getapplock` serialises the run and times out loudly after a minute; each script is its own transaction | `The_shipped_scripts_apply_once_in_order_and_are_recorded`, `Two_hosts_starting_together_migrate_once_between_them` |
| 3 | A deploy ships a page against an old schema | Migration is the first hosted service and runs to completion in `StartAsync` before Kestrel answers; a failure fails the start and the deploy's health step | `The_start_up_step_migrates_unless_told_not_to…`; the composition test pins it first in the hosted-service order |
| 4 | A shipped script is edited after it ran on the vhost | The rule is written on the migrator and the scripts are numbered; a script's name is part of its record, so a renamed one shows in `schema_versions` as a mismatch the runbook says to look for | `Scripts_split_on_their_go_lines…` pins the naming; runbook `docs/operations.md` |
| 5 | The connection string leaks | It is an environment variable on the runner and nowhere else; the SqlClient logger is not registered; the health probe never echoes configuration | `docs/configuration.md`; `The_health_endpoint_answers_ok_and_nothing_else` |
| 6 | The site starts without a database and serves stale or empty pages | `Database:ConnectionString` is required and validated at start; the host refuses to start without it, so the deploy fails rather than the catalogue silently emptying | Options validation at start; `The_composition_root_wires_the_store…` |
| 7 | Tests touch a shared database or leave databases behind | Each test host creates a database named by a fresh GUID on the server named by `SELFICIENT_TEST_SQL` and drops it on dispose after clearing the pools; the runner's server is a service container that dies with the job | `TestDatabase`; the local run ends with zero `selficient_test_%` databases |
| 8 | A vulnerable transitive package rides in with the client library | NuGet audit is warnings-as-errors; the Pkcs package SqlClient still references is pinned centrally to the patched release | The build fails on any advisory |
| 9 | `INTEGER` widths and dates drift between the database and the records | Columns are declared `BIGINT`/`INT`/`DATE`/`DATETIME2` to match the records exactly; dates are stored and read as UTC | Every store test reads back what it wrote |

## Outcomes

*Database pass, 31 October 2026.*

- All nine hold. The whole Infrastructure, Web, E2E and load suites now run the real store against a real SQL Server with a database each; no store call is mocked anywhere.
- Tried: two migrators started together on an empty database. The lock serialised them and one of them did nothing. Pinned.
- Tried: a 500-character evidence sentence and a 200-character page title. Both are clipped to their columns before the insert rather than failing it. Pinned.
- Residual: the test kit's `CREATE DATABASE` interpolates a name; it is a GUID the kit made and the suppression says so. Accepted.

## Keys, cache, probes, telemetry, audit (31 Oct 2026)

| # | Case | Control | Test |
|---|---|---|---|
| 10 | The key ring is copied out of the database and used to forge the operator's cookie | Keys at rest are wrapped by the machine's DPAPI on Windows; a copy on another machine does not unwrap; the cookie is also bound to the application name | `The_key_ring_is_kept_whole_in_order_and_named`; the DPAPI wrap is the framework's, applied in the composition root |
| 11 | A cached value is poisoned through the cache table | The table is written only by the site's own login; the cache holds public lookups (geocodes, elevations, yields) and drawn cards, never a decision; a wrong value expires in thirty days at most | `The_cache_holds_a_value_for_a_second_read_and_for_a_second_process` |
| 12 | The readiness probe tells a stranger what is wrong | It says `ready` or `not ready` and the status code, no names, no versions, no descriptions; the detail is behind the operator key | `Readiness_says_ready_or_not_and_nothing_else_while_liveness_says_ok` |
| 13 | Telemetry carries a postcode or a coordinate off the box | Upstream spans are cut to scheme and host before and after the response; probes and tiles are not traced; nothing exports unless a collector is named | `An_upstream_span_keeps_the_host_and_loses_the_postcode`, `Probes_and_tiles_are_not_traced_and_everything_else_is` |
| 14 | The audit trail identifies a person | A hand records when, which, the offer id and the sentence; there is no user, address or agent column | Schema in `0003_operations.sql`; `The_operations_log_keeps_hands_and_runs_newest_first_and_clips_long_text` |
| 15 | A refused sign-in is invisible | Every refused key is a `sign-in` hand with "Refused" as its outcome, on the reviewer, newest first | `The_reviewer_shows_readiness_the_last_runs_and_the_last_hands…` |
| 16 | A job dies mid-run and nobody knows | A run row is opened before the work and closed after; a row still "running" after the wake interval is a crashed process, visible on the reviewer | `A_wake_opens_the_readers_proposals_first…` and the worker tests assert the run rows |

## The counter and the deals under load (31 Oct 2026)

| # | Case | Control | Test |
|---|---|---|---|
| 17 | A flood of clicks on one offer locks one row and the site queues behind the pool | The hop increments memory; one flush a second writes one row per offer per day; the hop touches no database | `A_shop_link_counts_the_click_in_memory…`, `Clicks_add_up_per_offer_per_day_and_leave_in_one_batch`; the load run's `out_get_200_per_second` target |
| 18 | The counter grows without bound | Its keys are offers times days and nothing else; an unknown or hidden id is a 404 before any count | `A_hidden_proposed_or_unknown_offer…` asserts nothing pending |
| 19 | The store is down and clicks are lost | A failed flush puts the batch back on the counter and logs how many wait; the next second tries again; a stop flushes once more | `A_store_that_refuses_the_batch_gives_the_clicks_back_to_the_counter`, `A_store_that_is_down_is_logged…`, the flusher's stop test |
| 20 | The deals page is a grouped read per visitor | One read per catalogue version and day, shared while it runs, as objects in process; a failed read is not kept | `The_deals_are_read_once_per_catalogue_version_and_day`, `A_read_that_failed_is_not_kept…`; the load run's `deals_get_50_per_second` target |
