# Selficient — design direction, version 2

*30 October 2026. Supersedes `product/design-direction.md` for everything visual. Written after the research in `research/` and after Rob opened the first build at desktop width and found a form with no identity. The palette and the type trio survive; what changes is that they are now used.*

## The subject, pinned harder

The night the power goes. A kitchen table with a storm lantern on it, the wind-up radio, the headtorch, a bottle of water and a tin; the Ordnance Survey map on the wall with the contour the house sits on; the larder door open. A British house looking after itself without fuss. Everything on the page should be a thing you could find in that kitchen or on that map.

What it is not: a dashboard, a control room, camouflage, sirens, hazard stripes, a prepper's bunker, a utility company's app.

## The thesis

**A warm light in a dark kitchen, and the number is the flame.** The figure glows lantern-amber out of slate; everything else is quiet paper, slate ink and pine. The research found that no preparedness site anywhere shows progress, that the trustworthy ones own one colour and picture the home rather than the disaster, and that the best "what stays on" explainers show the first thing that falls off. So the two signature elements are the glowing figure and the Board beneath it, and the imagery is the kitchen, the larder and the hill.

## Palette

| Token | Value | Use |
|---|---|---|
| `--c-night` | `#13202A` | The hero ground and the roofline. Deeper than slate so the glow has somewhere to sit. |
| `--c-slate` | `#1B2A33` | Ink in daylight; panels on the night ground. |
| `--c-sky` | `#EDF1F2` | Page ground in daylight. |
| `--c-paper` | `#FFFFFF` | Sheets and cards. |
| `--c-lantern` | `#E8A33D` | The glow, the figure on night, fills, the one accent. Never a warning. |
| `--c-lantern-ink` | `#8F5A12` | Lantern as text in daylight (4.5:1 on sky). |
| `--c-glow` | `oklch(80% 0.16 75 / 0.35)` | The radial light behind the figure. |
| `--c-pine` | `#1F5F4A` light, `#5BA98A` dark | Actions, links, held cells. |
| `--c-contour` | `#C9D3D2` daylight, `#2E4452` night | Rules, the contour lines, lost cells (hatched). |
| `--c-ember` | `#B3261E` | Genuine warnings only. |

Cells on the Board are never colour alone: held is a pine fill, partial is a lantern fill with a diagonal half, lost is contour hatching. A colour-blind reader and a monochrome print read the same board.

## Type

- **Bricolage Grotesque** for the figure, the page titles, the scenario names and the rung labels. The figure is set light (weight 300) at 75% width and the display optical size, so it reads as an instrument needle rather than a poster. Everything else in Bricolage is 600. The font subset widens to weights 300 to 800 and widths 75 to 100 to allow this.
- **Atkinson Hyperlegible Next** for every sentence.
- **IBM Plex Mono** for the micro-labels that make the screen true: `NP12 · WALES · 232 M`, `36 L`, `13.5 kWh`, `day 3`. Small (0.75 rem), letter-spaced, muted, never decorative: every label is a real value from the engine.

Scale: body 1 rem / 1.55; micro 0.75 rem; h3 1.25; h2 clamp(1.75, 2.4); h1 clamp(1.9, 2.6); figure clamp(5, 11) rem.

## The three layers of every screen

From the film designers: **ambient**, **hero**, **sheet**.

1. **Ambient**: the contour band, the glow, the still life. Low contrast, never read, drawn from the household's own data (ring spacing from elevation).
2. **Hero**: one fact at full contrast. The figure. On the Board page, the scenario's figure. On the solar page, the saving.
3. **Sheet**: the paper where the work is done. The Board, the tiers, the forms. Plain, legible, generous.

## Layout

### Landing

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ ◎ Selficient                                    Your plan  Scenarios  Solar │  roofline
├──────────────────────────────────────────────────────────────────────────────┤
│ ░░░░ contour rings, night, ambient ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │
│                                                                              │
│  NP12 · WALES · 232 M · 4 PEOPLE              ┌────────────────────────┐    │
│  Your house could stand on its own for        │    the kitchen table   │    │
│                                               │  lantern (glowing),    │    │
│    0.5 days   ← glow behind                   │  radio, torch, bottle, │    │
│                                               │  tins, map on the wall │    │
│  Water   ▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬ 3 d             └────────────────────────┘    │
│  Food    ▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬ 6 d                                    │
│  Warmth  ▬▬▬ 0.5 d  ← the shortest bar is the figure                        │
│  Light   ▬▬▬▬▬▬▬▬▬▬▬▬ 2 d        ┆3     ┆7            ┆30                   │
│                                                                              │
│  [ Postcode ______ ] [ Start your plan ]                                     │
└──────────────────────────────────────────────────────────────────────────────┘
│ What could happen here           ◐ storm 3 d  ◑ water 1 d  ❄ snow 7 d  … │  scenario cards
│ (the pitch, how it works, the larder shelf, questions, closing band)         │
```

The hero bleeds to the viewport edges on the night ground. The strip is Dark Sky's form: bars from now, ending where each runs out, with the 3, 7 and 30 ticks. On a phone the still life sits under the strip at half height.

### The plan and the Board

```
┌──────────────┬───────────────────────────────────────────────────────────────┐
│ night panel  │ SCENARIOS  [◐ storm 3.5 d] [◑ water 1 d] [❄ snow 7 d] [▦ months 9 d] │
│ NP12 · 232 M │                                                               │
│ Your house   │ A storm takes the power out        ● Winter ○ Summer   │
│ could stand  │ If we had  ● in the house  ○ power banks  ○ 1 kWh  ○ 3 kWh   │
│ on its own   │            ○ home battery  ○ the car      ○ a generator       │
│  3.5 days    │                                                               │
│ (glow)       │                  1 day   3 days  1 week  2 weeks  1 month     │
│              │ Normal  Working  ▓▓▓▓    ░░░░    ░░░░    ░░░░    ░░░░         │
│ strip        │ House   Fridge   ▓▓▓▓    ▓▓▒▒    ░░░░    ░░░░    ░░░░  ← tap  │
│              │         Boiler   ▓▓▓▓    ▓▓▓▓    ░░░░    ░░░░    ░░░░         │
│ map card     │ Function Light   ▓▓▓▓    ▓▓▓▓    ▓▓▓▓    ▓▓▓▓    ▓▓▒▒         │
│              │ Survive Water    ▓▓▓▓    ▓▓▓▓    ▓▓▒▒    ░░░░    ░░░░         │
│ what you     │         Warmth   ▓▓▓▓    ▓▓▓▓    ▓▓▒▒    ░░░░    ░░░░         │
│ rely on  +   │                                                               │
│              │ Your plan, in order   (tiers as shelves; each action says      │
│              │  which cells it turns: "turns Fridge at 3 days")              │
└──────────────┴───────────────────────────────────────────────────────────────┘
```

Left is the night panel: the figure with its glow, the strip, the map, and "what you rely on" with its add button. Right is the sheet: the scenario cards, the switches, the Board, then the tiers. On a phone the panel comes first, then the scenario cards scroll sideways, then the Board as the timeline (a bar per need), then the tiers.

### Forms

Intro beside the form, two-column fieldsets, as built in the M11 pass; the new questions (freezer, stove, medical device, car) join their sections. The "what you rely on" form is a sheet with one row per thing.

## Imagery

The site has had none. It gets an illustration system in two tones of slate plus lantern, drawn as SVG so it weighs nothing and recolours for the theme:

- **Marks** (icon size, 24 to 48 px): one per need (bottle, tin and oats, blanket-and-flame, cross, lantern beam, radio with handle, pan on a ring, tap, fridge with snowflake, radiator, shower, router, laptop, washing machine, television, car) and one per source (power bank, power station with handle, wall battery with gateway, roof with panels, car with plug, generator with jerrycan) and one per scenario (storm cloud over a pylon, a dripping tap, a snowflake over a road, a calendar fortnight, a ration-book grid for months, a sun, a house with a closed door).
- **Still lifes** (hero size): the kitchen table (lantern lit, radio, torch, bottle, tins, the map behind); the larder shelf (jars and tins on a plank, labelled with per-person numbers); the cupboard under the stairs (the power station, the water cans, the box).
- **The hill**: the house on its contour rings, dusk, the lantern window lit, a front coming over the ridge. The contour band is the ambient version of it on every page.

Style rules: geometric, flat, 2 px strokes in contour on night and slate on paper, lantern used once per picture as the light source, no faces, no gradients except the glow.

## Motion

One orchestrated moment on the plan: the glow brightens from nothing behind the figure over 600 ms, the figure counts up over 900 ms, the strip's bars draw left to right in sequence, the scenario cards settle in staggered 80 ms. Ticking an action pulses the lantern once and redraws the affected cells. Switching a source redraws the Board in place. Scroll-driven: as the tiers arrive, the cells they turn light up (`animation-timeline: scroll()`, no frame sequences). `prefers-reduced-motion` turns all of it off.

## Copy

Unchanged from v1, with one addition: every cell on the Board has a sentence that names the provider that holds it and the one that fails, and every action names the cells it turns. Numbers stay domestic ("36 litres is six of the big bottles"; "a day and a half, then the freezer").

## The self-critique

Against the three generic defaults: not cream-and-serif (night slate and a grotesque), not black-and-neon (the accent is a warm lantern, used as light, on a palette with paper and pine), not broadsheet hairlines (2 px rules, round-cornered sheets). The risk taken is the night hero with a glow, which could tip into "dashboard"; the still life, the Atkinson body and the plain sentences hold it in the kitchen. Removed before building: a ring gauge (hides the limiting resource), photography (competes with the data, as BBC Weather learned), and any hazard stripes or triangles.

## Built (M14, 30 Oct 2026)

Everything above is on the page except two things. The scroll-driven cells ("as the tiers arrive, the cells they turn light up") are not built: the actions name the cells they turn in words instead, which reads on a phone and needs no scroll position. The cupboard-under-the-stairs still life is not drawn; the kitchen table, the larder shelf and the hill are. The landing hero is the engine's board for a typical house with nothing put by, which came out at a day rather than the half a day the copy guessed, so the copy now says what the engine says. One more change from the wireframe: the source and season switches redraw the sheet by posting for it rather than showing one of twelve pre-drawn sheets, because twelve sheets were 342 KB of HTML on every plan request and the redraw is a few milliseconds. The marks live in `src/Selficient.Web/wwwroot/img/marks.svg`, the still lifes in `Pages/Shared/_StillLifeKitchen.cshtml`, `_Larder.cshtml` and `_Hill.cshtml`, the strip in `_Strip.cshtml` with its maths in `Pages/Plan/Strip.cs`.

### Second pass, the same evening

Rob put the demo page beside the build and found the build thinner: outlines where the demo had filled shapes, no pictogram on the scenario cards, nothing beside the actions. Fixed: the kitchen still life is redrawn with filled panels, heavy strokes, the pale glass and the flame; each scenario card carries its pictogram; every plan action carries a mark (the source it is, else the first need its provider meets, else the list). The scenario cards also now follow the theme (paper in daylight, slate in the dark) rather than staying night, because seven night cards beside the night panel made the light theme read as half-flipped. The night hero, the plan's night panel, the roofline and the closing band stay night in both themes: they are the thesis, a warm light in a dark kitchen, and the demo had them so too.

### Third pass: daylight is daylight

Rob, on the light theme: "Doesn't change most of the homepage." He was right. The night hero, the plan's night panel and the closing band stayed night in both themes, which this document asked for, and on a light page it read as a theme that had not finished flipping. Changed: every surface now follows the theme. In daylight the hero and the panel are paper with the contour rings and a deep-amber figure, the kitchen table is drawn in slate, and the closing band is a bordered paper card; in the dark they are the night this document drew, glow and all. The roofline header and footer stay slate in both, as a frame. The thesis survives where it was meant to live, which is the dark theme, and the light theme is a whole light theme.

### Fourth pass: the roofline is a dark-theme thing

Rob asked whether the header and footer staying slate in daylight was a bug or a choice. It was a choice, the last one left from this document, and once the hero was paper it had lost its argument: a dark bar top and bottom of a light page. Both now follow the theme, paper with a slate rule in daylight and the roofline in the dark. The light theme has no dark surface left on it.

### The photos leave the landing page

Rob asked what the AI photos bring. Honestly: the boiler plate reading answers a question nobody can answer themselves, the consumer unit answers a niche one, the cupboard count saves a minute and could move the figure if it were wrong (it cannot: nothing ticks until a person presses), and the narrative repeats the plan's own sentences. The differentiator is the engine and the board, so "Three photos" is no longer one of the six things the landing page says it covers. The photos page stays as a tool reached from the plan.
