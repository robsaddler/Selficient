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

## Outcomes (adversarial pass, 11 Nov 2026)

- Every case has a passing test; every gated assembly at 100% lines and branches.
- Found by the tests: axe refused the first tally, a definition list with a button inside each group, so it is a plain list; and the "Plan all of these" button's accessible name did not contain its visible words, which fails anyone using voice control, so the name now starts with them.
- Checked hardest and found clean: the cap across three lists, the precedence when lists overlap, and that today's figure is untouched by planning.
- Known and left: a refusal of the plan page's own POST for a bad id re-renders the form without the sentence, because the form has no place for a sentence that belongs to no field. The page never posts ids without script, so only a tampered post meets it, and the fragment answers the same post with the sentence.
