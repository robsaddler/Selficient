# Threat model: street groups

*13 November 2026. The design is `docs/design/street-groups.md`. This document grows with the stages; stage 1, the server, is done.*

## What is being protected

What a home has that a thief or a bully would like to know: that it has a generator, fuel, a warm room, a fridge that runs. And who lives there. The feature exists to tell neighbours some of that, so the whole of the design is about telling only the neighbours a household chose, and nobody else: not the server, not a stranger with a guessed address, not the rest of the street where the household said the closer circle only.

## Stage 1: the server

The server never holds a key. It holds membership and blobs. Its job is to let in only who should be let in, and to hand a group's blobs to its members and nobody else.

| # | Threat | Answer | Pinned by |
|---|---|---|---|
| 1 | **A stranger reads a group** by guessing its address | A group's id is 122 random bits, and knowing it is not enough: every read is answered only for an account that is a member, and "no such group" and "not a member" are the same sentence and the same status | `A_group_is_answered_only_to_its_members_and_a_stranger_cannot_tell_it_exists` |
| 2 | **A stranger joins** | Joining needs the invitation's pass, 256 random bits; the server holds its SHA-256 and compares in fixed time. A wrong pass and a missing group are the same sentence. Joining is rate limited with the other account writes | `Joining_takes_the_pass_from_the_invitation_and_a_wrong_one_opens_nothing` |
| 3 | **The server, or whoever takes its database, reads what homes offer** | It has no key: the group key and the pass travel in the fragment of the link. The verifier is a hash of the pass, which is not the key and does not lead to it | The schema; the endpoints take no key; stage 2's tests |
| 4 | **A removed home comes back** with the old link and a new account | Removing a home sets a new pass in the same statement, so the old link opens nothing | `The_longest_standing_member_removes_a_home_and_the_old_invitation_dies_with_it` |
| 5 | **Somebody who is not the starter removes a home**, or the starter is removed | Only the longest-standing member may remove, and nobody can remove themselves that way; they leave | The same test |
| 6 | **A group is orphaned**: the starter leaves or deletes their account and nobody can remove anyone | The one who may remove is whoever has been in longest, worked out when asked; there is no stored role to lose | `When_the_starter_leaves_the_longest_standing_member_may_remove_and_an_empty_group_is_swept` |
| 7 | **An account's deletion leaves its record behind** | The membership row cascades from the account; an empty group is swept | The same test; the store's test of deletion |
| 8 | **A request from another site** joins a group or removes a home while the household is signed in | Every write needs the header only our own script can add and, where the browser says, a same-origin fetch; the session cookie is same-site | `A_write_that_did_not_come_from_our_pages_changes_nothing` |
| 9 | **Storage is filled**: enormous records, thousands of groups, a group of thousands | A record is 24 KB at most and must be base64; the group's name blob 2 KB; a hundred homes to a group; five groups to an account. Each refusal is a sentence | `What_is_too_large_too_many_or_not_ciphertext_is_refused_with_a_sentence` |
| 10 | **One member writes another's record** | A write is to the row the session's account holds; nothing in the request names a member | `A_member_writes_only_its_own_record` |
| 11 | **The account's number, or an email address, reaches another member** | Members are known to each other by a random member id made for that group; the answer carries no account number and no address | `A_group_is_answered_only_to_its_members…` reads the answer for both |
| 12 | **The server learns the street's name or a house number from what it is sent** | It is sent only base64 that the browser encrypted; the guard checks that it is base64 and nothing else, so a client that sent plain words is refused | `What_is_too_large…` |

Accepted: the server knows which accounts are in a group together, and so which email addresses are neighbours. It has to, to decide who may read. Rob agreed the privacy rule's extension for this on 13 November.

Accepted: a removed member who kept the group key could read the group if the server gave it the blobs. The server does not. Defending against the server acting with a removed member would mean changing the key on every removal, which locks out every member who was away; it is not worth that.

## Stage 2: the browser

The journeys are in `tests/Selficient.E2E.Tests/Street.cs`, each household in a browser of its own.

| # | What could go wrong | What stops it | Test |
|---|---|---|---|
| 13 | **A member outside a home's closer circle reads its first name** or the offers made to the circle | They are sealed with a key made fresh at every save and wrapped only for the chosen homes, with a key each pair of homes derives (ECDH P-256, then HKDF salted with the group). A home outside has nothing that opens a wrap | `Neighbours_share_what_each_chose_with_whom_each_chose_and_the_server_can_read_none_of_it`: Sam's page has no "Rob" on it and one offer; Doris's has the name and both |
| 14 | **A member, or the server, learns who is in whose circle, or how many** | The wraps carry no names, are shuffled, and are padded with random decoys to at least eight and then to the next eight | The record's shape in `street-crypto.js`; the journey reads what the server holds, unwrapped as far as it goes without a key, and finds no name, number or offer |
| 15 | **The server reads the street's name, a house number, a first name or an offer** | Everything is sealed in the browser with AES-GCM under the group key, which is only ever in the link's fragment and the device's own storage | The same journey: the answer of `/api/street/{id}` is searched, envelope inside envelope |
| 16 | **A hostile member's record breaks another member's page**: markup in a house number, offers that are not offers, a hundred and fifty wraps, a public key that is not one, a record that is noise | Every value read from a record is checked: text is one line, cut to its length and set as text, never markup; offers are kept only if they are on the closed list, once each; wraps are capped; a bad key or a record that does not open makes the home "could not be read" and nothing else. The page has no inline script allowance for markup to use in any case | `A_neighbour_whose_browser_lies_cannot_break_another_home_s_page` |
| 17 | **A member makes itself invisible** by writing nonsense, so that it stays in the group unseen | A home that cannot be read is still drawn and can still be removed | The same test removes it |
| 18 | **The invitation leaks through a return address, a referrer or a cache** | The page is `no-store` and `Referrer-Policy: no-referrer`; a guest's invitation waits in the tab's session storage, not in the sign-in's address; the code is drawn in the browser by `qr.js` | `StreetPagesTests`; the journey signs Doris in on her way to the invitation |
| 19 | **A removed home keeps a working invitation**, or the members left behind keep a dead one | The starter's browser sets a new pass with the removal and seals it into the group's `about`, which every member reads | The journey: Sam's old link opens nothing; Doris's page shows the new one without being sent it |
| 20 | **A second device becomes a second home**, or cannot see what the first set | What a home's devices need is kept sealed beside its membership and answered only to its account | The journey: Rob's phone opens the invitation and has his name, his circle and his offers |
| 21 | **A lost device**: the household is locked out, or the key is put where the server can read it to "help" | A device without the key says so in a sentence and says what to do: open the invitation again. The key is in the device's storage and nowhere else (decision 160) | The journey: Rob's phone before it has opened the invitation; a group address this device never heard of |

## Stage 3: the plan and the group

| # | What could go wrong | What stops it | Test |
|---|---|---|---|
| 22 | **The server learns what a home has** by being asked which offers it could make | It is never asked. The page carries the same list of signs to everyone and the browser holds it against its own store | `The_plan_leads_to_the_street_and_the_street_says_what_the_plan_holds_without_choosing_for_anyone`; `StreetPagesTests` reads the list off the page |
| 23 | **A household shares something it did not choose**, because its plan held it | A sign is a sentence. No offer is set by it, and nothing is published until "Save what I share" | The same journey: every choice is still nobody and the street has nothing between it |
| 24 | **A server with no accounts offers a group** that cannot be joined | `/street` answers with the accounts-unavailable page and the plan draws no way in | `A_server_that_offers_no_accounts_offers_no_street_groups_and_the_plan_does_not_point_to_them` |

Accepted: every member holds the group key, so any member can read what is shared with the whole group and can write a record for itself that says anything. That is what a member is. What a member cannot do is read a closer circle it is not in, write another home's record, or make another home's page do anything.

Accepted: a home in a closer circle can tell it is in it (it sees the first name). It cannot tell who else is.
