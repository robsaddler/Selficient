# Threat model: rotation calendar and `.ics` export (M3)

*30 October 2026. Written before the code.*

## What it is

The plan page lists the household's recurring upkeep (water rotation at the clock changes, battery top-ups, alarm tests, decking clearance, insurance review) and offers an "Add to your calendar" link. The link is a GET to `/plan/calendar.ics` carrying only two booleans (`decking`, `garden`), and returns a `text/calendar` attachment built entirely from static copy and computed dates. No personal data is involved.

## Abuse and failure cases

| # | Case | Control | Test |
|---|---|---|---|
| 1 | Header or line injection into the `.ics` (CR/LF in text) | Every text field is static copy; the writer escapes `\`, `;`, `,` and newlines and folds lines at 75 octets per RFC 5545 regardless | `Text_is_escaped_and_lines_are_folded` |
| 2 | Query parameters used to steer anything but the two booleans | Model-bound to `bool` with defaults; anything else is ignored; no reflection of input into output | `Unknown_or_malformed_query_values_are_ignored` |
| 3 | Content-type or filename tricks | Fixed `text/calendar; charset=utf-8` and fixed filename `selficient-calendar.ics`; `X-Content-Type-Options: nosniff` already global | `The_download_has_a_fixed_type_and_name` |
| 4 | Wrong dates (clock changes computed wrongly, leap years) | Pure date functions with tests for the known 2026 and 2027 clock changes and a leap year | `Clock_changes_are_the_last_sundays_of_march_and_october` |
| 5 | Calendar apps rejecting the file | Required properties present (VERSION, PRODID, UID, DTSTAMP, DTSTART), CRLF endings, UTC `DTSTAMP`; validated by a structural test | `The_calendar_has_every_required_property` |
| 6 | Abuse as a free download endpoint | API rate-limit policy applies; the body is under 8 KB and computed, never stored | `Rate_limit_applies_to_the_calendar` |
| 7 | Timestamps leaking server time zone | All times rendered in UTC or as all-day dates | `Dates_are_all_day_and_stamps_are_utc` |

## Outcomes (adversarial pass, 30 Oct 2026)

- All seven cases have passing tests. The writer escapes and folds per RFC 5545 and never splits a multi-byte character; the endpoint binds two booleans and ignores everything else; the API rate-limit policy applies; dates are all-day and the stamp is UTC.
- No security review findings specific to this feature beyond the M2 logging issue, which also covered this endpoint's category overrides.
