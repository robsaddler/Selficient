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

## Still to answer (stage 2)

- The closer circle: that a member outside it cannot read the first name, cannot tell who is in whose circle, and cannot tell how many.
- A hostile member: a record crafted to break another member's page (markup in a name, an enormous list, a public key that is not one).
- A lost device: what a household is told, and that the key is not put anywhere the server can read to "help".
