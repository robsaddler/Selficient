# The catalogue as a system

*31 October 2026. Written after the first pick Rob opened did not land. The affiliate links are the business, so the catalogue stops being a JSON file and becomes a system with a database, background work, a judge and a reviewer.*

## What has to be true at launch

1. Every link a household sees was opened by a machine in the last 24 hours and landed on the item.
2. Every price shown was read from the shop in the last 24 hours, or is not shown.
3. Every pick is the right item for the action's specification (litres, watt-hours, watts, hours), not merely a page that mentions the words.
4. When a shop changes, we know before a household does.
5. We know which picks earn, without cookies or analytics.

## The shape

### Storage: SQLite through Dapper

The site's first database, on the vhost, `catalogue.db`, read by the web app and written by the worker.

- `retailers`: id, name, host, network (amazon, awin, direct, none), affiliate rule (tag parameter, Awin merchant id), `walls_robots` (Argos, Screwfix), notes.
- `products`: id, action id, brand, model, canonical name, the spec fields the engine cares about (litres, watt hours, watts, hours, pack size), the sentence that says why, status (proposed, live, retired), who approved and when.
- `offers`: id, product id, retailer id, url, affiliate url, price pence, currency, in stock, `seen_at`, source (feed, page, hand), status (live, failing, hidden).
- `checks`: offer id, checked at, HTTP status, verdict (landed, no results, wrong item, robot wall, dead), evidence (title, first lines, price read), judged by (rule, model, person).
- `clicks`: offer id, day, count. No person, no cookie, no address.

The current JSON becomes the seed: one importer, run once.

### The worker: four jobs on a clock

A hosted service in the web app (one deployment, one set of secrets), or the same code as a Windows service if the vhost's app pool recycles too often. Jobs are idempotent and logged.

| Job | When | What |
|---|---|---|
| **Feeds** | daily, 05:00 | Pull the Awin product feed for each joined advertiser and the Amazon PA-API items for each ASIN we hold; update price, stock, url. Structured data, no browser, the reliable half. |
| **Verify** | nightly, 02:00, and after any catalogue change | Open every live offer in Chromium (what `check-links` does now), record a `check`. Two consecutive failures hide the offer and raise an issue. A feed-backed offer that the page contradicts is flagged. |
| **Discover** | weekly, Sunday | For every action with fewer than two live offers, or an offer older than 30 days: search each willing retailer, collect candidate pages, hand each to the judge, propose the best two as `proposed` products. Nothing goes live without a person. |
| **Prune** | nightly | Hide any offer not seen for 3 days; retire products with no live offer for 30; keep the history. |

### The judge: where the AI earns its place

Claude is the right tool for exactly one thing here: reading a shop page and answering a structured question. For a candidate page and an action's spec it returns `{ is_the_item, meets_spec, price_pence, pack_size, in_stock, confidence, why }` against a JSON schema. It never chooses what the household needs (the engine does that) and never invents a price (it reads one or returns null). At a fraction of a penny a page, verifying 40 offers nightly and judging 200 candidates a week costs less than a coffee a month. The rule engine's specifications are the rubric, so a 1 kWh station that is really 512 Wh fails `meets_spec` and never reaches the page.

### The reviewer: a small admin page

Behind an operator key in configuration, `/admin/catalogue`: proposed products with the judge's reasoning and the page it read, one button to approve, one to ban (a product or a whole retailer), one to pin a pick first. Live offers with their last check. Click counts by action. Twenty minutes on a Sunday evening is the whole operating cost.

### The click counter

Every link on the site becomes `/out/{offer}`: count, then redirect to the affiliate URL. No cookie, no person, one number per offer per day. It is how the catalogue learns what earns and what to drop, and it is the only honest answer to "what sells" before the networks report.

## The money, and its rules

- **Amazon** (15 of 38 picks today). Associates needs a live site, then three qualifying sales in 180 days before the Product Advertising API opens. Until the API, Amazon prices must not be shown stale: the operating agreement forbids displaying a price not fetched from the API within 24 hours. So Amazon offers show no price until PA-API is on, only "see the price on Amazon". The search links with the tag are compliant and earn.
- **Awin** (Argos, Wickes, Screwfix, Toolstation, Halfords, Currys, Safelincs, Ledlenser, and most of the British high street). One publisher account, per-advertiser approval taking days to weeks, then product feeds with price and stock and deep links that pay. This is the half that makes the catalogue broad and the prices honest.
- **Brand programmes** (Bluetti, EcoFlow, Anker) through Impact or ShareASale. Highest commission on the dearest items.
- **Solar leads** stay the biggest single line and are a separate build.

## What to build first

Phase A needs no accounts and makes launch safe: the database and the importer; the nightly verify job with the judge; the hide-and-raise rule; `/out` counting; the admin page. Three to four days. At the end, every link on the site has been opened in the last day and judged to be the item, and anything that breaks is hidden before a household sees it.

Phase B needs Rob's accounts: Awin and Amazon importers and link builders. Two to three days once the first advertisers approve.

Phase C is discovery: the weekly proposer and the review flow. Two days.

Phase D is the launch itself: a share card for the figure (the one thing people will paste into a group chat), the Be Ready tie-in, the press list, and a catalogue of twenty picks every one of which Rob has opened himself the day before.

## What this does not fix

A shop that walls off robots cannot be verified by a machine; those picks are hand-checked or moved. A judge reads what a page says and can be wrong about a page that lies. Feeds lag the shelf by a day. None of these is a reason not to build it; they are the reasons the reviewer exists.
