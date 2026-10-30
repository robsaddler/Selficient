# Threat model: rules engine (M1)

*30 October 2026. Written before the code, per the feature workflow. Each case is encoded as a test named in the right-hand column; the file is updated with outcomes at the adversarial pass.*

## What it is

Pure Domain code that turns a household profile into a ranked risk register, a tiered plan of actions with costs, and the Days Ready figure. It runs on the server inside a request and, later, its outputs are stored in the browser. It has no IO and no secrets. Its attack surface is its inputs and the trust other code places in its outputs.

## Assets

- The honesty of the Days Ready figure (the product's promise).
- The determinism of the plan (caching, support, and reproducibility all rely on it).
- Server CPU and memory per request.
- The wording of every sentence (no path for user text into copy).

## Abuse and failure cases

| # | Case | Attacker or cause | Control | Test |
|---|---|---|---|---|
| 1 | Absurd household sizes (a million people, a thousand storeys) blow up costs, loops or allocations | Bot, fuzzing | Value objects bound every input: household 1–20 people, storeys 1–6, elevation −10 to 1,400 m; out-of-range is a `Fault`, never an exception | `Household_rejects_more_than_twenty_people`, `Property_rejects_storeys_outside_one_to_six`, `Elevation_rejects_values_off_the_british_isles` |
| 2 | Negative or non-finite quantities (negative children, NaN elevation) | Malformed request | `decimal` only (no NaN); children ≥ 0 and ≤ people; negatives refused by `Days`, `Litres`, `Money` | `Household_rejects_negative_children_or_more_children_than_people`, `Money_cannot_be_negative` |
| 3 | A profile combination yields an empty or tiny plan, leaving a household with false confidence | Logic gap | Baseline rule always contributes the government checklist regardless of profile; property test over random profiles | `Every_profile_gets_the_government_baseline` (FsCheck) |
| 4 | Two rules emit the same action id, double-counting cost and provisions | Developer error | `PlanBuilder` returns a `Fault` on duplicate ids rather than merging silently | `Builder_refuses_duplicate_action_ids` |
| 5 | Non-deterministic ordering (dictionary order, DI registration order) makes the same profile produce different plans | Framework behaviour | Builder sorts by tier, then rule order key, then id; two builds of the same profile are structurally equal | `The_same_profile_always_produces_the_same_plan` |
| 6 | Days Ready overstates readiness | Modelling error | Figure is the minimum across water, food and light; property tests prove it never exceeds any single dimension and never decreases when provisions are added | `Days_ready_is_the_weakest_dimension`, `Ticking_an_action_never_lowers_days_ready` |
| 7 | Days Ready understates or goes negative | Modelling error | `Days` cannot be negative; baseline inventory is defined for every household | `Days_ready_is_never_negative`, `A_household_with_nothing_still_has_the_baseline_half_day` |
| 8 | User text reaches a sentence (injection into "why" copy, later into AI prompts or PDFs) | Hostile input | Rules interpolate only formatted numbers from value objects; no string input exists on the profile at all | `The_profile_carries_no_free_text` (architecture test: no `string` properties on profile types) |
| 9 | CPU or allocation blow-up under load | Load | Rules are O(rules) with no recursion; benchmark pins < 1 ms and < 10 KB per plan; no LINQ in hot loops that allocate per item beyond the result lists | `PlanBenchmark` (BenchmarkDotMet, not in the gate), `Plan_generation_allocates_a_bounded_amount` |
| 10 | Cost overflow or rounding drift across tiers | Arithmetic | `Money` is integer pence; ranges sum with checked arithmetic | `Money_adds_in_pence_without_drift`, `Tier_costs_are_the_sum_of_their_actions` |
| 11 | Risk ranking tie-breaks differ between runs | Sorting | Score then kind order; test with equal scores | `Risks_with_equal_scores_rank_in_a_fixed_order` |
| 12 | A later upstream (flood lookup, elevation) feeds a value the rules treat as trusted | Compromised upstream | Those arrive as closed enums or bounded value objects only; `Unknown` is a first-class state that never removes a baseline action | `Unknown_flood_risk_keeps_the_baseline_and_adds_nothing_alarming` |

## Out of scope here

Transport, storage and AI are covered by their own models (M2, M3, M8). This engine never sees a postcode, a name or free text.

## Outcomes

*Filled in at the adversarial pass.*
