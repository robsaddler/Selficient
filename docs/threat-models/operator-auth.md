# Threat model: the operator's sign-in

*M20. Replaces the single shared key that M18 shipped and M19 left alone.*

## What this protects

The reviewer at `/admin/catalogue` is the only authenticated surface Selficient has. From it an operator can hide and show offers, which changes what every household is shown and which shops earn a commission, and can start a verification run that opens every link in the catalogue from the server. There is no household data behind it: nothing a reader enters ever reaches the server, so the asset here is the integrity of the catalogue and the commercial surface, not anyone's personal information.

## What was wrong before

One shared secret in an environment variable, compared in constant time, exchanged for a signed cookie. Everything below that a reasonable operator surface has, it did not have:

- **One factor, and a shared one.** Anybody holding the string is the operator. There is nothing to prove possession of a device.
- **No identity.** Every hand taken was recorded as "what and when", because there was nobody to name. The audit trail could not answer "who hid this offer", which is the first question after a mistake.
- **No lockout.** The rate limiter capped attempts per minute per address; it did not lock an account, because there was no account.
- **No rotation.** Changing the key meant an environment variable and a restart, and every past holder losing access at once, so in practice it would not be changed.
- **Low entropy in practice.** A twenty-character minimum on a string a person types and reuses is far weaker than a hashed password plus a time-based code, and nothing stopped it being written down in a deploy script.

## Decisions

| # | Decision | Why |
|---|---|---|
| 1 | Operator accounts live in SQL Server through Dapper, alongside the rest of the platform | Rob's call, 31 Oct. No external identity provider, so the reviewer keeps working when the network does not, and nothing about the operators leaves the VPS |
| 2 | Passwords are stored as a versioned PBKDF2-HMAC-SHA512 hash, 210,000 iterations, 16-byte salt, 64-byte output | The BCL's own KDF, no supply-chain addition, above ASP.NET Core Identity's own default of 100,000. The format carries its version and parameters, so the algorithm can be raised or swapped for Argon2id later and every password is rehashed on the next correct sign-in without a reset |
| 3 | A second factor is required, not offered: a time-based code (RFC 6238, SHA-1, six digits, thirty-second step) from an authenticator app | Possession of a device, so a stolen or guessed password alone is not a sign-in. SHA-1 here is the RFC's own construction inside an HMAC and is not a collision question; every authenticator app implements it |
| 4 | The TOTP secret and the recovery codes are encrypted at rest with the Data Protection key ring already in the database | A database backup read by someone else must not hand over the second factor as well as the hashes |
| 5 | Ten single-use recovery codes, shown once, stored as SHA-256 hashes | A lost phone must not mean a lost platform. The codes are 128 bits of randomness, so a fast hash is right and a slow KDF would buy nothing |
| 6 | Lockout is per account and grows: the fifth consecutive failure locks for a minute, and each failure after that doubles the wait to a cap of an hour | Slow enough to make guessing pointless, short enough that a tired operator is not locked out of their own platform for a day |
| 7 | Every account carries a security stamp, and it changes whenever the password, the second factor or the enabled flag changes | Every signed-in session on every device is revoked by that change. The cookie carries the stamp and it is checked against the store on every request |
| 8 | The sign-in cookie is HttpOnly, Secure, SameSite=Strict, scoped to `/admin`, with a sliding window inside an absolute lifetime | A cookie that cannot be read by script, cannot be sent from another site, and cannot outlive the day it was issued in |
| 9 | Every attempt is recorded with its outcome, and every hand on the reviewer now names the operator who took it | The audit question "who" is answerable at last |
| 10 | The first account is created at start-up from `Operator__BootstrapUsername` and `Operator__BootstrapPassword` when the table is empty, and that account must change its password and enrol a second factor before it can do anything | A new VPS needs a way in that is not a password in the repository, and a bootstrap credential that stays valid is a backdoor |
| 11 | Every attempt keeps the address and the user agent it came from, and every hand keeps the address | The "no personal data" rule is a promise to the households who read the site, not a constraint on the back office. Operators are staff, and a trail that cannot say where a run of refusals came from cannot be investigated. Corrected the same night it shipped, after Rob pointed out the rule was being read as covering everyone |

## Cases

Each is a test. The test name is in the right-hand column.

| # | Case | Expected | Test |
|---|---|---|---|
| 1 | A correct username and password with no code | The password step passes, the sign-in is not complete, and nothing on the reviewer is reachable | `A_password_alone_is_not_a_sign_in` |
| 2 | A correct username, wrong password | Refused with the same sentence and the same timing as an unknown username | `A_wrong_password_and_an_unknown_name_answer_alike` |
| 3 | An unknown username | Refused; a hash is still computed so the answer takes as long as a real one | `A_wrong_password_and_an_unknown_name_answer_alike` |
| 4 | Five consecutive wrong passwords | The account locks, and the correct password during the lock is still refused | `Five_wrong_passwords_lock_the_account_and_the_right_one_does_not_open_it` |
| 5 | Repeated lockouts | Each lock is twice the last, to a cap of an hour | `Each_lockout_is_twice_the_last_up_to_an_hour` |
| 6 | A correct sign-in after a lock has passed | Opens, and the failure count is back to nothing | `A_lock_that_has_run_out_lets_the_right_password_through_and_forgets_the_failures` |
| 7 | A code from the previous or next thirty-second step | Accepted, because clocks drift | `A_code_one_step_either_side_is_accepted` |
| 8 | A code two steps away | Refused | `A_code_two_steps_away_is_refused` |
| 9 | The same code used twice | The second use is refused, even inside its window | `A_code_cannot_be_used_twice` |
| 10 | The RFC 6238 published test vectors | Every one produces the RFC's own code | `The_published_test_vectors_produce_the_published_codes` |
| 11 | A recovery code | Signs in once and never again | `A_recovery_code_works_once` |
| 12 | A password change | Every other session is signed out at once | `Changing_the_password_signs_every_other_session_out` |
| 13 | A cookie carrying a stale security stamp | Refused on the next request, not at the next expiry | `A_cookie_from_before_a_password_change_is_refused_on_the_next_request` |
| 14 | A disabled account | Refused, and its sessions end | `A_disabled_account_cannot_sign_in_and_its_sessions_end` |
| 15 | A password below the policy | Refused at change time with the sentence saying what is wrong | `A_password_that_breaks_the_policy_is_refused_with_a_sentence` |
| 16 | The bootstrap account before it has changed its password | Can reach only the account page; the reviewer refuses it | `The_bootstrap_account_must_change_its_password_and_enrol_before_it_can_review` |
| 17 | Bootstrap settings when operators already exist | Ignored; no second account appears | `Bootstrap_does_nothing_once_an_operator_exists` |
| 18 | The hash of a password stored with fewer iterations than the policy | Verified, then silently rehashed to the current policy | `An_old_hash_is_verified_and_quietly_brought_up_to_the_current_policy` |
| 19 | The TOTP secret read straight out of the database | Ciphertext, not a secret anybody could enrol with | `The_second_factor_secret_is_ciphertext_in_the_table` |
| 20 | A sign-in form posted from another origin | Refused by antiforgery | `A_sign_in_posted_from_another_origin_is_refused` |
| 21 | The sign-in page itself | Answers no faster for a real username than a false one, within the measured tolerance | `A_wrong_password_and_an_unknown_name_answer_alike` |
| 22 | Every attempt, right or wrong | Leaves a row with its outcome, and the password never appears in it or in any log | `Every_attempt_is_recorded_and_no_password_is_ever_written` |
| 23 | A hand on the reviewer | Records the operator's username beside what was done | `A_hand_names_the_operator_who_took_it` |
| 24 | The reviewer with no operator configured and no bootstrap | The pages do not exist, as before | `With_no_operator_the_reviewer_is_not_there` |
| 25 | An attempt from a browser | Keeps the address and the agent; one with no request behind it keeps neither rather than inventing them | `An_operators_trail_says_where_the_attempt_came_from` |
| 26 | The enrolment code | Drawn into the response as an SVG path, never fetched from an endpoint, because the address under it carries the seed | `The_path_draws_exactly_the_dark_modules_of_the_code`, `Enrolment_arms_the_second_factor_and_shows_the_recovery_codes_once` |
