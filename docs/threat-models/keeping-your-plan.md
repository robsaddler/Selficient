# Threat model: saying where the plan is kept (M21)

*12 November 2026. The first milestone of `docs/design/accounts-and-sharing.md`: the header chip and its panel in guest mode, the words beside the start buttons, the sentence under the first plan, the request for persistent storage and the Safari warning. No server change, no new endpoint, no new input.*

## What it is

A chip in the header of every household page, a native `<dialog>` behind it, and a script (`keep.js`) that reads whether a profile is in the browser's own store and swaps two pre-rendered sentences. Every sentence is a constant in `KeepCopy` rendered by the server. The script writes two flags to `localStorage` (the sentence has been said; the browser has been asked to persist storage) and calls `navigator.storage.persist()` once.

## Assets

- The reader's trust that what the page says about where their plan is kept is true.
- The plan in the browser's store, which this milestone reads but never writes.

## Cases

| # | Threat | Answer | Pinned by |
|---|---|---|---|
| 1 | The page says something untrue about where the plan is: "saved on this device" when nothing is, or the reverse | The chip is rendered as "No account needed" and only changes when the script has read a valid profile from the store, through the same `isProfile` guard the plan page uses. It follows the store's change channel, so clearing the plan in another tab puts it back | `A_visitor_is_told_no_account_is_needed_and_then_where_the_plan_is_kept` |
| 2 | Words from the store or the address reach the page as markup | Nothing from the store is written to the page. The script only toggles `hidden` on sentences the server rendered from constants | `Every_household_page_says_no_account_is_needed_where_sign_in_would_be` |
| 3 | The chip is a dead control without the script, or the script blocks the link | It is a link to the privacy page; the script prevents the navigation only when the dialog it names exists | The same web test (the `href`), and the journey (the address does not change) |
| 4 | The promise outlives the truth once accounts exist | The sentences live in one class, `KeepCopy`, and M23 changes them there with the rule in `CLAUDE.md`. The Safari sentence does not yet mention an account because one cannot be made | Review at M23 |
| 5 | A tampered `localStorage` flag hides the first-plan sentence or repeats it | It is a courtesy and carries no state that matters; the chip and the panel say the same thing at any time | Accepted |
| 6 | The chip tells an operator in the back office that no account is needed, on a page that is all about their account | The back office does not carry it (`KeepCopy.ShowsOn`) | `Every_household_page_says_no_account_is_needed_where_sign_in_would_be` |
| 7 | The persistent-storage request prompts or nags | Chromium and Safari answer `persist()` without a prompt; Firefox may ask once. It is called once per device, after a plan exists, and the answer is not acted on | The journey (asked once) |
| 8 | The chip pushes the header wider than a phone | It takes a line of its own under the links below 44rem | `The_header_chip_sits_where_sign_in_would_be_and_takes_its_own_line_on_a_phone`, and the sideways-scroll assertion in the journey |

## Not in this milestone

Sending a copy (M22) and accounts (M23 onward) have their own models, `handoff.md` and `household-accounts.md`, written before their code.
