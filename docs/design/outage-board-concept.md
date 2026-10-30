# The Outage Board: a readiness model with the detail in it

*30 October 2026. Concept, before code. Companion to `product/design-direction.md` (to be superseded by v2) and `plans/build-plan-v1.md`.*

## The problem with one number

Days Ready answers "how long could the house stand on its own" with the weakest of water, food and light. It is honest and it is memorable, and it hides exactly what a worried household wants to know next:

- If the power goes for three days, does the freezer survive? Does the boiler run?
- Would a power bank, a portable power station, a home battery, the car, or a generator actually keep the things we care about going, and for how long?
- What is the difference between a day, three days, a week, a fortnight and a month?
- What goes first, and what would we miss most?

The answer is not one number but a ladder of needs, each either held or lost at each length of outage, under each thing you might buy. That is what the Outage Board shows.

## The ladder of needs (Maslow, applied to a house)

Bottom rung first. A household climbs it; an outage knocks it down from the top.

| Rung | Need | What "held" means | What meets it |
|---|---|---|---|
| 1 Survive | **Water to drink** | 3 litres a person a day | Bottled water, filled cans, bath liner plus purification tablets or a filter, a water butt for flushing |
| 1 Survive | **Warmth** | One room kept warm, everyone sleeping warm | Blankets and sleeping bags (always), a gas boiler *with* electricity for its pump and controls, a wood or multi-fuel stove, a heat pump with a very large source, an electric heater with a very large source |
| 1 Survive | **Food that needs no cooking** | Three meals a day a person | Tins, oats, nuts, biscuits, dried fruit; the larder tier of the plan |
| 1 Survive | **Health** | Medication kept cold, medical devices running, alarms working | A fridge with power for insulin, a CPAP or oxygen concentrator with power, carbon monoxide alarms, the first-aid kit, cash |
| 2 Function | **Light** | Every room and stair lit after dark | Headtorches, lanterns, plug-in power-failure lights, LED lights on a source |
| 2 Function | **Information and contact** | Phones charged, a radio, a way to call for help | Power banks, a wind-up radio, the router on a source (while the exchange and mast have power), satellite internet |
| 2 Function | **Cooking and hot drinks** | Hot food and a kettle | A gas hob with a lighter, a camping stove outdoors, a microwave or kettle on a source that can take the burst |
| 2 Function | **Sanitation** | Flushing, hand washing | Stored water for flushing, a water butt, wipes and sanitiser; hot water from the boiler needs power |
| 3 Keep the house running | **The fridge and freezer** | Food kept, nothing thrown away | A closed fridge for 4 hours and a closed freezer for 24 to 48 hours on their own (FSA); beyond that, a source |
| 3 Keep the house running | **Heating system** | The boiler or heat pump running as normal | A source with the energy for the system you have: a gas boiler is cheap to run on battery, a heat pump is not |
| 3 Keep the house running | **Hot water** | Showers and washing up | A combi boiler on a source, an immersion heater only on a very large source |
| 3 Keep the house running | **Internet** | The router and anything working from home | Router on a source, and the exchange still up |
| 4 Carry on as normal | **Working from home** | Laptop, router, lights, a warm room | Everything in rung 3 held |
| 4 Carry on as normal | **Laundry and cleaning** | The washing machine and the hoover | A large source |
| 4 Carry on as normal | **Entertainment** | TV, games, music | A medium source |
| 4 Carry on as normal | **The car** | Charged and ready to leave | An EV is a source in a cut, not a load; it leaves when you leave |

The ladder is the household's, not a fixed list: a house with a gas hob has cooking without power; a house with a heat pump has warmth only with a very large source; a house with a CPAP has a rung-1 need the neighbours do not. The rules engine already knows these facts from the profile.

## The horizons

| Horizon | What it usually is |
|---|---|
| **1 day** | A storm night, a local fault |
| **3 days** | The government's ask; a winter storm cutting overhead lines across a region |
| **1 week** | A named storm's long tail in rural areas; a burst main under a town |
| **2 weeks** | A severe regional event; a flood that takes the substation |
| **1 month** | The planning case for national resilience; fuel or cyber disruption |

## The sources (the "what if I bought" axis)

Every source has an energy store (kWh usable), a ceiling on power at once (kW), a way of refilling (or none), and a sentence about where it can be used.

| Source | Stores | Power at once | Refills in a cut | Notes |
|---|---|---|---|---|
| **What is in the house** | Phone batteries, torch cells | nil | no | The baseline everyone has |
| **Power banks** (two of about 25,000 mAh) | 0.18 kWh | 0.02 kW | no, unless a folding panel on a bright day | Phones, headtorches, the radio; nothing with a plug |
| **Small portable power station** (about 1 kWh, 1.8 kW) | 1 kWh | 1.8 kW | a 200 W folding panel, roughly 0.3 kWh a winter day, 1 kWh a summer day | Router, lights, phone charging, CPAP; the fridge for a day; kettle one cup at a time |
| **Large portable power station** (about 3 kWh, 3.6 kW) | 3 kWh | 3.6 kW | 400 W of panels, roughly 0.6 kWh a winter day, 2 kWh a summer day | Fridge, freezer, boiler pump and controls, router and lights for about two days; the kettle and microwave |
| **Home battery with a backup gateway** (13.5 kWh, 5 kW) | 13.5 kWh | 5 kW | the roof: about 3 kWh a December day and 20 a June day on 5 kWp | The house as normal except heavy heat; indefinitely in summer, several days in winter |
| **Solar alone** | nil | nil | - | The inverter must shut down in a cut by law. Nothing, until there is a battery with a gateway |
| **The car** (60 kWh EV with vehicle-to-load at 3 kW, or vehicle-to-home at 7 kW) | 60 kWh, keeping 20 to drive | 3 kW or 7 kW | no, unless the solar has a gateway | A fortnight of essentials; a few days of a heat pump; leaves when you do |
| **Petrol generator** (3 kW) | 20 litres of fuel is about 50 kWh | 3 kW | fuel, while the forecourts pump | Outdoors only, carbon monoxide, noise, stored fuel |

Two honest facts the board must show: solar without a battery and gateway gives nothing in a cut, and the mobile network itself fails in the first hours of a regional cut, so a charged phone is for the radio and for calling when a mast is up, not for streaming.

## The loads

Daily energy and the burst each needs. Sized to the house from the profile (people, heating, hob, freezer, medical device, works from home).

| Load | Energy a day | Burst | Season |
|---|---|---|---|
| Phones and torches charging | 0.05 kWh | 20 W | - |
| Router | 0.3 kWh | 15 W | - |
| Lights, LED, the rooms in use | 0.2 kWh | 60 W | more in winter |
| Fridge-freezer | 1.0 kWh | 600 W at start | - |
| Chest freezer | 0.7 kWh | 500 W at start | - |
| Gas boiler, pump and controls | 1.2 kWh in winter, 0.3 in summer | 150 W | yes |
| Heat pump | 25 kWh in a cold snap, 3 in summer | 3 kW | yes |
| Electric heater, one room | 16 kWh if run all evening and night | 2 kW | yes |
| CPAP | 0.3 kWh a night | 60 W | - |
| Kettle, four boils | 0.4 kWh | 3 kW | - |
| Microwave, two uses | 0.2 kWh | 1.2 kW | - |
| Induction hob, one meal | 1 kWh | 3 kW | - |
| Combi boiler hot water | included in boiler | - | - |
| Immersion heater, one tank | 3 kWh | 3 kW | - |
| Laptop and a screen | 0.4 kWh | 80 W | - |
| TV | 0.3 kWh | 100 W | - |
| Washing machine, one wash | 1 kWh | 2 kW | - |

## How the board is computed (the rules engine owns it)

For a household, a season (the user flips winter and summer; winter is the default from October to March), a source and a horizon:

1. Take the loads the household has, in ladder order (rung 1 first, then 2, 3, 4), each with its daily energy and burst.
2. Walk the ladder. A need is **held** if the source's remaining energy over the horizon covers the load's energy for that many days (store plus refill times days) and the source's ceiling covers the load's burst. It is **held for a while** if the store covers it for at least one day but not the whole horizon; the board says for how many days. Otherwise it is **lost**.
3. Needs that do not depend on electricity (bottled water, no-cook food, blankets, a gas hob, a wood stove, a wind-up radio) are decided by the plan's ticks exactly as Days Ready is today, and shown on the same board.
4. The whole thing is a pure function in `Selficient.Domain`, benchmarked and property-tested like the rest of the engine. AI never computes it.

Days Ready stays as the headline. It becomes the bottom row of the board: the first horizon at which a rung-1 need is lost.

## What it looks like

```
THE OUTAGE BOARD                            Season: ● Winter ○ Summer

If we had…  ○ what is in the house   ○ power banks   ● a 1 kWh power station
            ○ a 3 kWh power station  ○ a home battery and gateway  ○ the car  ○ a generator

                       1 day    3 days   1 week   2 weeks  1 month
 Carry on as normal
   Working from home    ▓▓▓▓     ░░░░     ░░░░     ░░░░     ░░░░      laptop and router, one day
   Entertainment        ▓▓▓▓     ░░░░     ░░░░     ░░░░     ░░░░
 Keep the house running
   Fridge and freezer   ▓▓▓▓     ▓▓░░     ░░░░     ░░░░     ░░░░      a day and a half, then the freezer
   Boiler running       ▓▓▓▓     ▓▓▓▓     ░░░░     ░░░░     ░░░░      three days of pump and controls
   Internet             ▓▓▓▓     ▓▓▓▓     ▓▓░░     ░░░░     ░░░░      while the exchange is up
 Function
   Light                ▓▓▓▓     ▓▓▓▓     ▓▓▓▓     ▓▓▓▓     ▓▓░░      headtorches, then the station
   Phones and radio     ▓▓▓▓     ▓▓▓▓     ▓▓▓▓     ▓▓▓▓     ▓▓▓▓      the wind-up radio always
   Cooking              ▓▓▓▓     ▓▓▓▓     ▓▓▓▓     ▓▓▓▓     ▓▓▓▓      gas hob with a lighter
 Survive
   Water                ▓▓▓▓     ▓▓▓▓     ▓▓░░     ░░░░     ░░░░      36 litres is three days for four
   Warmth               ▓▓▓▓     ▓▓▓▓     ▓▓░░     ░░░░     ░░░░      the boiler, then blankets
   Food                 ▓▓▓▓     ▓▓▓▓     ▓▓▓▓     ▓▓░░     ░░░░
   Health               ▓▓▓▓     ▓▓▓▓     ▓▓▓▓     ▓▓▓▓     ▓▓▓▓

 ▓ held   ▒ held for part of it   ░ lost
```

Switching the source redraws the board in place (every variant is rendered by the server and switched with the radio, so it works without script and is instant with it). Tapping a cell opens the sentence: what holds it, what fails, what one purchase would change. Under the board, the plan's actions are the things that turn cells from lost to held, and each says which cells it turns.

The board is the second screen of the plan, after the figure, and it is the thing a household screenshots and sends to the group chat.

## Inputs we need that the wizard does not ask yet

- Is there a chest freezer? (rung 3)
- Does anyone use a medical device that needs power overnight? (rung 1)
- Is the car electric, and can it power things (vehicle-to-load)? (source)
- Is there a wood or multi-fuel stove? (warmth without power)
- Is the hob gas or electric? (already asked)
- What heats the house? (already asked)

Four new questions, each one line.

## What this changes in the build

- Domain: `Outage/` with the ladder, the sources, the loads, the season, and `OutageBoard.Compute(household, ticks, source, season)`. Property tests: more energy never loses a rung; a longer horizon never gains one; solar alone equals nothing in the house.
- Application: the plan handler returns the board for every source.
- Web: `_OutageBoard.cshtml`, server-rendered per source, radios to switch, a details sentence per cell; four new wizard questions; the kit catalogue gains the power station, battery and vehicle-to-load entries with prices and dates.
- Design: the board is the second signature element after the figure, and the illustration system (below) draws every rung and every source.

## Illustration system

The site has no imagery and reads as a form. Every rung and every source gets a drawn mark in the house palette, used at icon size on the board and at hero size on the pages:

- Rungs: a water bottle, a blanket and a flame, a stacked tin and an oat packet, a cross and a pill, a lantern beam, a radio with a handle, a pan on a ring, a tap, a fridge with a snowflake, a radiator, a shower, a router with waves, a laptop, a washing machine, a television, a car.
- Sources: a power bank, a power station with a handle, a wall battery with a gateway, a roof with panels, a car with a plug, a generator with a jerrycan.
- Scenes: the hero scene (a house on its contour hill at dusk, lantern lit in the window, a front coming over the hill), the larder (shelves), the kitchen table (lantern, radio, torch, tins), the cupboard under the stairs.

All SVG, drawn once, two tones of slate plus lantern, so they read in both themes and weigh nothing.
