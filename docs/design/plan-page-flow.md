# The plan page: one loop, always in view

*30 October 2026. Written after Rob used the finished page and found himself "bouncing up and down ticking boxes to see what improves". The mechanics are right; the order and the distances are wrong.*

## What is wrong today

The page is two columns. The left is a tall sticky panel: the figure and the strip, then the map, then the "what you rely on" form. The right is the board (cards, pills, sheet, key), then eight risk cards, then the plan's tiers with the ticks, then upkeep, then the tools. The thing a person changes (a tick, two screens down the right) and the thing that answers (the figure, at the top of the left) are never on the screen together, because the sticky panel is taller than the viewport and the figure scrolls away with it. On a phone there is no sticky panel at all: the figure is the first screen and the ticks are the fifteenth.

## The jobs, in the order a worried household has them

1. **How exposed are we?** The verdict: the figure, and what runs out first.
2. **What should we do first, and what does it cost?** The actions, in order, each saying what it changes.
3. **What would actually happen to us?** The board: what holds, what goes, and when, in a scenario of their choosing, with the what-ifs.
4. **What about our particular needs?** The oxygen concentrator, the insulin, the baby.
5. **Why these risks, for this house?** The rationale, for the ones who want it.
6. **Where exactly are we?** The map and the pin.
7. **How do we keep this going?** Upkeep, export, import, clear.

Today's order is 1, 3, 5, 2, 7, with 4 and 6 in the side panel. The redesign puts them in the order above and keeps 1 on the screen throughout.

## The design

### A verdict bar that never leaves

The figure, its unit, the scenario's name and the four strip bars in miniature become a slim bar that sticks to the top of the viewport once the hero has scrolled past, on desktop and on phone alike. It is one line tall on a phone (`1 day · a storm · ▮▮▮▮`) and two on a desktop. Every tick, every what-if and every scenario change updates it in place, so wherever the person is on the page the answer is where their eye already is. The lantern pulses in the bar when the figure moves, as it does in the hero now.

The hero stays as it is at the top of the page: the full figure, the lede, the full strip. The bar is the hero's shadow, not a second hero.

### Scenario as a chooser, not a grid

The seven scenario cards become a row of compact chips under the figure in the hero (and in the bar, as the name you can tap to change). Each chip keeps its pictogram and its figure. They scroll sideways on a phone. Choosing one redraws the page for it, as now.

### One column, in job order

The two-column layout goes. The sticky bar does the job the sticky panel was for, and a single column at reading width puts things in the order they are wanted:

1. **The verdict.** Hero: figure, lede, strip, scenario chips.
2. **Your plan, in order.** The tiers, this week first and open, the rest folded. Each action keeps its mark, its sentence, its price, its "turns" line and its picks. New: each tier's header says what the tier buys, "Tick everything here and the house stands for 4 days", from one simulation per tier with that tier's actions held. The person sees the destination before the list.
3. **What holds, what goes, and when.** The board, with the source and season pills and the key. The sheet is the detail behind the figure, so it comes after the list that changes it, not before.
4. **What you rely on.** The dependency form, directly under the board it adds rows to.
5. **What is most likely to go wrong here.** The risks, as a folded list of eight with the top three open. The rationale for people who want it, out of the way of people who do not.
6. **Where your house stands.** The map, with the pin.
7. **Keep it going.** Upkeep, then export, import, start again and clear.

### Phone

Same column, same order. The sticky bar is the whole point on a phone: tick a tin on screen fifteen and the day count at the top of the screen changes.

### What does not change

The engine, the fragment endpoint, the board's rows and cells, the ticks and the store, the dependency form, the picks, the export format. This is a re-ordering of partials and three new pieces: the bar, the chips and the per-tier outcome.

## The work

- `BoardComposer`: a `TierOutcome` per tier (the figure with the tier's provider actions held). One simulation each, four in all.
- `_PlanSheet.cshtml`: the new order; `_VerdictBar.cshtml`; scenario chips replace the cards in `_Board.cshtml`'s head and move into the hero.
- `board.js`: update the bar as it updates the hero; scroll-aware showing of the bar (an `IntersectionObserver` on the hero, no scroll listener).
- CSS: the bar, the chips, one column at reading width, the folded risks.
- Tests: the bar's figure follows a tick (journey), the tier outcome (composer), the order of sections (page test), axe in both themes.
- Docs: handover step 19 rewritten; this document.

About half a day. Nothing in it is hard to reverse.
