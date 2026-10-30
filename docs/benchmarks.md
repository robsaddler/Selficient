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

## The board (engine v2)

`dotnet run -c Release --project tests/Selficient.Benchmarks -- --filter "*BoardBenchmark*"`. Rob's house with this week's shelf and a battery.

| Date | Benchmark | Mean | Allocated | Machine |
|---|---|---|---|---|
| 30 Oct 2026 | Storm to a month (30 days, 19 needs, about 40 providers) | 84 µs | 77 KB | dev workstation, .NET 10, ShortRunJob |
| 30 Oct 2026 | Six months of shortages (180 days) | 514 µs | 415 KB | same |
| 30 Oct 2026 | Every scenario against five sources (35 boards) | 4.2 ms | 3.9 MB | same |

**Targets.** A board under 1 ms and under 500 KB; the whole Board page (every scenario, every source) under 10 ms. The first cut was 391 µs and 1.5 MB for the storm and 19 ms and 67 MB for the page, from LINQ in the day loop (a `FirstOrDefault` per provider per need per day and a `Where().OrderByDescending()` per load per day); laying the per-need provider lists and the store list out once per run brought it to the figures above with no change to any cell. The page will render this many boards per request only when a household switches sources, and the result is small enough to cache per profile.

## Lighthouse (phone, simulated slow 4G)

Lighthouse 12 against the Development build over HTTPS, mobile form factor, from a clean profile (first visit, nothing cached).

| Date | Page | Performance | Accessibility | Best practices | SEO | FCP | LCP | CLS |
|---|---|---|---|---|---|---|---|---|
| 30 Oct 2026 | `/`, `/plan/start`, `/plan/solar`, `/plan/photos`, `/privacy`, `/terms` | 99 | 100 | 100 | 100 | 1.6 s | 1.7 s | 0 (0.03 on photos) |

The first run of the day scored 63 to 70 on performance and 95 on accessibility. What moved it: the stylesheet was one file importing six, a chain of round trips on a slow link, and is now seven sheets linked side by side, each declaring its own layer; nothing was compressed, and Brotli and gzip now cover every text type, the manifest and the icon; the fonts were full variable TrueType files (900 KB on disk, 400 KB on the wire) and are now Latin-1 WOFF2 subsets with the axes narrowed (150 KB on the wire, made by `build/subset-fonts.mjs`); the install button lived in the header and pushed the page down when it appeared, and now lives in the footer; and lantern as text and pine in the dark each sat below the contrast ratio and have their own values now. Only the repeat visit, served by the worker, is faster than this.

## Rules engine targets

**Targets.** The build plan asked for under 1 ms and under 10 KB per plan. Time is two orders of magnitude inside the target. Allocation is above the 10 KB guess: a plan carries thirty actions each with a title and a why sentence built for the household, which is what the user reads, so the strings are the product rather than overhead. The allocation target is revised to **under 50 KB per plan** and the time target stays at under 1 ms. At 7 µs and 23 KB, a single core generates well over 100,000 plans a second before anything else is considered; plan generation will never be the bottleneck.
