# Benchmarks

Run with:

```
dotnet run -c Release --project tests/Selficient.Benchmarks
```

Record the numbers here whenever the rules engine or the readiness calculation changes. Threat model case 9 (rules-engine.md) relies on these staying bounded.

## Rules engine

| Date | Benchmark | Mean | Allocated | Machine |
|---|---|---|---|---|
| 30 Oct 2026 | GeneratePlan (Rob's house: 30 actions, 8 risks) | 7.3 µs | 22.9 KB | dev workstation, .NET 10, ShortRunJob |
| 30 Oct 2026 | TickEverything (30 ticks with Days Ready after each) | 1.9 µs | 4.6 KB | same |

## Load (NBomber, `./build/run-load.ps1`)

The application hosted in-process on Kestrel against the fakes with the rate limits lifted, one shared client, twenty seconds per scenario, all four scenarios at once. Targets are the build plan's section 6; the runner exits non-zero when one is missed.

| Date | Scenario | Rate | p50 | p95 | p99 | Target p95 | Failed |
|---|---|---|---|---|---|---|---|
| 30 Oct 2026 | Plan POST (postcode, profile, full plan page) | 200/s | 1.6 ms | 5.2 ms | 13.1 ms | 150 ms | 0 of 4,000 |
| 30 Oct 2026 | Solar estimate POST | 50/s | 2.0 ms | 5.7 ms | 12.1 ms | 200 ms | 0 of 1,000 |
| 30 Oct 2026 | Tile status GET | 100/s | 1.0 ms | 4.0 ms | 7.7 ms | 50 ms | 0 of 2,000 |
| 30 Oct 2026 | Landing GET | 100/s | 1.3 ms | 5.1 ms | 8.3 ms | 100 ms | 0 of 2,000 |

Dev workstation, Release build, response compression on. An earlier run the same morning, before compression and the thread-pool floor, showed the same p95s with a p99 of 245 ms on the tile probe from a cold HybridCache; the floor removed it. All four scenarios together are 450 requests a second with a p99 under 15 ms, so a storm-warning evening is two orders of magnitude inside the server's reach and the rate limits, not the machine, are the ceiling.

*After M13 and M14 (the board on the plan, the strip, the marks), 30 Oct 2026, evening.* The plan POST now composes the board set and renders a sheet, and the board fragment joins the mix at 100 a second.

| Date | Scenario | Rate | p50 | p95 | p99 | Target p95 | Failed |
|---|---|---|---|---|---|---|---|
| 30 Oct 2026 | Plan POST (profile, plan, scenario cards, one board sheet, turns) | 200/s | 7.0 ms | 59.7 ms | 280.8 ms | 150 ms | 0 of 4,000 |
| 30 Oct 2026 | Board fragment POST (the same set, redrawn for a tick) | 100/s | 6.5 ms | 44.1 ms | 221.6 ms | 150 ms | 0 of 2,000 |
| 30 Oct 2026 | Solar estimate POST | 50/s | | 12.0 ms | 152.6 ms | 200 ms | 0 of 1,000 |
| 30 Oct 2026 | Tile status GET | 100/s | | 6.4 ms | 116.8 ms | 50 ms | 0 of 2,000 |
| 30 Oct 2026 | Landing GET (the typical house's board is simulated once at startup) | 100/s | | 8.5 ms | 135.7 ms | 100 ms | 0 of 2,000 |

The first run after M13 missed every target by two orders of magnitude (plan p95 46 s), and the cause was in two places. The plan page rendered every source for both seasons, twelve sheets and 342 KB of HTML a request; it now renders the one sheet the household is looking at (95 KB) and a switch posts for another. And the load harness hosts the site in a console project, which defaults to the workstation collector, while the web SDK gives the site the server collector; under the set's allocation (2.25 MB a request) the workstation collector serialised every request, including the tile probe. The harness now declares the server collector, as production has. What remains is the p99: the set allocates 2.25 MB, mostly the day-by-day rows of the boards simulated for what each untaken action would turn, and the pauses show at the ninety-ninth percentile. The target holds with margin; the allocation is the next thing to take out if it ever does not.

## The board (engine v2)

`dotnet run -c Release --project tests/Selficient.Benchmarks -- --filter "*BoardBenchmark*"`. Rob's house with this week's shelf and a battery.

| Date | Benchmark | Mean | Allocated | Machine |
|---|---|---|---|---|
| 30 Oct 2026 | Storm to a month (30 days, 19 needs, about 40 providers) | 84 µs | 77 KB | dev workstation, .NET 10, ShortRunJob |
| 30 Oct 2026 | Six months of shortages (180 days) | 514 µs | 415 KB | same |
| 30 Oct 2026 | Every scenario against five sources (35 boards) | 4.2 ms | 3.9 MB | same |
| 30 Oct 2026 | The set the plan page composes (`BoardSetBenchmark`: seven cards, one sheet, the house, about twenty-five turns) | 2.0 ms | 2.25 MB | same |

**Targets.** A board under 1 ms and under 500 KB; the whole Board page (every scenario, every source) under 10 ms. The first cut was 391 µs and 1.5 MB for the storm and 19 ms and 67 MB for the page, from LINQ in the day loop (a `FirstOrDefault` per provider per need per day and a `Where().OrderByDescending()` per load per day); laying the per-need provider lists and the store list out once per run brought it to the figures above with no change to any cell. The page composes one sheet a request since the M15 sweep (every source for both seasons was 3 ms and 3.25 MB, and twelve sheets of HTML); a switch posts for the next.

## Lighthouse (phone, simulated slow 4G)

Lighthouse 12 against the Development build over HTTPS, mobile form factor, from a clean profile (first visit, nothing cached).

| Date | Page | Performance | Accessibility | Best practices | SEO | FCP | LCP | CLS |
|---|---|---|---|---|---|---|---|---|
| 30 Oct 2026 | `/`, `/plan/start`, `/plan/solar`, `/plan/photos`, `/privacy`, `/terms` | 99 | 100 | 100 | 100 | 1.6 s | 1.7 s | 0 (0.03 on photos) |
| 30 Oct 2026, after M14 | `/` (night hero, strip, still life), `/plan/start` | 99 | 100 | 100 | 100 | 1.6 s | 1.7 s | 0.002, 0 |

The first run of the day scored 63 to 70 on performance and 95 on accessibility. What moved it: the stylesheet was one file importing six, a chain of round trips on a slow link, and is now seven sheets linked side by side, each declaring its own layer; nothing was compressed, and Brotli and gzip now cover every text type, the manifest and the icon; the fonts were full variable TrueType files (900 KB on disk, 400 KB on the wire) and are now Latin-1 WOFF2 subsets with the axes narrowed (150 KB on the wire, made by `build/subset-fonts.mjs`); the install button lived in the header and pushed the page down when it appeared, and now lives in the footer; and lantern as text and pine in the dark each sat below the contrast ratio and have their own values now. Only the repeat visit, served by the worker, is faster than this.

## Rules engine targets

**Targets.** The build plan asked for under 1 ms and under 10 KB per plan. Time is two orders of magnitude inside the target. Allocation is above the 10 KB guess: a plan carries thirty actions each with a title and a why sentence built for the household, which is what the user reads, so the strings are the product rather than overhead. The allocation target is revised to **under 50 KB per plan** and the time target stays at under 1 ms. At 7 µs and 23 KB, a single core generates well over 100,000 plans a second before anything else is considered; plan generation will never be the bottleneck.

## Load after M19 (SQL Server, the write-behind click counter, the deals page)

*31 Oct 2026, evening.* Two scenarios join the mix: the shop hop (`/out/{id}`, which counts a click and answers a 302) and the deals page. The data layer is SQL Server through Dapper rather than the file the earlier runs used, and the click path no longer touches the database on the request.

| Date | Scenario | Rate | p50 | p95 | p99 | Target p95 | Failed |
|---|---|---|---|---|---|---|---|
| 31 Oct 2026 | Plan POST | 200/s | 6.4 ms | 46.8 ms | 60.2 ms | 150 ms | 0 of 4,000 |
| 31 Oct 2026 | Board fragment POST | 100/s | 5.7 ms | 43.3 ms | 53.3 ms | 150 ms | 0 of 2,000 |
| 31 Oct 2026 | Solar estimate POST | 50/s | 1.2 ms | 3.2 ms | 7.8 ms | 200 ms | 0 of 1,000 |
| 31 Oct 2026 | Tile status GET | 100/s | 0.5 ms | 2.5 ms | 7.7 ms | 50 ms | 0 of 2,000 |
| 31 Oct 2026 | Landing GET | 100/s | 1.0 ms | 2.7 ms | 7.9 ms | 100 ms | 0 of 2,000 |
| 31 Oct 2026 | Shop hop GET (`/out/{id}`, counted) | 200/s | 0.5 ms | 2.5 ms | 5.9 ms | 50 ms | 0 of 4,000 |
| 31 Oct 2026 | Deals GET | 50/s | 0.7 ms | 2.7 ms | 6.3 ms | 100 ms | 0 of 1,000 |
| 11 Nov 2026, eight scenarios | Plan POST | 200/s | 7.4 ms | 51.7 ms | 67.4 ms | 150 ms | 0 of 4,000 |
| 11 Nov 2026, eight scenarios | Board fragment POST | 100/s | 6.4 ms | 46.9 ms | 60.8 ms | 150 ms | 0 of 2,000 |
| 11 Nov 2026, three choices and the tallies | Plan POST | 200/s | | 53.3 ms | 66.5 ms | 150 ms | 0 of 4,000 |
| 11 Nov 2026, three choices and the tallies | Board fragment POST | 100/s | | 47.0 ms | 58.6 ms | 150 ms | 0 of 2,000 |
| 11 Nov 2026, the household's own figures | Plan POST | 200/s | | 58.4 ms | 63.4 ms | 150 ms | 0 of 4,000 |
| 11 Nov 2026, the household's own figures | Board fragment POST | 100/s | | 51.8 ms | 55.7 ms | 150 ms | 0 of 2,000 |

Every simulation now goes through what the household has (`Possessions`), which copies a set for each what-if. A household that has counted nothing hands the engine the set it was given. One run of the first cut read 89 ms at the p95 with every other scenario doubled too, on a machine that was also serving a demo; the run above was taken after.

The three choices add two tick boxes to every action and five tallies to the page and to the fragment, and no simulation unless something is planned (then one); the same evening's run put both posts within two milliseconds of the run before it.

The eighth scenario (the whole grid going down) adds one card, and so one seven-day simulation, to every plan page; one run on 11 Nov 2026 put the plan POST about a millisecond higher at the median and five at the p95, and the other five scenarios where they were.

Dev workstation, Release build, 750 requests a second across seven scenarios. Three consecutive runs agreed to within about five milliseconds on the two posts and under a millisecond everywhere else. The shop hop is as cheap as the tile probe because it is a dictionary lookup and an increment: the catalogue is a snapshot indexed by offer id, and the clicks are written behind by a background flusher once a second, so a reader on their way to a shop never waits for a write.

### Why the earlier runs missed every target

Worth writing down, because the first answer was wrong twice. Run after run missed every target at once, including the tile probe, which does almost nothing: p50 stayed under a millisecond while p95 landed between 500 ms and 1.5 s on every scenario. Two wrong explanations were tested and discarded. It was not the M19 work: stashing all of it and measuring the committed M18 build missed every target in the same shape. It was not a loaded machine either, though a hundred-odd browser processes made that look plausible.

A stall of the same size at the same quantile across scenarios that cost wildly different amounts is not a cost, it is an event. The run was twenty seconds and the harness had no warm-up, so the first second, when the tiered JIT has promoted nothing, the connection pool is empty and every Razor page renders for the first time, is one twentieth of the samples and sits exactly on the p95. Tripling the run to sixty seconds moved the tile probe's p95 from 530 ms to 22 ms without a line changing, which is the proof: a sustained cost does not dilute, a single stall does.

The harness now warms up for five seconds before the clock starts, as a site that has been serving all day already has. The cold start itself is real and worth keeping in mind at a deploy: for about a second after the first request, under a burst of 800 a second, the slowest few per cent of requests take half a second. A deploy that takes traffic the instant it starts will show it.

The sixty-second experiment also found a second trap. The fixture lifts the plan limiter to its option ceiling of 10,000 a minute, and the plan and shop-hop scenarios each inject 200 a second, so any run past about fifty seconds crosses it and fills the report with fast failures that read like the site falling over. The harness now refuses such a run with a sentence telling you to shorten it or scale the rates.
