# Threat model: what a household says about each action (11 Nov 2026)

## What it is

Each action on the plan takes one of three choices: the household has it, plans to get it, or it is not for them. An action with none is not decided. The choices live in the browser's IndexedDB, in the store that held the ticks, as `have`, `plan` or `skip` against the action's id; a value of `true` from before reads as `have`. The board script posts them with the profile to `/plan/board` as three lists (`Ticks`, `PlanTo`, `NotForMe`) and the fragment answers with the board, a second Days Ready figure for what is planned, and a tally for the whole plan and for each stretch, which the page copies in. Buttons beside each tally clear one kind or plan everything not decided, and an Undo puts the last such press back. Export and import carry the three lists. Nothing is stored on the server.

## What we protect

- The server: two more lists on an endpoint that simulates, and one more simulation when something is planned.
- The figure: Days Ready for today must not move because of what someone means to buy.
- The page: ids from the store and from an imported file reach selectors, form fields and the tallies.
- The household: a bulk button must not lose a morning's choices to one stray press.

## Abuse and failure cases

| # | Case | Control | Test |
|---|---|---|---|
| 1 | Hundreds of ids in the two new lists to make the post expensive | The three lists together are capped at `IndexModel.MaxTicks` (200), the cap one list had; every id must be a kebab-case action id before the engine sees it; a refusal is our sentence with a 400 | `What_is_planned_and_ruled_out_is_checked_as_ticks_are_and_counted_together` |
| 2 | Markup or a path in an id (`<script>`, `../etc`) | The same `ActionId.IsValid` check as ticks; ids are never echoed into the fragment, only counted | the same test (asserts an unknown id is not in the response) |
| 3 | An id in two or three lists, to count it twice or to plan something already held | `PlanChoices` settles it in the domain: have beats plan beats ruled out, so every action is counted once and the tallies always add up to the plan | `An_action_is_in_one_state_and_having_beats_planning_beats_ruling_out`, `The_tally_counts_each_state_and_sums_only_what_is_still_to_spend`, `The_fragment_carries_the_tallies_and_where_the_plan_takes_the_house` (headtorches posted as held and ruled out) |
| 4 | A planned action moving today's figure, so a household believes it is readier than it is | The engine simulates today from what is held only; the planned figure is a separate run and a separate sentence | `What_is_planned_moves_the_figure_for_the_plan_and_never_the_figure_for_today`, the E2E journey (figure stays at 1) |
| 5 | The extra simulation as a cost | One run, only when something is planned, bounded like every other; the fragment keeps its own rate limit and the script its debounce | `What_is_planned_moves_the_figure_for_the_plan_and_never_the_figure_for_today`; load run recorded in `docs/benchmarks.md` |
| 6 | A tampered IndexedDB: a value that is not a choice, a key that is not an id, a prototype key | `getChoices` builds a null-prototype map from keys that match the id pattern and values that are one of three strings or the old `true`; anything else is left out, and the server re-validates what is posted | E2E journey (reload and export), case 1's test |
| 7 | An imported file with hostile `planned` or `skipped` lists | `isSnapshot` holds both to the rule ticks had: an array of at most 200 kebab-case ids; a file without them (exported before today) still opens | `Export_then_start_again_then_import_brings_the_plan_back`, the E2E journey's export |
| 8 | The fragment's tallies written into the page as markup | The script copies text and one attribute from the fragment's tally onto the page's own elements (`textContent`, `setAttribute`), never markup; the only `innerHTML` is still the server's encoded fragment in its slot | `The_plan_itself_has_no_accessibility_violations`, the journey |
| 9 | One press of "Clear" or "Plan all of these" destroying choices | The press is undoable until the next choice; Undo restores exactly the actions the press changed, to what each was | the E2E journey (plan a stretch and undo; clear a stretch and undo) |
| 10 | A button with nothing to act on, or the fragment's hidden copy of a button, doing something | A button marked `aria-disabled` returns before reading the store; the click handler ignores anything inside the fragment's hidden tallies | the E2E journey (asserts the attribute after the count empties) |
| 11 | A ruled-out safety action (the carbon monoxide alarm) silently dropping out of the advice | Ruling out hides the row's detail and stops the row arguing, but the action stays on the plan, in its place, counted under "Not for me" in the tally, and each stretch's sentence says "except what is not for you" | `What_is_ruled_out_is_not_suggested_again_and_is_left_out_of_where_a_stretch_leads`, `The_copy_helpers_say_whole_sentences` |

## Addendum, the same day: the household's own figures

A household can type how much it has of five stocks (days of food, litres of bottled water, litres in cans, pounds in cash, stove cartridges). The figures live in a fourth IndexedDB store and are posted as `Amounts[stock-id]=n`. A figure sets the stock the engine draws down, and decides which plan actions are held: ten days of food holds the three-day shelf and a third of the month, whatever is ticked.

| # | Case | Control | Test |
|---|---|---|---|
| 12 | A figure for something the plan does not measure, a key with markup in it, a negative, a number in the millions, or words | `MeasuredStocks.Check` refuses an unknown id and anything outside the stock's own range with a sentence and a 400 before the engine is reached; a value that is not a whole number fails binding with a 400; the key is never echoed | `A_figure_for_something_unmeasured_or_out_of_range_is_refused_with_a_sentence`, `Figures_from_a_browser_are_checked_before_anything_uses_them` |
| 13 | A huge figure used to make the simulation long | A stock's size does not change the number of days simulated, which is the scenario's; each figure is capped (a year of food, 2,000 litres, £10,000, 200 cartridges) and there are five stocks | the same tests; load run in `docs/benchmarks.md` |
| 14 | A ticked month of food posted beside a count of three days, to be credited with both | The count is the truth for every action it counts towards: `Possessions` takes the ticked actions of a counted stock out of play and puts the one counted provider in | `Ten_days_of_tins_hold_the_three_day_shelf_and_a_third_of_the_month_whatever_was_ticked`, `Ten_days_of_tins_count_as_ten_days_on_the_board_the_rows_and_the_tally` (the month is posted as ticked) |
| 15 | "What would this action do" stacking a month on a ten-day shelf, promising forty days | Adding an action to a counted stock raises the count to what the action asks and never past it | `Adding_an_action_tops_a_counted_shelf_up_to_what_it_asks_and_never_stacks_another_on_it`, `A_counted_shelf_moves_the_figure_and_costs_only_what_is_missing` |
| 16 | The counted-food provider ticked directly (`Ticks=stored-food`) to get a shelf for nothing | Its own stock is zero; only a checked figure gives it one | `Figures_from_a_browser_are_checked_before_anything_uses_them` |
| 17 | An unnamed dictionary on the page model reading every form field as a figure | The property is bound by name, so a post with no figures binds nothing. Found by the suite: without the name every plan post was refused | every plan page test |
| 18 | A tampered store or an imported file with hostile figures | `isAmounts` keeps at most twenty kebab-case ids with whole numbers from nothing to ten thousand, on read and on import; the server re-checks each against its own stock | the E2E journey (reload, export), case 12's tests |
| 19 | A household over-stating what it has, and the board believing it | It is their figure and their board; the unit says what is being counted and for whom ("days of food for the 4 of you"), the plan's own figure sits beside the box, and nothing typed here leaves the browser except to be simulated | `The_words_around_a_figure_are_whole_and_say_what_is_counted` |

| 20 | The cash rate posted as zero, a negative, a huge number or markup, to divide by nothing or to make £1 count as a year | The rate arrives as text and is parsed as a whole number with no sign; anything that is not 1 to 100 is refused with our sentence and a 400 before the engine is reached, so the framework's own binding message never shows. The store keeps nothing but a whole number in that range | `The_cash_rate_is_the_households_to_change_and_nonsense_is_refused_with_a_sentence`, `A_household_that_says_a_day_costs_twenty_pounds_needs_twice_the_cash_for_the_same_days` |
| 21 | A rate that changes what an action asks for leaving the page saying "held" where the server says not | The fragment carries each counted action's target under the rate in force; the page takes its targets from it and brings the choices into line, writing only when one differs, so the redraw it causes finds nothing more to change | the E2E journey (the same £200 held at five pounds and not at twenty) |
| 22 | The "planned" source asked for with nothing planned, or by id on a household that has none | The source is offered only when something is planned, and a source that is not offered falls back to the house, as an unknown one always has | `Everything_planned_is_a_source_of_its_own_once_something_is_planned`, `Everything_planned_is_a_pill_beside_the_house_and_draws_the_board_the_plan_leads_to` |

## Outcomes (adversarial pass, 11 Nov 2026)

- Every case has a passing test; every gated assembly at 100% lines and branches.
- Found by the tests: axe refused the first tally, a definition list with a button inside each group, so it is a plain list; and the "Plan all of these" button's accessible name did not contain its visible words, which fails anyone using voice control, so the name now starts with them.
- Checked hardest and found clean: the cap across three lists, the precedence when lists overlap, and that today's figure is untouched by planning.
- Known and left: a refusal of the plan page's own POST for a bad id re-renders the form without the sentence, because the form has no place for a sentence that belongs to no field. The page never posts ids without script, so only a tampered post meets it, and the fragment answers the same post with the sentence.
