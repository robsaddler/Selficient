# Threat model: marketing, legal and search (M9)

*30 October 2026. Written before the code.*

## What it is

The landing page, the privacy and terms pages, the share metadata and structured data, a sitemap, `robots.txt` and `llms.txt`. All static, all public, none of it takes input.

## Assets

- Honesty: what the pages promise about data must be what the code does.
- Search hygiene: a crawler must never be sent to, or index, a page that is personal or transient.
- The structured data: it is read by search engines and must not be a way to inject anything.

## Abuse and failure cases

| # | Case | Control | Test |
|---|---|---|---|
| 1 | The privacy page drifts from the code | The upstream list is one record in `LegalCopy` and the page renders it; the services it names are the typed clients in composition | `The_legal_pages_say_what_leaves_the_browser_and_who_to_write_to` |
| 2 | A private page lands in a search index | `NoIndex` pages get `noindex, nofollow` and no canonical, no share image; the sitemap is a hand-kept list of public pages; `robots.txt` disallows the rest | `Private_and_transient_pages_are_kept_out_of_search`, `The_sitemap_lists_only_public_pages_and_each_one_answers` |
| 3 | Structured data as an injection path | Serialised from constants and the page's own records with the JavaScript-safe encoder; the block carries the CSP nonce and is data, not code | `Structured_data_repeats_the_questions_word_for_word` |
| 4 | Copy that breaks the house voice | A test strips every public page to text and refuses exclamation marks, apologies and "Submit" | `Every_public_page_keeps_the_copy_rules` |
| 5 | The contact address or legal wording differs between pages | One `LegalCopy` for every page and the structured data | pinned above |

## Outcomes

*Adversarial pass, 30 October 2026.*

- Cases 1 to 5 hold and are pinned. The copy-rules test walks seven public pages and found nothing to change, which says the earlier milestones kept the voice.
- Found and fixed: the first canonical link tripped the "no page references a CDN" guard from M5; the guard now exempts the site's own origin and nothing else.
- Found: Razor encodes apostrophes as numeric entities, so a test that compares page text with a constant must encode it the same way; two legal strings were reworded to avoid the question altogether.
- Residual: the contact address, the company name and the ICO position are placeholders until Rob confirms them; the handover says which three lines to change.
