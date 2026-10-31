# Threat model: kit catalogue and affiliate links (M4)

*30 October 2026. Written before the code.*

## What it is

A catalogue of one to three product picks per plan action with the price seen, the date it was seen, the retailer and a link, embedded in the Infrastructure assembly as JSON and reviewed monthly. The plan page shows the picks under each action. Where a retailer runs an affiliate programme and a tag is configured, the tag is appended; a disclosure sits on the page.

## Assets

- The reader's trust: a wrong or stale price, or a link to the wrong thing, costs it.
- The reader's browser: an outbound link is a hand-off to another site.
- The site's standing with retailers and regulators (CAP code on affiliate disclosure).

## Abuse and failure cases

| # | Case | Control | Test |
|---|---|---|---|
| 1 | A catalogue entry carries `javascript:` or `data:` or plain `http:` links | `KitPick` requires an absolute https `Uri`; the loader refuses anything else at startup, so a bad entry stops the deploy rather than the reader | `Picks_refuse_anything_but_https` |
| 2 | A link points at a host nobody reviewed (typosquat, phishing) | Host allowlist in options; the loader refuses hosts outside it at startup | `Picks_refuse_hosts_outside_the_allowlist` |
| 3 | Affiliate tag injection via configuration | The tag is validated against `^[A-Za-z0-9-]{1,40}$` and appended only to allowlisted Amazon hosts as a single query parameter via `UriBuilder` | `The_amazon_tag_is_appended_safely_and_nowhere_else` |
| 4 | Catalogue text (names, notes) reaches the page unencoded | Razor encodes; names are also length-capped at load; no raw HTML anywhere | `Catalogue_text_is_encoded_on_the_page` |
| 5 | Tabnabbing from `target="_blank"` | Every outbound link carries `rel="sponsored noopener noreferrer"` | `Outbound_links_carry_sponsored_noopener_noreferrer` |
| 6 | A pick refers to an action that no longer exists, or a purchasable action has no pick | Load-time cross-check against the engine's full action set; test enumerates every rule over varied profiles and asserts coverage | `Every_pick_names_a_real_action`, `Every_purchasable_action_has_a_pick` |
| 7 | Stale prices mislead | Each pick carries `seen`; the page shows it; a test fails if any price is older than 120 days from the build date, as a reminder to review | `No_price_is_older_than_the_review_window` |
| 8 | Disclosure missing where links appear | Rendered on any page that renders a pick; asserted | `The_disclosure_appears_with_the_picks` |

## Outcomes

*Adversarial pass, 30 October 2026.*

- Cases 1 to 4, 6 and 7 hold as written. Test names on the page side differ slightly: cases 5 and 8 are asserted together by `Picks_render_under_their_actions_with_safe_links_and_the_disclosure`, which walks every outbound anchor; case 1 is `Picks_refuse_anything_but_https_at_load` plus the `KitPick` domain tests.
- Tried: an Amazon look-alike host (`AMAZON.co.uk.evil.example`). It is refused by the allowlist and, even if allowlisted by mistake, never receives the tag, because the tag check is an exact host comparison rather than a suffix match. Pinned by test.
- Tried: an empty affiliate tag (`""`) from a half-filled environment variable. Treated the same as no tag; no `?tag=` is ever emitted. Pinned by test.
- Tried: a catalogue entry with no `seen` date, or a malformed one. The loader used to throw a bare `FormatException` from `ParseExact`; it now throws a sentence naming the entry, so a deploy fails with a readable reason. Pinned by test.
- Tried: hostile catalogue text (`<script>`, quotes, ampersands) through a substituted catalogue. Razor encodes every occurrence, including inside `href`. Pinned by `Catalogue_text_is_encoded_on_the_page`.
- Residual: the 120-day staleness test is a reminder rather than a guarantee; the price shown is still the price we saw, and the page says so beside each pick. Accepted.
- No findings from the security review beyond the above.

## Outcomes, second pass (31 Oct 2026): the links themselves

Rob opened the first pick he tried and it did not land. Every link had been a shop's homepage, then a guessed search address; neither is a link. The catalogue now carries, for each pick, the shop's own search for the item's distinctive term, and every one of the 38 was opened in a real Chromium and seen to land on the item (`./build/check-links.ps1`, report in `artifacts/links/report.md`). Four picks moved from shops that show a robot wall to a browser that is not a person (Argos, Screwfix) or whose search needs a person's browser (Safelincs, Halfords) to Amazon, where the same item is sold and the tag applies. The checker runs every Monday (`.github/workflows/check-links.yml`) and raises an issue with the report when a link stops landing, because a household that lands on nothing goes and searches for itself without the tag. What it cannot do: tell a search page that found the wrong item from one that found the right one with certainty; it checks that the page counts results or names the item, and the weekly issue is read by a person.
