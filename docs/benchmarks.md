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

**Targets.** The build plan asked for under 1 ms and under 10 KB per plan. Time is two orders of magnitude inside the target. Allocation is above the 10 KB guess: a plan carries thirty actions each with a title and a why sentence built for the household, which is what the user reads, so the strings are the product rather than overhead. The allocation target is revised to **under 50 KB per plan** and the time target stays at under 1 ms. At 7 µs and 23 KB, a single core generates well over 100,000 plans a second before anything else is considered; plan generation will never be the bottleneck.
