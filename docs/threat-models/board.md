# Threat model: the board and what the household relies on (M13)

## What it is

The plan page carries the board: the chosen scenario, every source the house could add, both seasons, and a cell per need and horizon. A tick, a scenario card or a new dependency posts the whole profile, the ticks and the chosen scenario to `/plan/board`, which answers with the fragment and the page swaps it in; without script the board on the plan page itself is the one that counts. The household can add up to twelve things it relies on (a device that needs power, a medicine that must stay cold, a supply it cannot run out of), each with a name and figures, kept in the browser's IndexedDB and re-posted with the profile. Nothing is stored on the server.

## What we protect

- The server: a fragment endpoint is a cheap way to make the server simulate, so it must cost no more than the plan POST and refuse early.
- The page: a dependency's name is free text written by the household and rendered in the board, in the rely list and in hidden form fields.
- The figure: Days Ready now comes from the simulation; a tampered post must not produce a wrong-looking success, only a sentence.

## Abuse and failure cases

| # | Case | Control | Test |
|---|---|---|---|
| 1 | A dependency name carries markup or script into the board, the rely list or the hidden echo fields | Razor encodes every rendering; names are bounded to 40 characters and trimmed in the domain (`ResilienceProfile.MaxNameLength`); the browser renders names with `textContent`; the only `innerHTML` write is the server's own encoded fragment | `A_dependency_gets_its_own_row_and_decides_the_headline`, the copy-rules test on every public page |
| 2 | Hundreds of dependencies or hundreds of ticks posted to make the simulation expensive | `Ticks` capped at `IndexModel.MaxTicks` (200) and every tick must be a kebab-case action id before the engine sees it; dependencies capped at `ResilienceProfile.MaxPerHousehold` (12) with the thirteenth refused by a sentence; blank rows are dropped before counting | `Ticks_are_checked_before_they_reach_the_engine`, `The_full_profile_is_validated` (Domain) |
| 3 | Nonsense figures: a device drawing zero or a megawatt, a medicine with a negative stock, a floor of 900 | Every figure is range-checked in the domain factory (`Dependency.Create`, `ResilienceProfile.Create`) and the form's `[Range]` attributes say the same thing earlier; failures are our own sentences with a 400, never a stack trace | `A_bad_answer_in_the_newer_questions_comes_back_as_a_sentence`, `The_form_maps_the_newer_answers_with_their_defaults` |
| 4 | The fragment endpoint used without the antiforgery token, or by GET to scrape | Razor Pages antiforgery applies to the fragment POST; the script reads the token from the page's own form; a GET is redirected to the plan | `The_fragment_refuses_what_the_plan_page_would_and_redirects_a_get` |
| 5 | The fragment endpoint hammered to make the server simulate for free | Its own fixed-window bucket (`RateLimits.BoardPolicy`, 120 a minute per address, so a household ticking its way down the plan never spends the plan POST's 30); the browser debounces redraws at 350 ms | `Each_policy_permits_its_configured_number_in_a_window`, `The_board_has_its_own_rate_limit` |
| 6 | A scenario id or season from the post that is not in the catalogue | An unknown scenario falls back to the most likely one for the house; the season is a closed enum and a bad value is a model-binding sentence | `An_unknown_scenario_falls_back_to_the_storm` (Application) |
| 7 | Framework binding messages leaking as fragments ("The value '' is invalid.") | Every new value-type field carries its own sentence, including the three new booleans whose checkboxes carry an implicit required | `A_bad_answer_in_the_newer_questions_comes_back_as_a_sentence` |
| 8 | A tampered IndexedDB (dependency rows with a hostile kind or a prototype key) re-posted to the server | The store validates every row on read (`isDependency`) and the server re-validates every field through the same value objects; nothing from the store is trusted | `Something_the_household_relies_on_gets_a_row_and_survives_a_reload` (E2E), `Tampered_profile_values_are_rejected_with_sentences` |
| 9 | The fragment cached by a shared cache and shown to another household | `Cache-Control: no-store` on the fragment as on the plan page | `The_fragment_refuses_what_the_plan_page_would_and_redirects_a_get` (asserts `no-store`) |
| 10 | Personal data: a dependency name such as "Mum's insulin" reaching the server log | The handler logs nothing about the post; request logging carries no body; names live in the browser and are posted only to be rendered | `The_full_postcode_is_never_logged` pattern (no body logging anywhere) |
| 11 | A scenario that gives dangerous advice by omission: a household told its stored water will flush the toilet in a national outage, when the official advice is not to flush at all (added 11 Nov 2026) | The sewer rule is the engine's, not the copy's: in a scenario where the sewers stop with the power, no provider that needs a sewer meets sanitation while the power is short of steady, whatever is held. The scenario sentence, the plan action and the provider sentence all say "do not flush" in whole sentences, and each cites the Chief Medical Officers' scripts | `When_the_sewers_stop_with_the_power_stored_water_cannot_flush_and_only_a_bag_will_do`, `The_baseline_sizes_water_light_and_banks_to_the_household` (pins the wording) |
| 12 | The new scenario adds a simulation to every plan page and an id to the share route | The scenario is a constant in the engine like the other seven; the share route and the fragment take only ids the engine knows; one more card is one more bounded simulation of seven days | `Scenarios_are_distinct_bounded_and_ordered`, the existing unknown-scenario tests |

## Outcomes (adversarial pass, 30 Oct 2026)

- Every case has a passing test. Web suite at 178, every gated assembly at 100% lines and branches.
- Found by the tests: the new checkboxes leaked the framework's "field is required." fragment until each carried its own sentence; a blank dependency row with a null name threw before the "blank row" check ran; the fragment's echo fields were posted without the `Form.` prefix and bound to nothing, so every redraw was a 400 until the prefix was added.
- Checked hardest and found clean: XSS through names (encoded in Razor, `textContent` in script, the copy-rules test runs on the built plan), cost of the fragment (same limits as the plan, capped ticks and rows, one simulation per source and season plus one per untaken action), and the store (rows validated on read, server re-validates everything).
- Changed in the M15 sweep: the fragment first shared the plan POST's bucket of 30 a minute, which a keen household ticking twenty actions and switching scenarios would have spent, leaving the board silently stale. It now has its own bucket of 120 a minute (`RateLimits:BoardPerMinute`).

## M14 addendum: the look

The look adds no input. The sprite and the still lifes are static files and partials with no household data in them; the strip is drawn from the same board as the cells and carries the same bounded figures; the landing page's figure is simulated once at startup for a fixed household and cached in a static property, so no request can make it expensive. Checked: the sprite carries no `<style>` or script (the CSP would block the first and the second has no place in an SVG we serve), `<use>` is same-origin, and the copy-rules test still runs on the landing page with the new sentences in the pictures' titles.
