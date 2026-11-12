# Threat model: household accounts (M23 to M25)

*12 November 2026. The account a household may choose to make: an email address and, once sync lands, a copy of its plan. Sign-in is by a six-digit code sent to the address (M23) and by passkey (M25); the plan is kept in step across devices (M24). This is the first personal data the platform holds about a reader, under the rule Rob changed the same day: nothing about a reader is kept unless they make an account, and then only their email address and their plan, which they can delete at any time.*

*Sections 1 to 4 are M23 and are written before its code. Sections 5 and 6 are filled in before M24 and M25.*

## 1. What it is (M23)

- `/account/sign-in` takes an address and sends a code. `/account/code` takes the code. The same two pages make an account and sign in to one, and say the same sentences either way.
- A pending sign-in is bound to the browser that asked: a cookie (`selficient.signin`, HttpOnly, Secure, SameSite=Strict, path `/account`, ten minutes) holds the address and a random 256-bit value, encrypted and signed by the Data Protection key ring. The database row holds a hash of that value, a hash of the address and a keyed hash of the code, and nothing readable.
- A signed-in household carries `selficient.household`: HttpOnly, Secure, SameSite=Lax, ninety days sliding (decision 60), with the account's number and its security stamp. A second cookie, `selficient.signed-in`, readable by script and holding only `1`, tells the page to ask who is signed in, so a guest's browser never asks.
- `/account/sign-out` and `/account/delete` are forms, each one confirmation. Deleting removes the address, the plan, the passkeys and every sign-in at once; the copy in the browser stays.
- Mail goes through `IEmailSender`: SMTP to Papercut on the local machine, the Postmark API in production (decision 67).

## 2. Assets

- The address, which identifies a person.
- The session: whoever holds it reads and (from M24) writes that household's plan.
- The plan: answers about a home, its postcode, what is in its cupboards, where the house is.
- The promise to guests, who must lose nothing by accounts existing: no cookie they did not have, no request they did not make, nothing kept.
- The mail reputation of the sending domain, which a flood of codes to strangers would burn.

## 3. Cases (M23)

| # | Threat | Answer | Pinned by |
|---|---|---|---|
| 1 | **Enumeration**: the pages reveal which addresses have accounts | There is no "sign up" and no "that address is not known". Any well-formed address is sent a code and shown the same sentence; the account is made, if it does not exist, only when the code is proved. Timing is the same because the work is the same | `A_known_address_and_a_new_one_are_told_the_same_thing` |
| 2 | **Code guessing**: a million codes, five tries | Every attempt, right or wrong, increments the row's counter in one statement before the code is compared, so racing attempts cannot share a try; the sixth is refused whatever it says. Ten minutes. One use. Five tries of a million is one in two hundred thousand per code, and codes are limited per address (case 5) | `Five_wrong_codes_burn_the_code_and_the_right_one_is_then_refused`, `Attempts_are_counted_before_the_code_is_read` (store), `Racing_guesses_share_five_tries_between_them` |
| 3 | **A code used from a different browser**: read over a shoulder, from a forwarded email, from a mailbox the attacker can see | The code is useless without the binding cookie of the browser that asked. The stored hash of the code is keyed by the cookie's value, so the row cannot even be checked without it | `A_code_is_refused_in_a_browser_that_did_not_ask_for_it` |
| 4 | **The database is read**: a backup, an injection elsewhere | The codes table holds hashes only: no address, no code. The code's hash is keyed by a value that is only in the asking browser, so a six-digit space cannot be searched offline. Accounts hold the address, which is the one thing the feature needs | `A_code_row_holds_nothing_that_can_be_read` |
| 5 | **A flood of mail to a victim**, or to thousands of strangers through us | Per destination: three codes in fifteen minutes and six in an hour, counted on the address's hash, refused with a sentence that says so. Per sender: a limiter policy of its own on both forms. A malformed address is refused before anything is sent. The message says what to do if you did not ask | `An_address_is_sent_three_codes_in_fifteen_minutes_and_no_more`, `The_sign_in_forms_are_rate_limited_on_a_policy_of_their_own`, `An_address_that_is_not_one_is_refused_before_anything_is_sent` |
| 6 | **Sign-in CSRF**: an attacker signs the victim's browser in to the attacker's account, so the victim's plan syncs to it | Both forms carry antiforgery tokens. Beyond that, a code only works with the binding cookie, which is set only in answer to a same-site post and is SameSite=Strict, so an attacker cannot plant a pending sign-in for their own address in someone else's browser | `The_forms_refuse_a_post_without_a_token`, case 3's test |
| 7 | **Session fixation** | Signing in always issues a new cookie from the server; nothing a visitor arrives with is promoted. The binding cookie is deleted on success | `The_session_cookie_is_locked_down_and_signing_in_sets_a_fresh_session_and_clears_the_pending_one` |
| 8 | **A stolen session cookie** | HttpOnly, Secure, SameSite=Lax; the CSP allows no inline script and no other origin, so there is nothing to read it with. The stamp in it is checked against the account on every request that uses it, so deleting the account ends every session at the next request. Ninety days is Rob's decision (60); the trade is written there | The same test, `Deleting_the_account_ends_every_session_and_removes_every_row`, `A_session_that_ended_elsewhere_reads_as_a_guest_at_the_next_page` (E2E) |
| 9 | **The household's session reaches the back office, or an operator's reaches a household's pages** | Two schemes, two cookies, two tables. The operator's cookie is scoped to `/admin`; the household policy names its own scheme and the operator policy its own. Neither identity satisfies the other's policy | `A_household_is_not_an_operator_and_an_operator_is_not_a_household` |
| 10 | **Cross-site request forgery against sign out and delete** | Both are posts with antiforgery tokens; a visit by address shows the question and changes nothing. The session cookie is Lax, so a cross-site post does not carry it at all | `Sign_out_and_delete_are_posts_with_a_token_and_a_visit_by_address_changes_nothing` |
| 11 | **A guest pays for accounts existing** | A guest's page is byte-for-byte what it was, apart from words that mention accounts. The page asks "who is signed in?" only when the hint cookie exists, which only signing in sets. No account cookie is set by any page a guest can reach without asking for a code | `A_guest_is_never_asked_who_they_are_and_is_offered_an_account_only_as_a_third_way_to_keep_a_plan` (E2E: no request to `/api/account`, no cookie but the form's), `Asking_for_a_code_is_the_first_thing_that_sets_an_account_cookie` |
| 12 | **The address is logged** | The request log's line holds the path and the status, never a form body. The mail adapters log the failure and the provider's reason, never the recipient | `A_failed_send_is_logged_without_the_address` |
| 13 | **The mailer is down** | The person is told the code could not be sent and nothing is stored that counts against the address | `A_code_that_could_not_be_sent_is_said_so_and_not_counted` |
| 14 | **Accounts on a server with no mailer** | With no provider configured the sign-in page says accounts are not available here, the panel does not offer one, and nothing else changes | `A_server_with_no_mailer_offers_no_account` |
| 15 | **Header injection through the address** | The address is validated to one mailbox with no control characters before it reaches an adapter; the Postmark adapter sends JSON, and `MailAddress` refuses what SMTP would misread | `An_address_that_is_not_one_is_refused_before_anything_is_sent` |
| 16 | **Open redirect after sign-in** | There is no return address. Signing in lands on the plan | By construction |
| 17 | **A sender that keeps codes in memory, for the tests to read, runs in production** | There is none in the product. The recorder lives in the test kit and is put in place by the test host; the providers the options accept are none, SMTP and Postmark | `The_provider_is_one_of_three_and_defaults_to_papercut_on_a_development_machine_only`, `Mail_settings_that_cannot_work_stop_the_server_starting` |
| 18 | **A pending cookie from before a key ring change, or one tampered with** | It fails to decrypt and reads as no pending sign-in: the code page sends the visitor back to ask again | `A_pending_cookie_that_does_not_open_is_no_pending_sign_in` |

## 4. Accepted (M23)

- Whoever controls the mailbox controls the account. That is what "just an email address, no password" means, and it is the recovery path that makes a lost phone not a lost plan.
- An address that asks for a code and never uses it is remembered as a hash for one hour, to count codes against it, and then forgotten.
- Postmark processes the address and the code in transit, as any mail provider would. The privacy page names it.

## 5. Sync (M24)

*12 November 2026, before M24's code.*

### What it is

- The account keeps one copy of the plan: the same snapshot the browser exports, with the time its household details last changed. `GET /api/account/plan` answers it with a version number; `PUT /api/account/plan` replaces it, naming the version the writer last saw. A write based on a stale version is refused with 409 and the current copy.
- The copy is encrypted at rest by the Data Protection key ring, under a purpose of its own (decision 61). The server never computes anything from it: the rules engine runs on what a browser posts, as it always has.
- The browser does the work (`sync.js`). It keeps the last copy it agreed with the server (the base). When both sides have changed, it merges three ways: what a household said about each action is merged per action, with the base standing as the record of what was removed; its household details go to whichever side changed them, or to the newer when both did, and the page says so once. The first time a device with a plan meets an account with a different plan, the person chooses one. Never a silent merge of two unrelated plans.
- The plan stays in the browser. Offline, everything works as before, and the next run catches up.

### Cases

| # | Threat | Answer | Pinned by |
|---|---|---|---|
| 19 | **One account reads or writes another's plan** | The account number comes from the session and from nowhere else: no address, no body and no header names an account. There is no endpoint that takes one | `One_household_never_sees_or_writes_anothers_plan` |
| 20 | **A stale write clobbers a newer one** | The write is one statement, `UPDATE … WHERE version = @base`, and the first write is an insert under a range lock. Two devices writing at once: one is told 409 with the current copy, merges and writes again | `A_write_based_on_a_stale_version_is_refused_with_the_current_copy`, `Two_writers_racing_from_the_same_version_make_one_winner` (store) |
| 21 | **Cross-site request forgery against the write** | The session cookie is SameSite=Lax, which a cross-site PUT does not carry. Beyond that the endpoint requires a header a form cannot send (`X-Selficient-Sync`) and refuses any request whose `Sec-Fetch-Site` says it came from elsewhere | `A_write_without_the_sync_header_or_from_another_site_is_refused` |
| 22 | **The stored plan is read from the database** | It is ciphertext under the key ring, which is itself wrapped by the machine's DPAPI on the server. The table holds the account number, the ciphertext, a version and a time | `The_stored_plan_is_not_readable_in_the_database` |
| 23 | **A body that is not a plan**: free storage, or something built to break the page that reads it back | Capped at 64 KB on what arrives. The body must be JSON of exactly the snapshot's shape, by a guard on the server that mirrors the browser's `isSnapshot`: a flat map of short strings, bounded lists of kebab-case ids, figures in range, a pin in the United Kingdom. The browser runs its own guard again before anything it fetched is written to its store, and everything it shows is set as text | `What_is_not_a_plan_is_refused_before_it_is_stored`, the same test (over the cap), `A_copy_from_the_account_that_the_browser_would_not_accept_as_a_plan_changes_nothing` (E2E) |
| 24 | **A plan that passes the guard but is hostile to the rules engine** | The server never feeds a stored plan to the engine. A browser that restores it posts it like any other profile, and the engine has treated the profile as untrusted input since M2 | By construction |
| 25 | **A flood of writes** | A limiter policy of its own per address; one row per account, replaced in place, so an account's storage is bounded by the cap whatever it does | `Sync_is_rate_limited_on_a_policy_of_its_own` |
| 26 | **A key ring that is lost or replaced** makes every stored plan unreadable | The read answers the version with no body; the browser, which still has the plan, writes it back. Nothing is lost unless every device has also lost it | `A_stored_plan_the_key_ring_cannot_open_reads_as_none_and_is_replaced` |
| 27 | **Two unrelated plans are merged**: a person signs in on a friend's device, or on a device with an old plan | The first meeting of a device's plan and a different plan in the account is a choice between the two, with a summary of each. Merging happens only between copies that share a base | `A_second_device_with_its_own_plan_chooses_which_to_keep_and_the_two_are_never_mixed` (E2E) |
| 28 | **A removal comes back**: device A unticks, device B still has the tick and syncs | B's copy equals the base for that action, so A's change wins. A removal only loses to a change made after the same base on the other side | `Two_devices_stay_in_step_through_an_account_and_a_tick_taken_off_stays_off`, `Ticks_are_merged_action_by_action_against_what_the_two_last_agreed` (E2E) |
| 29 | **A shared device keeps a plan after sign-out** | Signing out asks, and unticking the box clears the plan and the sync record from the browser before the session ends | The account journey |
| 30 | **Sync leaks a guest's plan** | A guest's browser never runs it: sync starts only when the page knows somebody is signed in, which it learns only with the hint cookie | `A_guest_is_never_asked_who_they_are…` (no request to `/api/account` or `/api/account/plan`) |

### Accepted

- The server can read a stored plan: the key ring is its own. End-to-end encryption was set aside in the design because it would make the emailed code unable to recover a plan, which is the reason for the account.
- Clocks: "newer" for household details is by the two devices' own clocks. A device whose clock is badly wrong can win or lose a conflict it should not. The loser is told once, and can change the details back.

## 6. Passkeys (M25)

*12 November 2026, before M25's code.*

### What it is

- A signed-in household may add a passkey: a WebAuthn credential that lives in the device's own authenticator (fingerprint, face, screen lock). Registration is offered once after the first sign-in by code, and from the panel. The credential is discoverable and user verification is required, so the sign-in page's "Use a passkey" needs no address typed.
- The ceremonies are Fido2NetLib's, behind a port. The challenge and options for a ceremony in progress are kept in a cookie (`selficient.passkey`, HttpOnly, Secure, SameSite=Strict, path `/api`, five minutes), encrypted by the key ring, and removed when the ceremony ends either way.
- We store the credential id, its public key, its signature counter, a label made from the kind of device, and two dates. No private key ever leaves the authenticator; nothing biometric reaches us at all.
- The emailed code remains. It is the recovery path, which is why a lost phone is not a lost account.

### Cases

| # | Threat | Answer | Pinned by |
|---|---|---|---|
| 31 | **A credential registered to the wrong account** | Registration needs a session, and the account is the session's; the user handle in the options is that account's number. The verified credential is stored against the session's account and no other. The registration response is verified against the options this browser was given (the cookie), so a response made for another account's challenge fails | `A_passkey_is_added_to_the_account_that_is_signed_in_and_signs_it_in_afterwards`, `A_registration_answered_for_another_challenge_is_refused` |
| 32 | **The same credential registered twice, or to two accounts** | The credential id is unique in the table, checked in the ceremony and again by the index. The options name the account's existing credentials so the authenticator refuses to make a second for it | `A_credential_cannot_be_registered_twice` |
| 33 | **A replayed or forged assertion** | The challenge is 32 random bytes, used once, bound to this browser by the cookie and expiring in five minutes; the origin and the relying party in the signed data must be ours; the signature is checked against the stored public key | `An_assertion_for_another_challenge_origin_or_key_is_refused` |
| 34 | **A cloned authenticator** | The signature counter is stored and must go up when the authenticator keeps one. One that does not go up is refused | `A_signature_counter_that_goes_backwards_is_refused` |
| 35 | **User verification skipped** (a stolen, unlocked security key) | Required in the options and checked in the response. A response without it is refused | `An_assertion_without_user_verification_is_refused` |
| 36 | **Downgrade to the emailed code** | By design: the code is the recovery path. It is the reason the code has the stricter limits (five tries, per-address counts, bound to the asking browser). A passkey does not make an account weaker than it was without one | Section 3 |
| 37 | **A flood of ceremonies** | Beginning or finishing a sign-in by passkey spends the sign-in policy's allowance; an account holds ten passkeys at most | `Passkey_sign_in_is_rate_limited`, `An_account_holds_ten_passkeys_and_no_more` |
| 38 | **Removing someone else's passkey**, or the last way in | Removal names the passkey's number and the session's account in one statement. Removing every passkey leaves the emailed code, which always works | `A_household_removes_its_own_passkeys_and_nobody_elses` |
| 39 | **Cross-site requests to the passkey endpoints** | The same header the sync write requires, and the same check of where the request came from. The ceremony cookie is SameSite=Strict | `The_passkey_endpoints_refuse_what_did_not_come_from_our_pages` |
| 40 | **The label is a way to store markup** | It is chosen by the server from a fixed list, by the request's user agent. Nothing typed is stored | `A_passkey_is_labelled_by_the_kind_of_device_and_nothing_typed` |
| 41 | **Passkeys left behind when the account is deleted** | The rows cascade with the account. The authenticator's own copy cannot be removed from here and opens nothing afterwards: its credential id is unknown to us | `Deleting_the_account_removes_its_passkeys` |
| 42 | **Enumeration by passkey sign-in** | The options are the same for everyone: no credential list, because the credential is discoverable. Every refusal says one sentence | `Every_refused_passkey_sign_in_says_the_same_thing` |

### Accepted

- Attestation is not requested ("none"). We do not need to know the make of an authenticator, and asking would give a household a consent prompt for nothing.
- A synced passkey (iCloud Keychain, Google Password Manager) is as safe as that account. That is the user's own choice of authenticator.
