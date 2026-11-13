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

## Not yet answered (later steps)

- Who may change a figure, the audit of every change, and rollback (step 3).
- A sentence that states a figure in words and is not built from it will disagree with the board after the first correction (step 2). Until step 2 is done, nothing can be published, because there is nowhere to publish from.
- The browser's own arithmetic has two constants of its own (step 4).
- The plan's rules (how many litres to buy, how many cans) and their price ranges are not parameters yet (step 2).
