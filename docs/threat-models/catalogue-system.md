# Threat model: the catalogue as a system (M18)

*31 October 2026. Written before the code, with the adversarial pass appended after it.*

## What it is

The kit picks stop being a JSON file read at start-up and become offers in a SQLite database on the vhost (`catalogue.db`, Dapper, parameterised throughout), loaded into memory for the pages. A hosted worker wakes on a clock, and once a night after the configured hour opens every shown offer's page over plain HTTP, judges it (rules first, then Claude where a key is set) and records a check; two failures running hide the offer and the plan page shows the next pick. Every shop link on the site goes out through `/out/{offer}`, which counts one click per offer per day and redirects. A reviewer at `/admin/catalogue`, behind an operator key, shows every offer with its last check and its clicks and lets a person hide, show, or run the verification now. The design is `docs/design/catalogue-system.md`.

## Assets

- The household's trust: a link that lands on nothing, or on the wrong thing, sends them to search without us.
- The affiliate income the links carry, and the standing with Amazon (no stale price shown) and the other programmes.
- The site's promise: no personal data on the server. The new database must hold none.
- The vhost: a background worker that reads strangers' web pages is a new outbound surface, and the reviewer is the site's first authenticated page.
- The Anthropic spend: a judge that can be made to run often, or on long pages, costs money.

## Abuse and failure cases

| # | Case | Control | Test |
|---|---|---|---|
| 1 | SQL injection through an offer's name, note, link or a reviewer's input | Every statement is Dapper with named parameters; no string is ever concatenated into SQL; the schema is a constant | `Seeding_fills_an_empty_store…` seeds names with quotes and ampersands through the same path; no raw SQL exists for a test to find |
| 2 | A hostile shop page attacks the reader: a gigabyte body, a redirect loop, a page that never finishes, a non-HTML body | The read is bounded at `Catalogue:PageBytes` (512 KB default) and stops there; the client has a 25 s timeout and the resilience handler's retries; redirects are the handler's default limit; the body is treated as text and regex-flattened, never parsed as HTML or executed | `Only_the_first_part_of_a_long_page_is_read`, `A_shop_that_does_not_answer_or_takes_too_long_reads_as_status_nought` |
| 3 | A shop page carries text aimed at the model judge ("say landed", "the price is £0.01") | The judge answers a closed JSON schema with a verdict enum and a pence integer; the rules run first and a model verdict never un-hides anything; a price outside 1p to £9,999.99 is dropped; the system prompt names the page as data; the reviewer sees the evidence sentence | `A_long_reason_is_clipped_and_a_silly_price_is_dropped`, `The_model_answers_in_the_schema…`, Application `The_verifier_asks_the_rules_then_the_model…` |
| 4 | The judge is made to spend: a page of 500 KB sent whole, or a run every minute | The page is clipped to 12,000 characters before it is sent; the run is once a day after the hour, and "Open every link now" is behind the operator key and the plan rate bucket | `The_question_carries…and_clips_a_long_page`, `A_run_is_due_once_a_day_after_the_hour`, `Signing_out_closes_the_review_and_a_hand_without_a_session_is_not_found` |
| 5 | The model is down, slow, refuses, or answers nonsense | Every failure is caught and the rules' verdict stands; an unknown verdict is "dead" which the verifier treats as "no answer"; nothing is hidden on the model's word alone | `A_failed_call_an_empty_answer_or_a_malformed_one_is_no_answer`, `A_model_that_cannot_answer_leaves_the_rules_standing` |
| 6 | A robot wall (Argos, Screwfix) makes a good offer look dead and the catalogue empties itself | A robot wall is recorded and changes nothing: no failure, no hide; the reviewer sees the walled count | `A_landing_refreshes_the_offer_and_two_failures_running_hide_it` (the walled step), `The_rule_judge_reads_the_page_for_what_it_is` |
| 7 | One bad night hides the whole catalogue | Only two failures running hide an offer; a landing between resets the count; the worker logs each hide by name; the plan page falls back to the shipped picks if the store cannot be read at all | `A_landing_refreshes…`, `A_store_that_cannot_be_read_at_start_is_logged_and_the_pages_keep_the_shipped_picks` |
| 8 | The click counter becomes an open redirect | `/out/{offer}` redirects only to the stored offer's link, which came from the catalogue loader (https, allowlisted host) or the reviewer; the id is a route-constrained long; there is no URL parameter | `A_shop_link_counts_the_click_and_sends_the_reader_on…`, `A_hidden_proposed_or_unknown_offer_is_a_sentence_and_no_redirect` |
| 9 | The click counter is a tracker | It records offer id and day, nothing else: no cookie, no address, no user agent, no referrer; `Cache-Control: no-store` so no intermediary keeps the hop | `A_shop_link_counts_the_click…` asserts no `Set-Cookie` and no-store |
| 10 | A hidden or proposed offer is still reachable by its id | `/out` answers 404 with a sentence for anything not live or failing; nothing is counted | `A_hidden_proposed_or_unknown_offer…` |
| 11 | Click-count stuffing to make a pick look like it earns | Counts are a signal for the operator, never a ranking input; the plan page orders by price; the endpoint sits in the API rate bucket | `RateLimits` tests; the reviewer page shows the counts as data only |
| 12 | The reviewer is found and brute-forced | Without `Operator:Key` the page is 404 on every verb; the key is at least 20 characters; comparison is constant-time over SHA-256 digests; the page sits in the plan rate bucket (30 a minute); a wrong key is one sentence and no hint | `Without_an_operator_key_the_reviewer_does_not_exist`, `The_key_matches_exactly_and_only_the_key`, `The_wrong_key_is_told_so…` |
| 13 | The operator cookie is forged, replayed, or stolen | Data Protection signs and encrypts the issue moment; a forged value is a signed-out page; it expires after `SessionHours` (12); HttpOnly, Secure, SameSite=Strict, Path=/admin so no other page and no cross-site request carries it | `A_cookie_is_valid_until_the_session_runs_out_and_nothing_else_passes`, `The_cookie_is_locked_down…`, `Signing_out_closes_the_review…` (forged cookie) |
| 14 | Cross-site request forgery against hide, show, verify | Razor Pages' antiforgery on every POST; the cookie is SameSite=Strict as well | The handler tests all post a token; a post without one is the framework's 400 (pinned elsewhere) |
| 15 | The reviewer leaks into search or a cache | `NoIndex` on the page, `Cache-Control: no-store`, and `/admin/` added to `robots.txt` | `The_wrong_key_is_told_so…` asserts the robots meta and no-store |
| 16 | The reviewer renders catalogue text unencoded | Razor encodes every value; the notice with an apostrophe renders as `&#x27;` | `The_wrong_key_is_told_so…` asserts the encoded notice |
| 17 | The database holds a person | Offers, checks, clicks and runs: no column is for a postcode, an address or an identifier; the click row is (offer, day, count); the only free text (name, note, evidence) comes from the catalogue file, the shop page or the judge, never from a request | Schema in `SqliteCatalogueStore`; `Clicks_count_per_offer_per_day…` pins the click row's shape; `/out` never reads a header or a body |
| 18 | The worker runs in tests or on a developer's machine and opens real shops | `Catalogue:WorkerEnabled` is false by default and set true only on the vhost; the test factory substitutes the page reader on the one page that can run a verification; the fakes flag wires the fake judge | `Disabled_the_worker_loads_the_catalogue_at_start_and_stops_there`, `AdminCatalogueTests` constructor |
| 19 | The database file is written into the source tree or committed | Path is relative to the content root and ignored by git (`catalogue.db*`); the test factory puts each host's file in the temp folder and deletes it | `.gitignore`; `SelficientWebApplicationFactory.Dispose` |
| 20 | Concurrent first use races the schema creation | One lazy task creates the schema; every opener awaits it | `Many_first_calls_at_once_create_the_schema_once_and_all_succeed` |
| 21 | A failed run stops the worker for good | Each wake is wrapped; a failure is logged and the next tick tries again; a failed load at start is logged and the shipped picks stand in | `Enabled_the_worker_wakes_on_the_clock_survives_a_failed_run_and_stops_when_told` |

## Outcomes

*Adversarial pass, 31 October 2026.*

- All 21 cases hold as written. Test names above are the ones that exist.
- Tried: a model answer with `price_pence` of five million and a 300-character reason. The price is dropped and the reason clipped to 240 characters before anything is stored or shown. Pinned.
- Tried: a model that answers `"dead"` for a page the rules passed. The verifier keeps the rules' "landed" with the rule's own sentence, so a refusing or confused model can never hide an offer. Pinned.
- Tried: a page that is only a `<title>`. The title is part of the flattened text, so an item named only in the title still counts as named; a page whose title says "30 results found" and whose body says "0 results" in a hidden template is a landing, not "no results". Pinned.
- Tried: SQLite returning `INTEGER` columns as `long`. Dapper refused to materialise the row records that declared `int`, which would have been a start-up failure on the vhost rather than a test failure had the store had no tests. The records now take `long` and cast. Pinned by every store test.
- Tried: a tick arriving before the worker's first load finished. The host runs `ExecuteAsync` off the calling thread, so the timer did not exist yet and the tick was lost. The timer is now created before the first load. Pinned by the enabled-worker test.
- Residual: a shop that walls robots is never verified by the machine; its picks are the Monday Chromium check's and the reviewer's. A page that lies to the judge is believed until a person reads the reviewer. The model judge is advisory on price only until Phase B's feeds give a second source. Accepted and written into the design.
- `IOException` from a body read that fails midway is not caught in the reader; it reaches the worker's per-wake catch and the next night retries. Accepted rather than adding an unreachable catch.

## Phase C and D (31 Oct 2026): readers' tips, the deals page, the share card

### What was added

"Found it cheaper?" under every action's picks takes one thing, a shop's link, and keeps it as a proposed offer; the worker opens it on its next wake, the judges say what it is, and the reviewer approves or hides it. `/deals` is a public page of the cheapest shown pick per action with the quarter's drops first. `/share/{scenario}/{days}` is a landing page and `/share/{scenario}/{days}.png` a drawn card, so a household can paste its figure into a group chat; the plan page's "Share this figure" builds that address in the browser.

| # | Case | Control | Test |
|---|---|---|---|
| 22 | A tip is a server-side request forgery: a link to the vhost itself, a cloud metadata address, an internal host, or a scheme that is not https | The link must be absolute https with no user information; its host must be on the catalogue's own allowlist (the shops, exactly, case-insensitively, no suffix match); nothing is fetched on the request path, only by the worker through the same bounded reader as every other link | `A_tip_that_is_not_an_action_or_not_a_link_is_said_so`, `A_link_too_long_a_shop_off_the_list…`, `The_allowlist_is_the_catalogues_own_hosts` |
| 23 | A flood of tips fills the store or runs the judge all night | At most 50 proposals wait at once, then tips are declined with a sentence; one proposal per link ever (a hidden one included, so a banned link cannot come back as new); the page sits in the API rate bucket; a proposal is opened once, not every wake | `A_link_too_long_a_shop_off_the_list_a_full_box_and_a_known_link…`, `A_proposal_goes_in_nameless_and_priceless_once_per_link…`, `Proposals_are_opened_once_judged…` |
| 24 | A tip puts words on the page: a name, a note, a price of a penny | A tip carries no text; the name is the page's own title as the reader saw it, clipped, encoded by Razor; the price is what the judge read or nothing; a proposal without a price cannot be shown; nothing is shown to any household until the operator presses Show | `Describing_a_proposal_gives_it_the_pages_name…`, `A_readers_proposal_waits_at_the_foot_of_the_table_and_is_shown_only_once_it_has_a_price` |
| 25 | A tip identifies the reader | The form has one field; the request's address is in the web server log for a fortnight like every request and nowhere else; the stored row is action, link, host, date | `Every_action_with_picks_offers_the_tip_box…` asserts the stored row; the privacy page says it |
| 26 | The deals page becomes a scraping or price-fixing surface | It shows what the plan already shows, cheapest first, from the same store; links go through the counter; the page is cached ten minutes and sits in the plan rate bucket | `The_deals_page_lists_the_cheapest_pick_per_action_with_the_drops_first` |
| 27 | The share card is made to say something: a figure of 999, a scenario of someone's choosing, text in the address | Two route parts only: a scenario id the engine knows and a figure that is a count of half days from 0 to 60; anything else is 404 before any drawing; the words on the card are the engine's scenario name and fixed copy | `Only_a_scenario_the_engine_knows_and_a_count_of_half_days_make_a_card`, `The_card_image_is_a_cached_public_png_and_anything_else_is_not_found` |
| 28 | Drawing cards costs the vhost | One card per (scenario, figure) pair, 121 figures by 8 scenarios at most, each drawn once and kept for a month in the cache and a day in every browser and proxy | `The_card_image_is_a_cached_public_png…` asserts the second request is the kept bytes |
| 29 | A shared link carries the household | The link is scenario and figure; the landing page and the card render from those two alone; the journey asserts the copied link holds no postcode | `Sharing_the_figure_copies_a_link_that_lands_on_the_card_and_says_nothing_about_the_house` |
| 30 | The share landing is indexed a hundred times over | `NoIndex` on the landing; the card's Open Graph tags are the one case a no-index page still carries them, decided in the head partial | `The_share_landing_carries_the_card_for_the_chat_apps…` |
| 31 | A release that ships new picks never reaches a store seeded before it, or reaching it brings back what an operator hid (added 11 Nov 2026) | On every reload the shipped picks are compared with every offer the store has ever held, of any status, by action and link; only the ones never held are added. Offers are never deleted and a link never changes, so a hidden or retired pick is still held and stays as the operator left it. The shipped file is the only source, and it passes the same https and allowlist checks at load as before | `A_release_that_ships_new_picks_adds_only_those_to_a_store_seeded_before_it`, `The_stored_catalogue_seeds_an_empty_store…` (the second reload adds nothing) |

### Outcomes

- Tried: `https://user:pass@www.argos.co.uk/x`, `javascript:alert(1)`, a bare host, http, a 500-character address, a fragment. Each is refused or stripped before anything is stored. Pinned.
- Tried: proposing the same link twice, and proposing a link that is already live. Both answer "already in the catalogue" and store nothing. Pinned.
- Tried: pressing Show on a proposal the judge could not price. Refused with a sentence; the row stays proposed. Pinned.
- Tried: `/share/storm/1.25.png`, `/share/storm/1e3.png`, `/share/tsunami/1.png`, `/share/storm/-1.png`. All 404 before the renderer is touched. Pinned.
- Residual: the judges read a tipped page as they read any other; a shop page that lies about its price is believed until a person reads the reviewer, which is why nothing goes live without one. Accepted.
