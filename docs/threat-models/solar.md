# Threat model: solar and battery estimator (M6)

*30 October 2026. Written before the code.*

## What it is

A page under the plan that takes the roof (aspect, pitch, panel count), the battery (size and backup level), the household's electricity use and tariff situation, fetches the yield for that roof from PVGIS (the European Commission's open service, keyless), and runs a deterministic monthly model of self-consumption, export, overnight shifting, cost and payback. Optionally, behind an explicit consent step and only when a key is configured, Google's Solar API describes the roof segments so the form can be pre-filled. Nothing is stored; the estimate is a page.

## Assets

- The reader's money: a figure that is wrong by a factor rather than a margin leads to a £15,000 decision. Every number traces to a documented assumption.
- The Google key: it is billable and must never reach a page, a log or a URL a reader sees.
- Upstream goodwill: PVGIS is free and rate-limited; one household must not become a hundred calls.
- Privacy: coordinates go to PVGIS (necessarily) and, only with consent, to Google. Never to a log.

## Abuse and failure cases

| # | Case | Control | Test |
|---|---|---|---|
| 1 | Hostile form values (negative panels, 10,000 kWh battery, pitch 500°, aspect "../") | Every input is a closed enum or a bounded value object; the handler refuses anything outside the range with a sentence; the PVGIS URL is built only from those validated numbers | `Solar_inputs_are_bounded`, `The_pvgis_url_carries_only_validated_numbers` |
| 2 | PVGIS down, slow, or answering with junk | Typed client with the standard resilience pipeline and the body cap; a non-success or unparseable answer is a `lookup.unavailable` fault; the page says so in a sentence and offers the fake-free compass path again | `Pvgis_failures_are_a_sentence_not_a_stack_trace` |
| 3 | PVGIS hammered by one household | Yield cached in `HybridCache` for thirty days keyed on coordinates to three decimals, aspect and pitch; the plan rate limit applies to the page | `Yields_are_cached_by_rounded_coordinates` |
| 4 | Google key leakage | Key lives in `Google:SolarApiKey` only; sent as a header, never a query string; the typed client has its loggers removed; the page never renders it; a test asserts the key appears in no response body and no log | `The_google_key_never_leaves_the_server` |
| 5 | Google called without consent | The roof lookup is a separate POST handler that requires a `consent=true` field; the GET page and the estimate POST never call Google; without a key the provider is the null object and the consent panel is not rendered | `Google_is_only_called_with_consent_and_a_key` |
| 6 | Google answers with a hostile or huge payload | Body cap; segments bounded to twenty; azimuth, pitch and area range-checked before use; anything odd degrades to "no roof data" | `Hostile_roof_data_is_ignored` |
| 7 | Money maths drift (floats, rounding) | All money in `Money` pence; yields and kWh as `decimal`; the golden test reproduces the committed review for Rob's roof within ten percent | `Robs_roof_matches_the_review` |
| 8 | Grants and deadlines go stale | Each grant and the VAT deadline carry a `seen` date in code; a test fails when any is older than 180 days, as a reminder | `Grant_copy_is_reviewed_twice_a_year` |
| 9 | Coordinates in logs | Shared with the geography adapters: `RemoveAllLoggers()` and the Serilog overrides; the real-stub log test covers PVGIS too | `Nothing_about_the_roof_reaches_the_log` |

## Outcomes

*Adversarial pass, 30 October 2026.*

- Case 1 holds: every input is a closed enum or a bounded value object (`RoofPitch` 10 to 60, `PanelArray` 2 to 40, `Battery` 0 to 30 kWh, `AnnualDemand` 500 to 30,000), the form repeats the ranges as sentences, and the PVGIS path is built from those numbers only; a test pins the exact URL. An undefined aspect from a hand-made POST is `dwelling.unknown-choice`.
- Case 2 holds: PVGIS answers that are down, garbage, `null`, missing blocks, eleven months, a month numbered 13, a month without a figure, or a year under 300 kWh/kWp are all one sentence, pinned nine ways. The adapter orders months by their `month` field rather than trusting the array order, which PVGIS happens to honour today.
- Case 3 holds: yields cached thirty days keyed on coordinates to three decimals (about a hundred metres), aspect and pitch; a failure is never cached. The page is under the plan rate limit.
- Case 4 holds: the key is bound into `GoogleOptions` with a shape check, sent as `X-Goog-Api-Key`, and the Google client's loggers are removed like every upstream's. The key is read by composition only to decide whether to register the adapter; without it the null object is registered and the consent panel is not rendered. No test key appears in any page.
- Case 5 holds: the roof lookup is its own handler, which needs the `consent` field and an available provider, and otherwise behaves as a plain estimate. The GET and the estimate POST never touch the provider. Pinned four ways, including a substituted provider that answers with no segments.
- Case 6 holds: segments with impossible pitch, azimuth or area are dropped, more than twenty are cut, odd panel counts fall back, and an answer with nothing usable is "no roof data". Pinned.
- Case 7: money is integer pence throughout, flows are `decimal` rounded to a tenth of a kilowatt-hour, and energy is conserved month by month (a test adds every flow back to generation and demand). The golden test lands Rob's roof at a saving within the review's £1,250 to £1,500 band and a payback of 10 to 13 years. Found: the review's rough split of generation (2,800 used, 2,350 exported) is not what the monthly model gives; the model uses every winter unit and stores more in spring and autumn, so it lands higher on use and lower on export. The saving, which is what the household decides on, agrees. Recorded in the test.
- Case 8 holds: every grant and every tariff carries a `seen` date; tests fail at 180 days.
- Case 9 holds by construction: the same `AddUpstreamClient` helper, with `RemoveAllLoggers()`, registers PVGIS and Google.
- Found and fixed: `£`, `°` and `²` were leaving Razor as numeric entities. The encoder now lets Latin-1 through, which carries no HTML-significant character, so the pages read as written and the tests can say what the page says.
- Found and fixed: a `const decimal` on the tariff and estimator left an unrunnable static constructor behind, as the M2 gotcha warned. Properties now.
- Residual: the Google panel shows the segments as a table rather than over aerial imagery, which would need Google's map script and its origins in the CSP. Recorded as a later refinement in the handover; the figures the estimator needs are all present.
- Residual: the model is a monthly one. Half-hourly tariffs such as Agile are not represented; the four strategies cover the common cases. Accepted.
