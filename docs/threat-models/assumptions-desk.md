# Threat model: the assumptions desk

*13 November 2026. Rob asked for every algorithmic figure the system uses to be configurable from the back office, so that he can hand the product to an expert who says "a stairlift's charger draws X, not Y", and for a household to be able to give its own figure where it knows better. It is built in six steps; this document grows with them. Step 1, the registry and the engine reading from it, is done.*

## What it is

- **A registry** (`ParameterRegistry`) of every figure the simulation works from: what people need in a day, what each provider holds, draws, gives and stores, and what each scenario does to each utility and when. Each has an id, a name in words, a unit, the shipped value and its source, a range, where a household meets it, whether a household may give its own, and whether the browser's script needs it.
- **The figures are read off the engine's own tables.** A provider's stock is a parameter because the provider has a stock; nobody keeps a second list. The shipped values stay in code.
- **A `ParameterSet`**: the shipped figures, with a published set of corrections over them, and perhaps one household's own over those. The simulation is handed a set on the household's profile (`ResilienceProfile.Figures`) and reads every figure from it, including the scenario, whichever copy of the scenario the caller held.
- **Still to come:** sentences built from the figures (step 2); storage, draft, preview, publish and rollback, with only Rob able to publish (3); the published file the browser keeps (4); the expert's screen (5); a household's own figures on the plan page (6).

## Cases

| # | Threat | Answer | Pinned by |
|---|---|---|---|
| 1 | **The list and the engine disagree.** A figure is on the desk and changing it changes nothing, or a figure the board uses is not on the desk | The definitions are generated from the provider and scenario tables, and the same descriptors that read a figure for the list write it for the engine. A parameter exists only if the engine reads it | `Every_kind_of_figure_a_provider_carries_is_written_from_the_set`, `The_board_follows_a_corrected_provider`, `The_board_follows_what_people_are_said_to_need`, `The_board_follows_a_corrected_scenario_whichever_copy_of_it_the_caller_held` |
| 2 | **Introducing the desk changes the product.** With nothing corrected the board differs from the one every other test pins | With no corrections the providers and scenarios the engine uses are the shipped instances themselves, not copies | `With_nothing_corrected_the_engine_is_the_shipped_one_to_the_instance`; the whole existing suite, unchanged |
| 3 | **A nonsense value.** A negative stock, a share of 3, half a day where days are whole, a figure that is not a figure | Every value is checked against its range and its wholeness, and an unknown id is refused, with a sentence naming the figure | `A_published_set_is_refused_when_it_names_nothing_strays_from_its_range_or_contradicts_itself` |
| 4 | **Values that are each in range and together impossible.** A store keeping back more than it can give; a stretch of a scenario ending before it starts | The set is checked as a whole before it exists | The same test |
| 5 | **A household gives itself a figure it has no business giving**, and a sent-by-hand post changes what a person drinks or how long a storm lasts | `ForHousehold` accepts only parameters flagged as the household's to know, inside the same range as anyone's, and makes a set for that plan only | `A_household_may_give_only_the_figures_that_are_its_own_to_know` |
| 6 | **A board worked out from two sets at once.** The providers from one and the scenario from another | The set travels on the profile and the simulation resolves the scenario under it, so there is one set per board, and the board carries the scenario it drew | `The_board_follows_a_corrected_scenario_whichever_copy_of_it_the_caller_held` |
| 7 | **The plan request gets slower** | Everything derived from a set is built once and kept on it; a day of the simulation does no lookups by name. The load harness was run after the change: plan p95 51 ms and board p95 45 ms against a target of 150 | `./build/run-load.ps1` |

## Step 2, first half: the words on the board (done)

| # | Threat | Answer | Pinned by |
|---|---|---|---|
| 8 | **The words and the board disagree after a correction.** An expert says the batteries are four days; the row holds for four and the sentence still says two | Every provider and scenario sentence that states a figure is built from it, and built again for any set that corrects it, including when only what people need has changed (the same bottles are fewer days at six litres a person). The composer and the cell wording read the providers and scenarios under the household's set | `A_corrected_figure_corrects_the_words_that_state_it`, `A_scenarios_sentence_follows_its_stretches_and_one_that_is_not_built_states_no_figure`, `The_set_is_composed_under_the_households_figures_from_the_scenario_to_the_name_on_a_source` |
| 9 | **A new sentence states a figure in plain words and nobody notices** | A sentence with no builder must contain no digit and none of a list of number words; the shipped sentence of one that has a builder must be what the builder says | `A_provider_whose_words_are_not_built_from_its_figures_states_none`, the loop in the scenario test |
| 10 | **A slow upstream becomes a server error** (found while testing this step, not caused by it) | Decision 121 | `The_pipelines_own_timeout_is_a_fault_in_every_adapter`, `A_caller_who_has_gone_is_not_answered_by_any_adapter` |

## Step 2, second half: the plan's own figures and sentences (done)

| # | Threat | Answer | Pinned by |
|---|---|---|---|
| 11 | **The plan and the board count the same thing differently.** The plan asks for 36 litres because it multiplies three days by three litres; the board holds nine litres a person because somebody typed nine; an expert corrects one | A rule reads the quantity from the provider that counts it, and its sentence is said from that. What a household's own figure counts towards is read the same way | `The_plan_asks_for_what_the_board_counts_and_says_so`, `A_households_own_figure_counts_towards_what_the_plan_asks_for_under_its_figures` |
| 12 | **A price that makes no sense.** A least above a most; a negative price | Each price is a pair checked together, inside a range from nothing to ten times what ships | `Every_action_that_costs_money_has_a_least_and_a_most_that_can_be_corrected` |
| 13 | **A corrected price is multiplied wrongly**, so four headtorches cost the price of one | The parameter is the price of one and the rule multiplies it, as it did before | `A_corrected_price_is_the_cost_on_the_plan_multiplied_as_the_rule_multiplies_it` |

## Step 3: storing, drafting, previewing, publishing and putting back (done; no screen yet)

| # | Threat | Answer | Pinned by |
|---|---|---|---|
| 14 | **Somebody who may not publishes.** An expert given an account to draft puts a figure into force | Publishing and restoring ask the store whether this operator may, every time; drafting does not need it. Disabled accounts may not | `Only_an_operator_who_may_publish_puts_a_draft_into_force_and_every_figure_it_moves_is_recorded`, `The_first_operator_may_publish_until_somebody_is_named_and_then_only_the_named_may` |
| 15 | **A change with no account of itself.** A figure moves and nobody can say who moved it, why, or on what authority | A draft needs a reason and a source. Every figure a publication moves is a row for ever: before, after, reason, source, drafter, and the set that names its publisher and time. The publication is also a hand in the operations log | `A_correction_is_drafted_with_a_reason_and_a_source…`, `Every_change_to_a_figure_is_kept_with_why_and_on_what_authority_newest_first` |
| 16 | **History rewritten.** A bad set is edited or deleted to hide it | Sets are only added; a restore is a new set naming the one it puts back, and what it moves is recorded like any other change | `An_earlier_set_is_put_back_as_a_new_set_and_what_it_moves_is_recorded`, `Nothing_is_in_force_until_a_set_is_published_and_then_the_latest_is` |
| 17 | **Two publications at once.** Each is built over the same set and the second silently discards the first | The set's number is its primary key; the second insert fails, nothing of it is kept, and its publisher is told | `Publishing_empties_the_draft_when_asked_and_a_number_already_taken_writes_nothing`, `A_publication_that_loses_the_race_or_can_no_longer_be_run_is_refused_with_a_sentence` |
| 18 | **A published set the code can no longer run** stops the site, or is half obeyed without anyone knowing | Read at start and after each publication; a correction that no longer stands is dropped and named in `CurrentFigures.Dropped`; corrections that contradict each other apply none; the plan is always built | `The_figures_in_force_are_the_shipped_ones_until_a_set_is_read_and_a_correction_the_code_no_longer_accepts_is_dropped_and_named` |
| 19 | **Injection through a reason, a source or a note** | They are parameters to parameterised statements, trimmed and capped at 400 characters, and nothing yet draws them on a page. The screen (step 5) must encode them as it does all text | The store tests round-trip them; step 5 adds the page tests |
| 20 | **A plan built from a different set than the one in force** | The handler asks for the figures in force each time it builds a profile, and the profile carries them to the rules and the board | `A_plan_is_built_from_the_figures_in_force_when_it_is_built`, `A_published_correction_is_on_the_next_plan_in_its_words_its_quantities_and_its_prices` |

Accepted: the figures in force are held in memory by the one server and read again when it publishes. A second server would not hear of a publication until it restarted; there is one server.

## Step 4: the browser and the figures (done)

| # | Threat | Answer | Pinned by |
|---|---|---|---|
| 21 | **A plan on a phone goes on showing figures that have been corrected** | The page carries the number of the set it was built with; the browser asks which is in force each time a plan is opened with the network there, and rebuilds when they differ | `A_plan_built_from_figures_that_have_since_been_corrected_is_built_again_and_says_so` |
| 22 | **A rebuild loop.** The rebuilt page still disagrees with the server and rebuilds for ever | A mark in the session says the page has just been rebuilt; that page shows the sentence and does not ask again. With no session storage there is no rebuild at all | The same journey, which waits and finds the page where it was |
| 23 | **The script's shipped figures drift from the server's** | A test parses `figures-shipped.js` and compares it with what the endpoint answers for the shipped set, field by field | `The_figures_the_script_ships_with_are_the_figures_the_server_ships_with` |
| 24 | **A forged or broken answer is kept by the browser** | The answer's shape is checked before it is kept or acted on: a whole number of a version, a bounded list, ids of the registry's shape, finite numbers. Anything else is ignored and the browser goes on with what it knew | The last part of the journey |
| 25 | **The endpoint tells the world about a household** | It is the same for everyone: the set's number and date, and the eight figures a household may give, with the values in force. No cookie is read | `The_browser_is_told_which_set_is_in_force_and_the_figures_a_household_may_give` |

## Step 5: the expert's screen (done)

`/admin/figures`, behind the operator policy like the catalogue: a signed-in account with its second factor done. It has no script. Every change is a form post with an antiforgery token.

| # | Threat | Answer | Pinned by |
|---|---|---|---|
| 26 | **Somebody not signed in reads or posts** | The page carries the operator policy; a request with no session, or a forged cookie, is sent to the sign-in before any handler runs | `The_desk_is_closed_to_anyone_who_is_not_signed_in` |
| 27 | **An expert publishes or restores by posting the form the page did not show them** | The page hides the hand, and that is a courtesy. The desk asks the store whether this operator may on every publish and restore, whatever was on the screen | `One_draft_is_shared_and_only_a_publisher_puts_it_into_force_or_takes_out_what_somebody_else_drafted` |
| 28 | **An expert throws away somebody else's correction** | A correction is taken out by whoever drafted it or by a publisher; anybody else is refused with a sentence | The same test; `A_correction_is_taken_out_of_the_draft_by_whoever_drafted_it_or_by_a_publisher_and_by_nobody_else` |
| 29 | **Script in a reason, a source, a note or a search** is drawn on a page a publisher reads (case 19, now answered) | Razor encodes every one of them; none is written with `Html.Raw`. The page has no script of its own for an injected attribute to reach, and the policy allows no inline script | `A_correction_goes_into_the_draft…` (a reason with markup in it), `A_search_finds_figures…` (a search that is a script element) |
| 30 | **A forged request from another site** drafts or publishes while a publisher is signed in | Every post needs the antiforgery token; the cookie is same-site | The framework's validation, on by default for every page; the tests post with the page's token |
| 31 | **A value that is not a number, or text of any length, reaches the store** | The value is bound as text and parsed by hand, with no sign and no grouping; reason, source and note are trimmed and capped at 400 by the desk; an id that is not a figure is refused before the desk is asked and is not echoed | `A_correction_that_cannot_stand_is_answered_at_the_figure_with_what_was_typed_still_in_the_box` |
| 32 | **A proposal vanishes.** A draft is replaced or taken out and nobody can say it was ever made | Every draft kept and every draft taken out is a hand in the operations log, with the operator, the figure and the value | `A_draft_of_the_value_already_in_force_is_refused_and_every_draft_kept_is_a_hand_in_the_log` |
| 33 | **A publisher publishes without seeing what it does** | The preview is on the draft tab above the publish button whenever anything is drafted, worked out from the same draft the page lists | `A_correction_goes_into_the_draft…`, the journey in `Desk.cs` |
| 34 | **A draft or a set a release has made impossible breaks the desk** | The desk says which correction was dropped, says the draft cannot be published and why, names the figure by its id, offers no preview and no publish button, and still draws every other figure | `What_a_release_has_made_impossible_is_said_on_the_desk_and_never_run` |
| 35 | **The desk is used to wear the server out** | It is under the same rate limit as the catalogue; a search is cut at 80 characters and is a scan of 345 definitions in memory; the preview runs only on the draft tab | `A_search_finds_figures…` |

Accepted: an expert can see every other operator's drafts, reasons and names. They are colleagues working on one draft, and that is the point of it.

Accepted: there is still no screen to name a second publisher. It is a row in `figure_publishers`, added by hand, until Rob wants somebody else to publish.

## Step 6: a household's own figures (done)

| # | Threat | Answer | Pinned by |
|---|---|---|---|
| 36 | **A household gives itself a figure that is not its to give**: what a person drinks, how long a storm lasts, a price | Only the figures flagged as a household's are used, inside the same range as anyone's, for that request only. Anything else is declined and the figure in force stands (case 5, now on the page) | `Each_figure_that_can_be_used_is_used_and_each_that_cannot_is_declined…`, `A_plan_is_built_from_the_households_own_figures_over_the_ones_in_force` |
| 37 | **A figure kept in a browser for years breaks the plan** after a release narrows its range or removes it | It is declined with a sentence at its question, the plan's figure is used, and the plan is built. Each figure is tried on its own, so one bad one does not cost the household the others | `A_figure_that_contradicts_the_set_it_is_laid_over_is_declined_and_the_others_still_stand`, `The_plan_and_the_board_are_worked_out_from_a_households_own_figures_and_say_so` |
| 38 | **A rebuild loop.** The browser keeps figures the page was not built from, rebuilds, and the page it gets still differs | The page lists what it was asked, used or not, so a declined figure is not a difference. The page that follows a rebuild does not rebuild; figures are compared to four places | The journey `A_household_gives_its_own_figure…`, which waits on a declined figure and finds the page where it was |
| 39 | **A hand-made post with text for a number, or thousands of figures** | Refused with a sentence before anything is looked up or built: twenty at most, each a plain number | `What_no_browser_of_ours_would_send_is_refused_with_a_sentence` |
| 40 | **An id from a post is written onto the page** | It is written, as text, in a hidden list and in hidden inputs, encoded by Razor; a declined id's sentence is shown only beside a question, and there is a question only for a figure in the registry | The page test reads an unknown id back from the hidden list |
| 41 | **The plan and the board under it are worked out from different figures** | Every post that carries the profile carries the own figures: the page, the board fragment, the explanation, the no-script form for what a household relies on | The same page test: the fragment says the same row was worked out from the same figure; both echo forms carry it |
| 42 | **The account's copy refuses a plan that has own figures, or accepts anything under that name** | `isAssumptions` in the browser and `PlanGuard` on the server take the same shape: up to twenty, an id of the registry's form, a number from nothing to a million | The cases added to `PlanSyncTests`; the journey saves through `saveAssumptions` |
| 43 | **A household's figures reach the server's keeping** | They are in the browser. For an account they are inside the copy encrypted in the browser, which the server cannot read. A post carries them for the length of the request, as it carries the answers | The privacy rule; nothing new is stored |

## Switching a household's own figure on and off (done)

| # | Threat | Answer | Pinned by |
|---|---|---|---|
| 44 | **An expert switches on a figure no household should set**: what a person drinks, how long a storm lasts | Only a figure flagged as switchable takes a switch; the set refuses any other with a sentence, whoever posts it | `Only_a_figure_about_a_thing_a_household_has_or_a_price_may_be_switched`, `A_switch_that_cannot_stand_is_refused_with_a_sentence` |
| 45 | **Text an expert typed is drawn on every household's plan** | It is a question of 10 to 200 characters ending in a question mark, checked by the set; it is drawn as text by Razor and nowhere as markup; only a publisher puts it into force, after reading it in the draft | `An_expert_switches_a_households_own_figure_on…` (a question with markup in it is refused for not being a question and is echoed encoded) |
| 46 | **An expert switches a figure on without a publisher** | A switch is a draft like any correction; publishing asks the store whether the operator may | The same test; `Whether_a_household_may_give_its_own_figure_is_drafted_published_and_put_back_like_a_correction` |
| 47 | **A figure a household gave is still obeyed after it is switched off** | The household's set takes a figure only where the set in force says it may give one; the rest are declined and the plan's stands | `A_households_own_figure_is_taken_only_where_the_set_in_force_says_it_may_give_one`, `A_household_is_asked_what_the_set_in_force_says_it_may_give_in_the_words_the_set_gives` |
| 48 | **A switch a release has made impossible stops the site** | It is dropped and named on the desk, like a correction, and the rest of the set stands | `A_switch_the_code_no_longer_accepts_is_dropped_and_named_and_the_rest_of_the_set_stands` |
| 49 | **History loses a switch** | Each is a row in the changes with before, after, the question, the reason and who drafted it, and a hand in the operations log; putting a set back puts its switches back and records what moved | The application test above; `Which_figures_a_household_may_give_is_kept_with_the_set_and_a_figure_has_a_draft_of_each_kind` |

## Not yet answered (later steps)

- The plan's sentences are not swept by a test the way the board's are: they are full of numbers that are facts and not figures (a date, a phone number, an insurance limit), so a list of number words would fail every one. They were read by hand. A new rule sentence that states an engine figure in plain words would not be caught.
- The counts the rules work out by formula (one power-failure torch a storey and one more; a power bank between two people, at least two; a lantern between two and one more) are logic, not figures, and are not on the desk.
