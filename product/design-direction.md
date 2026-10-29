# Selficient — design direction

*29 October 2026. Written before any UI code so every colour and type decision in the app derives from it.*

## The subject, pinned

**Subject:** a UK household working out how long it could stand on its own if the power, water or signal went, and closing the gap.
**Audience:** ordinary families prompted by the government's Be Ready campaign. Mildly anxious, not preppers, short on time, allergic to being lectured.
**The page's single job:** turn worry into a plan with a number on it.

## The feeling

Calm competence in a domestic setting. The storm lantern on the kitchen table, the well-stocked larder, the Ordnance Survey map with the contour your house sits on. British, practical, warm, unhurried. The opposite of camouflage, sirens and countdown clocks.

Words we use: ready, cover, stand on its own, days, household, kit.
Words we never use: stockpile, prepper, survival, bug-out, threat level, apocalypse.

## Signature element: the Days Ready figure

Every household gets one honest number: **how many days the house could run on what is in it right now.** It is computed from real inputs (litres of water over people over three litres, days of no-cook food, hours of light, hours of boiler run-time from stored power, cash) and it moves the moment an item is ticked.

It opens the product ("Your house could stand on its own for **0.5 days**"), it anchors the plan ("Tier 1 takes you to **7 days**"), and it is the thing a person remembers and tells their neighbour. It is large, set in the display face, and it is the only place amber is allowed to shout.

Behind it, a quiet band of contour lines drawn from the household's actual elevation above sea level, so the hero is literally theirs. At 232 m the contours are tight; at 8 m they are wide. Decorative, truthful, unique per house.

## Palette

| Token | Hex | Use |
|---|---|---|
| `--c-slate` | `#1B2A33` | Ink, headings, the plan text. Wet slate roof. |
| `--c-sky` | `#EDF1F2` | Page ground. Sky before weather. Not cream. |
| `--c-pine` | `#1F5F4A` | Primary actions, links, ticked states. Safety-sign green lineage without the neon. |
| `--c-lantern` | `#E8A33D` | The Days Ready figure, progress fill, one accent per screen. |
| `--c-contour` | `#C9D3D2` | Rules, borders, contour lines, disabled. |
| `--c-ember` | `#B3261E` | Genuine warnings only (a flagged tree, a CO rule). Never decorative. |
| `--c-paper` | `#FFFFFF` | Cards and the plan sheet. |

Dark mode: `--c-sky` becomes `#13202A`, paper `#1B2A33`, ink `#E6ECEE`, pine lifts to `#3F9A7A`, lantern stays. Contrast checked to WCAG AA at every pairing.

## Typography

- **Display:** Bricolage Grotesque, variable, used only for the Days Ready figure, page titles and tier headings. Characterful without being quirky; its slightly compressed numerals make the figure feel like an instrument reading.
- **Body:** Atkinson Hyperlegible. Designed by the Braille Institute for low-vision readers. In a product whose users may be reading by torchlight on a phone at 5% battery, the most legible body face there is becomes the brand choice.
- **Data:** IBM Plex Mono for litres, kWh, prices and dates in tables, so figures line up and read as measurements.

All three self-hosted in `wwwroot/fonts` with `font-display: swap`, as the template does. Fallback stack: system-ui, then Arial.

Type scale (fluid): body 1rem/1.55; small 0.875rem; h3 1.25rem; h2 1.6rem; h1 2.2rem; display figure clamp(3.5rem, 12vw, 7rem) with tabular numerals.

## Layout

Mobile first; this is a PWA on a phone in a cupboard.

```
PHONE                                DESKTOP (≥ 960px)
┌──────────────────────┐             ┌──────────────┬───────────────────────────┐
│ Selficient      ⋮    │             │ Selficient                   Install ▾   │
├──────────────────────┤             ├──────────────┼───────────────────────────┤
│ ≈ contour band ≈     │             │ ≈ contour ≈  │                           │
│ Your house could     │             │ 0.5 days     │  THE PLAN                 │
│ stand on its own for │             │ ready        │  ① This week      £300    │
│      0.5 days        │             │              │   ☐ 36 L bottled water …  │
│ ▮▮▮▯▯▯▯▯▯ 3 → 7 → 30 │             │ Your house   │   ☐ Power-failure lights… │
├──────────────────────┤             │ 232 m, very  │  ② This month     £550    │
│ ① THIS WEEK    £300  │             │ low flood    │   …                       │
│ ☐ 36 L water   £15   │             │ risk, storm  │                           │
│ ☐ 4 headtorches £80  │             │ cut likely   │                           │
│ …                    │             │              │                           │
├──────────────────────┤             │ Kit · Solar  │                           │
│ ▮ 2 of 11 done  ▸    │ sticky      └──────────────┴───────────────────────────┘
└──────────────────────┘
```

The numbered tiers are a true sequence (this week, this month, three months, a year), so numbering carries information. Nothing else is numbered.

Cards are the "plan sheet": white, 2px contour border, 12px radius, no shadows. Progress is a flat lantern bar, never a ring.

## Motion

One orchestrated moment: when a plan is generated, the Days Ready figure counts up from 0 over 900 ms and the tiers settle in from below, staggered 80 ms. Ticking an action nudges the figure. Nothing else moves. `prefers-reduced-motion` disables all of it.

## Copy rules

- Sentence case everywhere. Plain verbs. "Save your plan", not "Submit".
- Every action says what it buys: "4 × 25 L water cans — 8 days of drinking water for four, £60."
- Numbers are concrete and domestic: "36 litres is six of the big bottles."
- Errors say what happened and what to do: "We couldn't find that postcode. Check it and try again."
- Empty states invite: "Tick what you already have. Most houses start at about half a day."
- Never apologise, never exclaim, never use "oops".

## The self-critique

Checked against the generic defaults: not cream-and-serif, not black-and-neon, not broadsheet hairlines. The risk taken is the hyperlegible body face, which is unfashionable and slightly wide; it is justified by the domain. Removed before building: a planned map tile behind the hero (noise, and a tracking-free app should not call a tile server) and a "readiness ring", replaced by the flat bar.
