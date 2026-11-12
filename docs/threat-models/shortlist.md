# Threat model: saved for later (M26)

*12 November 2026. The last milestone of `docs/design/accounts-and-sharing.md`: a household can save a pick from "Where to look" for later, and see what it saved on one page, grouped by shop. The list is part of the plan's snapshot, so it is exported, copied to another device and kept in step by an account like the rest of the plan.*

## What it is

- A "Save for later" button beside each pick on the plan. What is kept is the offer's number, in the browser's store with the plan, and nothing else: no name, no price, no shop.
- `/plan/shortlist` shows the list. The browser posts the numbers; the server looks each up in the catalogue the pages already hold and renders the picks from its own data, grouped by shop, with each link going out through `/out/{id}` like every other shop link.
- The snapshot gains `shortlist`: at most a hundred offer numbers. The browser's guard and the server's guard for a synced plan both check it.

## Cases

| # | Threat | Answer | Pinned by |
|---|---|---|---|
| 1 | **The list is a way to put text on the page**: a name, a link or a price of the poster's choosing | Nothing but numbers is accepted. Every word and every address on the page comes from the catalogue, looked up by number; a number the catalogue does not hold shows nothing but a count of things no longer there | `The_shortlist_is_rendered_from_the_catalogue_and_never_from_what_was_posted` |
| 2 | **An open redirect, or a link that skips the disclosure and the counter** | Links are `/out/{id}`, which only goes where the catalogue says that offer goes | The same test (every link is `/out/`), `OutEndpointTests` |
| 3 | **A very long list**: a post of a million numbers to make the server work | At most a hundred numbers are read, each at most eighteen digits; the lookup is a dictionary read per number from memory, with no database behind it. The page is on the plan's rate limit | `What_is_posted_is_cut_to_a_hundred_numbers_and_anything_else_is_ignored` |
| 4 | **The list says something about a household** to us | It is posted to draw the page and kept nowhere on the server, unless the household has an account, in which case it is part of the plan the account keeps, encrypted, under the rule Rob agreed. The request log holds the path, never the form | By construction; the privacy page's account section |
| 5 | **A hidden or removed offer lingers** on a list and sends someone to a dead or changed link | A number the catalogue no longer shows is not rendered. The page says how many went, and the browser takes them off the list | `An_offer_that_has_left_the_catalogue_is_counted_and_not_shown`, the journey |
| 6 | **A snapshot with a hostile shortlist**: imported from a file, received as a copy, or pulled from an account | Both guards refuse a snapshot whose shortlist is not a bounded list of digit strings, so it is never written to the store; and what the store holds is only ever sent back as numbers | `PlanSyncTests` (the shortlist cases), the journey (an import with a hostile shortlist is refused) |
| 7 | **Cross-site posts to the page** | A Razor form with an antiforgery token. The post changes nothing anywhere: it only draws a page for the poster | The page tests (a post without a token is refused) |

## Accepted

- Prices on the list are the catalogue's, as seen on the date shown. The total is a sum of those, and says "as seen".
