# The household resilience model, version 2

*30 October 2026. Governs the second generation of the rules engine and the Board that shows it. Supersedes the "one number" model of v1 for everything except the headline, which it keeps.*

## What it has to do

A worried household asks, in roughly this order:

1. **What could happen to us here?** A storm night, three days without power, a burst main, snow on the hill, a fortnight of regional disruption, months of shortages, a heatwave, being told to stay at home.
2. **What would we lose, and when?** Not one number: the fridge on day two, the boiler on day one, phones when the mast battery dies, hot water, work, warmth, food, the car.
3. **What would change that?** A power bank, a power station, a home battery, the car, a generator, a wood stove, a water butt, an allotment, a stock of medicine, a skill.
4. **What about the things only we rely on?** A CPAP, an oxygen concentrator, insulin in the fridge, a stairlift, a baby's formula, a dog's medication, a business that must stay online.

The first version answers one and part of two for one scenario. Version two answers all four for any scenario, from a data model that a new scenario, need, piece of kit or household quirk slots into without a code change to the engine.

## The shape of it

Five kinds of thing, and one function.

```
Scenario  ──removes──▶  Utility (electricity, mains water, gas, mobile, internet, roads, shops, fuel, deliveries)
                              ▲
Provider  ──depends on────────┘           Provider ──meets──▶ Need ◀──demanded by── Household
   (stock, flow, energy source, skill)

Board = Simulate(household, providers held, scenario, season, horizon)
      → for every need, for every day: held / partial / lost, and why
```

### 1. Utilities

The things that come into a house from outside and can stop. A closed list, because every provider declares which it depends on and the engine has to know what "off" means.

`Electricity`, `MainsWater`, `MainsGas`, `MobileNetwork`, `FixedInternet`, `Roads`, `Shops`, `Fuel` (petrol and diesel at the forecourt), `Deliveries` (post, parcels, heating oil, LPG), `Pharmacy`, `Cash` (ATMs and card terminals, which fail with power and connectivity).

### 2. Scenarios

A scenario is a name, a sentence, a likelihood for this household (from the risk assessors we have), and a **timeline of utility states**: for each utility, when it fails and when it returns, allowed to be intermittent and to degrade rather than stop.

```
Scenario "A storm takes the power out"
  Electricity:    off from hour 0, back at the horizon (the user picks: 1 day .. 2 weeks)
  MobileNetwork:  degraded from hour 1 (mast batteries), off from hour 6, back with electricity
  FixedInternet:  off with electricity (the router), exchange back with electricity
  Roads:          degraded for 1 day (fallen trees)
  Shops:          open but no card payments while electricity is off
  Cash:           ATMs off with electricity
  Season:         winter by default

Scenario "Months of shortages" (a long supply-chain or fuel crisis, a war footing)
  Shops:          degraded from day 0 (rationed, gaps, queues), for the horizon (1 .. 6 months)
  Fuel:           rationed from day 0
  Deliveries:     degraded
  Electricity:    intermittent, 4 hours off a day from week 2 (planned cuts)
  Pharmacy:       degraded (substitutions, 28-day limits)
  Cash:           up
  Everything else up

Scenario "The water stops"           MainsWater off 1 .. 3 days, Shops up (bottled water sells out in hours: a degraded flag)
Scenario "Snow closes the road"      Roads off 3 .. 7 days, Deliveries off, Shops unreachable, Electricity at risk
Scenario "A heatwave"                Electricity degraded (evening cuts), MainsWater restricted, health demand up
Scenario "You must stay at home"     Shops unreachable for the household for 2 .. 4 weeks; everything else up
Scenario "A fortnight of regional disruption"  Electricity off 3 days then intermittent, Shops degraded, Fuel off, Cash off
```

Scenarios are data (`content/scenarios.json`), validated at startup like the kit catalogue, and ordered for a household by the existing risk ranking (flood scenario hidden for a house at 232 m, as now). A household can also adjust a scenario's horizon with the board's duration control: the horizons are 1 day, 3 days, 1 week, 2 weeks, 1 month, 3 months, 6 months, and a scenario declares which of them make sense for it.

### 3. Needs

The ladder from `outage-board-concept.md`, generalised. A need is a name, a rung, a **unit of demand**, and a demand function of the household. Needs are also data, but the demand functions are code, because they are the household arithmetic the engine already owns.

| Rung | Need | Unit a day | Demand comes from |
|---|---|---|---|
| Survive | Drinking water | litres | 3 a person, more in a heatwave, plus pets |
| Survive | Food | person-meals | 3 a person; infants have their own (formula) |
| Survive | Warmth | warm-room-hours | 24 in winter for a house with a vulnerable person or an infant, 16 otherwise, 0 in summer |
| Survive | Health | each item its own | medicines in days of stock; devices in watt-hours; cold storage for medicines |
| Survive | Infant and pet care | items | nappies, formula, pet food |
| Function | Light | lit-room-hours | rooms in use × dark hours by season |
| Function | Information | yes/no | a working radio or a charged phone with a signal |
| Function | Contact | yes/no | a way to call for help |
| Function | Cooking | hot-meals | 1 to 2 a day |
| Function | Sanitation | litres and items | flushing water, hand washing, wipes |
| Function | Cash | pounds | a few days of essentials in notes |
| House | Cold storage | yes/no | fridge and freezer contents kept |
| House | Heating system | yes/no | the boiler or heat pump running as normal |
| House | Hot water | yes/no | |
| House | Internet | yes/no | |
| House | Mobility | miles or days | the car usable and fuelled |
| Normal | Working from home | yes/no | only for a household that depends on it |
| Normal | Laundry | washes a week | |
| Normal | Entertainment | yes/no | |
| Normal | Income | yes/no | wages, benefits, a business |

A household can **add a need** (section 7), which lands in a rung with a unit and a demand it types in.

### 4. Providers

Anything that meets a need. Four kinds, one record shape:

```
Provider
  id, name, sentence
  meets:        [(need, amount per day or per use, conditions)]
  dependsOn:    [utility or energy]        -- what it needs to work
  kind:         Stock | Flow | EnergySource | Skill
  Stock:        capacity in the need's unit (36 litres; 9 person-days of food; 90 days of a medicine; 20 litres of fuel)
  Flow:         output per day by season (a water butt in rain; an allotment's harvest by month; a wood stove per log store; a well)
                with a lead time (an allotment planted in March feeds from June) and an upkeep (logs, seeds, time)
  EnergySource: store in kWh, usable fraction, ceiling in kW, surge in kW, self-draw in W, refill per day by season (roof, panels, fuel), reserve to keep (the car's miles)
  Skill:        a thing a person can do that unlocks providers (preserving, purifying water, first aid, lighting a stove)
  where:        indoors | outdoors only | garden needed | roof needed
  risks:        carbon monoxide, fire, fuel storage (so the plan can pair it with the alarm and the rules)
  cost, kit picks, upkeep:  as the kit catalogue has today
```

Electrical appliances are providers whose `dependsOn` is `Electricity` *or* an `EnergySource`, with a **load**: energy a day by season and a burst in watts, drawn from the DECC household survey and manufacturers (`content/loads.json`, each figure with its source). The fridge meets Cold storage; the boiler's pump and controls meet Warmth and Hot water and depend on MainsGas and Electricity; a heat pump meets Warmth and depends on Electricity at 16 to 25 kWh a winter day; a router meets Internet and depends on Electricity and FixedInternet; a CPAP meets Health and depends on Electricity at 0.2 to 0.7 kWh a night.

Growing food is a Flow provider: an allotment plot or raised beds meet Food at a yield per week by month for the UK (potatoes, brassicas, roots, legumes, salad), with a lead time from planting, needing water (which itself is a need, met by a butt or the mains) and a skill. A full plot feeds a family of four for roughly a third of the year's vegetables; the model says so rather than pretending it is a larder. Preserving is a Skill that lets a harvest become Stock (jars, drying) without Cold storage. Chickens are a Flow for eggs with a feed Stock. This is what makes "months of shortages" answerable without inventing a different engine.

### 5. The household

What the wizard knows today plus: a chest freezer, a wood or multi-fuel stove, a water butt, a garden or allotment and its size, an electric car and whether it can power things, medical devices and medicines (section 7), infants, pets, whether income depends on being online, and what the household already holds (the ticks, which are providers held).

### 6. The simulation

Deterministic, in `Selficient.Domain`, benchmarked, property-tested.

```
Simulate(household, providersHeld, scenario, season, horizonDays):
  stocks   := every Stock provider's capacity
  sources  := every EnergySource's store
  for day in 1 .. horizonDays:
    utilities := scenario.StateOn(day)                         -- up, degraded, off
    harvest   := every Flow provider's output for (day, season) whose lead time has passed and whose upkeep is met
    stocks    += harvest
    refill sources from roof and panels by season and from fuel while Fuel is up
    for need in ladder order (Survive first):
      demand := need.Demand(household, season, day)
      supply := 0
      for provider in providers meeting need, cheapest dependency first (needs nothing > needs stock > needs energy):
        if provider.dependsOn are all up, or an EnergySource can carry its load today
           (energy left ≥ load; ceiling ≥ burst; the source is not reserved):
          take from stock or source; supply += amount
      record need.day := Held if supply ≥ demand, Partial if supply > 0, Lost otherwise, with the provider that carried it and the one that ran dry
  return the per-need, per-day record
```

Energy is allocated in ladder order so a small station keeps the CPAP and the router before it tries the fridge, and the board says "the fridge is lost from day 2 because the station went to the CPAP". The same walk with every provider ticked and the same with each candidate purchase added gives the "what would change it" column for free, and the kit recommendations stop being a flat list: each action states which cells it turns.

**Invariants, as property tests:**
- Adding a provider never turns a Held cell to Lost on any day (monotonic in kit).
- A longer horizon never has an earlier day change (prefix-stable).
- A scenario with every utility up yields Held everywhere for a house with the baseline (no false alarms).
- Solar without a battery and gateway contributes nothing while Electricity is off.
- An EnergySource never supplies more energy than store plus refill, nor a burst above its ceiling.
- Days Ready (the headline) equals the first day on which any Survive need is not Held, in the storm scenario with the ticks the household has, and reproduces v1's figure for every v1 profile (golden tests kept).

**Cost:** ≤ 180 days × ~20 needs × ~40 providers, integer arithmetic in pence and watt-hours, well under a millisecond; benchmarked like the plan.

### 7. Things only this household relies on

The wizard cannot list every medical device or dependency. A household adds its own, in the browser, with a small typed form:

```
Add something you rely on
  What is it?            [ Oxygen concentrator            ]
  Which kind?            ( ) A device that needs power   ( ) A medicine   ( ) A supply   ( ) A person or animal
  Power it uses          [ 300 ] W   for [ 24 ] hours a day      -- devices
  Days of stock at home  [ 28 ]                                    -- medicines and supplies
  Must it stay cold?     [ ] yes                                   -- insulin
  Which rung?            Survive (chosen for you; change if you must)
```

Each becomes a Need of the household's own with a Provider that meets it (the stock, or an electrical load), sent with the plan POST as a bounded, validated list (at most 12, every field range-checked, no free text on the server beyond a 40-character name that is only ever echoed back to the same browser, never logged), kept in IndexedDB with the profile, carried by export and import, and shown on the board as its own row with its own sentence. Photo help can propose them ("this looks like a nebuliser") but never add them.

### 8. The Board

The same screen for every scenario: rows are the needs in ladder order (bottom rung at the bottom, so a household climbs it), columns are the horizons the scenario allows, each cell Held, Partial or Lost, with the carrying provider on tap and the one that failed. Above it: the scenario as a row of cards with a drawn mark and that scenario's Days Ready; a season switch; the source switch (only for scenarios that touch Electricity); the "what you rely on" list. Below it: the plan's actions, each saying which cells it turns and for which scenarios. The timeline view (a bar per need from now to the day it is lost, with the fridge-4-hours and freezer-48-hours marks on the axis) is the same data drawn the Dark Sky way, and is the phone's default because it reads in one thumb-scroll.

```
┌ Scenarios ─────────────────────────────────────────────────────────────────────┐
│ [storm 3.5 d] [water 1 d] [snow 7 d] [two weeks 0.5 d] [months 9 d] [heat 7 d] │
└────────────────────────────────────────────────────────────────────────────────┘
 Months of shortages                                Season ● Winter ○ Summer
 If we had: ● what is in the house ○ raised beds ○ an allotment ○ a store cupboard for 3 months …

                  1 wk   2 wk   1 mo   3 mo   6 mo
 Normal   Income   ▓▓▓▓   ▓▓▓▓   ▓▓▒▒   ░░░░   ░░░░   the business needs the shops it sells to
 House    Mobility ▓▓▓▓   ▓▓▒▒   ░░░░   ░░░░   ░░░░   fuel rationed; the EV holds if the grid does
 Function Cooking  ▓▓▓▓   ▓▓▓▓   ▓▓▓▓   ▓▓▓▓   ▓▓▓▓   the gas hob
 Survive  Food     ▓▓▓▓   ▓▓▓▓   ▓▓▒▒   ▒▒░░   ░░░░   9 days in the cupboard, then rationed shops; beds feed from June
 Survive  Health   ▓▓▓▓   ▓▓▓▓   ▓▓▓▓   ▒▒░░   ░░░░   28 days of the prescription, then the pharmacy's gaps
```

### 9. Why this is robust

- **Closed lists where the engine must reason** (utilities, rungs, provider kinds, horizons) and **open data where the world changes** (scenarios, needs, providers, loads, yields, prices), every record carrying its source and date, validated at startup, so a new technology or a new threat is a JSON entry and its tests.
- **One function.** Days Ready, the board, the timeline, the kit's "what it turns" and the AI's explanations all read the one simulation; nothing computes twice.
- **Honest units.** Litres, meals, warm-room-hours, kilowatt-hours; a yes/no where that is the truth. No scores.
- **Household-owned extension** for the things the wizard cannot know, bounded and private.
- **Deterministic and property-tested**, so a wrong answer is a reproducible bug and the invariants above hold for every household FsCheck can invent.
- **Export schema versioned** (`selficient: 2`) with migration from 1, as the plan store already does for the pin.

### 10. What it costs to build

Four milestones, each gated like the others:

- **M12 Engine v2.** Utilities, scenarios, needs, providers, loads, the simulation, the invariants, the golden tests, the benchmark. Content: scenarios, loads with sources, the first provider set (everything the plan sells today plus the power sources, the stove, the butt, beds and allotment, preserving). Days Ready re-derived and unchanged for v1 profiles.
- **M13 The Board.** The scenario cards, the board and the timeline, the source and season switches, cell sentences, actions saying what they turn; four new wizard questions; "what you rely on"; export v2.
- **M14 The look.** Design direction v2 (the lantern on slate, the strip, the drawn marks), the illustration system, the hero scenes, motion. Built on the research in `docs/design/research/`.
- **M15 Sweep.** Threat model for the new inputs, load and E2E journeys for the board, screenshots at both widths, handover.
