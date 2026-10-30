# Visual references: cinematic yet usable

*Researcher's report, 30 October 2026.*

## What the FUI people and award sites actually do

**Territory Studio** (The Martian, Blade Runner 2049): identity comes from one visual language agreed first, then a modular kit. Screens split into ambient (atmosphere, low contrast, never read) and hero (one narrative fact, legible at a glance). Ridley Scott's note: don't reference other sci-fi; reference art, choreography and marine life. For us: reference the larder and the OS map, not dashboards. Reproducible as a two-tier contrast system in custom properties.

**Jayse Hansen** (Iron Man, Avengers): "glance at stuff and absorb the data in an instant", pattern over digits, everything in focus, micro-text true to the scene, a deliberate budget for how much is on screen. Reproducible: real micro-labels (litres, metres above sea level, the postcode district) in Plex Mono at 0.7 rem, one hero number, nothing fake.

**Perception**: a strict three-tier hierarchy, critical largest and highest contrast, secondary smaller, tertiary at the periphery. Already our figure / plan / kit split.

**Linear**: near-black #08090a, paper-white type, one acid accent "as a flashlight", 0.5 px hairlines, hierarchy from luminance not size. Cheap to reproduce and cold; the thing to avoid.

**Awwwards data-viz winners** are dark, mono-labelled, hairline-ruled. **Information is Beautiful** near-neighbours: the Canadian Climate Institute's Heat Pump Calculator (one big computed figure that moves as inputs change) and the United States of Rising Hazards (a hazard map that lights up by place).

## Weather and maps with identity

**Dark Sky**: the best model. One unified timeline starting at now; rain as categorical bands rather than false-precision millimetres; "temperature pills" on a shared scale so the eye compares lengths, not digits; the screen re-prioritises itself. Reproducible in CSS grid and SVG in a hundred lines.

**Met Office warnings**: yellow, amber, red with fixed national meaning; an impact-by-likelihood matrix; cards "what to expect / what should I do". Reuse the structure, never the triangle, and never turn our lantern amber into a warning colour: it is a glow, not an alert.

**Windy / Ventusky**: weather as flow; strong identity, poor glanceability. One idea only: slow continuous ambient motion reads as "live".

**Ordnance Survey**: the "O" as a window onto a real map of a real place; patterns from map-key marks. Truthful decoration: our contour band from real elevation is the same move.

**The Shipping Forecast**: identity from a fixed ritual sequence of named areas in a calm palette. Our tiers are a fixed sequence too.

**BBC Weather 2018**: photographic scenes read well but lost legibility; a warning against mood imagery competing with data.

## Identity built from the subject's own world

Field Notes (one typeface, one material, one story: our material is the plan sheet). Finisterre (palette from the Cornish coast; warmth from imagery, not saturation). National Trust (a typeface cut from a 1748 inscription). The Economist's Graphic Detail (red for primary data only, off-white #F5F4F0, horizontal gridlines only, headline as chart title: "authority is the absence of noise"). The Pudding (pinned graphic right, text steps left, IntersectionObserver). Apple (scroll-scrubbed frame sequences: beautiful, 18 MB, hostile to a phone at 5% battery; take only the idea with `animation-timeline: scroll()` on an SVG). Stripe Press (tactility of the physical object; ours is the lantern and the shelf).

## The type trio in the wild

Bricolage Grotesque (Mathieu Triay; collage of French and British grotesques; variable weight, width and optical size) is used for headlines and figures, never body; its 75% width at display sizes makes a figure read as an instrument. Atkinson Hyperlegible is in the Cooper Hewitt collection; low fashion value, high meaning. IBM Plex Mono's slab details make it warmer than other monos. Nobody notable pairs all three.

## Three directions

**A. Lantern on the table.** A warm light in a dark kitchen; the number is the flame. Slate #1B2A33 hero, paper sheet, lantern #E8A33D as fill with an ink variant for text, pine, contour. Bricolage light weight at 75% width for the figure; Plex Mono micro-labels. The Dark Sky strip: one horizontal timeline from now, a bar per resource ending where it runs out, tier marks at 3, 7 and 30; the shortest bar is Days Ready, visibly. Motion: the glow brightens behind the figure, the figure counts up, the tier marks light in sequence; a tick makes the lantern pulse once.

**B. The forecast sheet.** The household read like the Shipping Forecast: off-white, ink, sea blue, amber only as a tab; bars drawing once like a pen; a stacked larder of shelves. Beautiful, could be any broadsheet's explainer.

**C. The contour that lights up.** Readiness as terrain: concentric rings filling from the house outward as tiers arrive, scroll-driven. The most cinematic hero; but a ring hides which resource is limiting, and the direction vetoed rings and map tiles for sound reasons.

**Pick: A**, stealing C's scroll-driven fill for the plan page. It is the only one instantly legible as "how long will the house last" and unmistakably ours; everything in it is custom properties, one SVG, one reduced-motion-guarded count-up and the three fonts already subset.
