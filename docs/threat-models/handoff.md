# Threat model: sending a copy to another device (M22)

*12 November 2026. The second milestone of `docs/design/accounts-and-sharing.md`: an encrypted, one-use, ten-minute relay that carries a plan from one browser to another, and the page that receives it. The first table in the platform database that holds anything a household made, so it is modelled on the assumption that the database is read by someone who should not.*

## What it is

- The sender's browser makes a fresh AES-256-GCM key, encrypts the plan's snapshot with it and posts the ciphertext to `POST /api/handoff`. The server keeps it under a random 128-bit id for ten minutes and answers the id.
- The sender shows a QR code and a link, `/receive#<id>.<key>`. The code is drawn in the browser (`qr.js`), because drawing it on the server would mean sending the server the key.
- The receiver's page reads the fragment, removes it from the address bar, fetches `GET /api/handoff/<id>` once (the row is deleted as it is read), decrypts, checks the shape with the same guard the file import uses, shows what arrived and writes nothing until the person presses a button.
- The sender polls `GET /api/handoff/<id>/waiting` to learn that the copy was collected.

## Assets

- The plan in transit: a household's answers, its postcode, what it owns and the pin on its house.
- The plan already on the receiving device, which a careless or hostile copy could overwrite.
- The promise on the privacy page that the server holds a copy it cannot read.
- The database's disk and the server's memory, which a flood of posts could fill.

## Trust boundaries

The fragment never leaves the two browsers. The server is trusted to delete on read and on expiry, and is not trusted with the plaintext. The receiving browser trusts nothing it decrypts until the guard has passed it.

## Cases

| # | Threat | Answer | Pinned by |
|---|---|---|---|
| 1 | The server, its database, a backup or a log reads the plan | The key is made in the browser and travels only in the fragment, which no browser sends. The table holds the id, the ciphertext and two times, and nothing about who sent it: no address, no agent, no account | `A_copy_goes_in_and_comes_out_once` (the columns), `A_plan_is_sent_to_another_device_which_takes_it_and_the_server_never_sees_the_key` (the posted bytes do not contain the postcode, and the key is absent from every request) |
| 2 | A guessed id | 128 bits from the system's generator, compared as a primary key; collection is rate limited per address; and a guessed id yields ciphertext without its key | `An_id_that_is_not_one_is_never_looked_up`, `Leaving_and_collecting_are_rate_limited_on_a_policy_of_their_own` |
| 3 | A replayed link, or one read from browser history, a chat log or a screenshot after use | The row is deleted in the statement that reads it (`DELETE … OUTPUT`), so a second fetch is a 404 whatever the gap. The receiving page takes the fragment out of the address bar and its history entry as soon as it has read it | `A_copy_goes_in_and_comes_out_once`, `Two_collectors_racing_get_one_copy_between_them`, the journey (the address after arrival carries no fragment; a second visit is told the link is used) |
| 4 | A link used after ten minutes | The read is `WHERE expires_at > now`, so an expired row is never returned even before the sweeper has removed it; the sweeper deletes it within the minute | `A_copy_that_has_run_out_is_not_handed_over_and_is_swept` |
| 5 | An oversized or empty body | The body is read to a cap of 64 KB plus one byte and refused with 413 past it; anything shorter than a nonce, a tag and one byte is refused with 400. The cap is on what is read, not on the `Content-Length` a client claims | `A_body_over_the_cap_is_refused_however_it_is_declared`, `A_body_too_short_to_be_a_copy_is_refused` |
| 6 | A flood of posts fills the table | A limiter policy of its own, per address, far below the general API's; a ceiling on rows waiting at once, past which a post is a 503 with a sentence; ten-minute expiry; the sweeper | `Leaving_and_collecting_are_rate_limited_on_a_policy_of_their_own`, `The_desk_refuses_when_it_is_full`, `A_full_desk_is_a_503_with_a_sentence` |
| 7 | The relay is used as free anonymous storage for something else | 64 KB, ten minutes, one read, no listing and no way to choose an id. It is a poor file host by construction | The limits above |
| 8 | A hostile link: someone sends a victim `/receive#…` to plant a plan on their device | Nothing is written on arrival. The page says what arrived (people, district, ticks, the kind of device, how long ago) and writes only on a press. If the device already holds a plan the choice is replace or keep, in words, never a merge and never a default | `A_plan_is_sent_to_another_device_which_takes_it…`, `A_device_that_already_has_a_plan_chooses_to_keep_it_or_replace_it…` |
| 9 | A decrypted payload that is not a plan, or is a plan built to break the page | The envelope's version, time and device kind are checked, then `isSnapshot`, the guard the file import has used since M3: flat string map, kebab-case ids, bounded lists, a pin in the United Kingdom. The device kind is one of a fixed list and is shown as words the page holds, never as the sender's text. Everything shown is set as text, never as markup | `A_link_that_is_wrong_broken_or_hostile_changes_nothing_and_says_why` |
| 10 | A decompression bomb: a tiny ciphertext that inflates to gigabytes in the receiver's tab | Inflation stops at one megabyte and the copy is refused | The same journey (two megabytes in two kilobytes) |
| 11 | The wrong key, a truncated link, a flipped bit | AES-GCM authenticates: any change to the ciphertext or the key fails decryption, which the page reports as an unreadable copy. A link without both parts is reported as incomplete before any request is made | The same journey |
| 12 | The fragment leaks through a referrer, a third-party script or the server's log | The receive page loads nothing from another origin (the site-wide CSP allows none), answers with `Referrer-Policy: no-referrer`, and a fragment is not part of a referrer or a request line in any case. The request log's line for the fetch carries the id, which is spent by then | `The_receive_page_sends_no_referrer_and_is_kept_out_of_search` |
| 13 | The "waiting" probe leaks whether an id exists | It needs the 128-bit id, answers a boolean, and deletes nothing. Knowing a copy is waiting is no use without the key | `The_sender_can_ask_whether_the_copy_is_still_waiting` |
| 14 | A cross-site page posts to the relay in a visitor's name | There is no session and no cookie on these endpoints, so there is no authority to borrow; the post would store bytes the attacker chose under an id only the attacker learns | By construction |
| 15 | The receiver reloads between fetching and pressing the button and loses the only copy | The decrypted copy waits in the tab's session storage, under the id, until the person presses a button; it is removed then, and with the tab | The journey (a reload before the press still shows the copy) |
| 16 | The QR code is unreadable, so the feature fails for the person it was built for | The encoder is checked against an independent decoder for every length a link can be, and the code is drawn dark on white in both themes | `A_code_drawn_in_the_browser_reads_back_as_the_text_it_was_given` |
| 17 | The sender believes the two devices are now linked | The sentence at the moment of sending and the sentence on arrival both say that changes will not follow. There is no device list and no second send without a new press | The journey (both sentences) |

## Accepted

- The server learns that some browser at some address sent or collected a copy, and when, from the request log every site keeps for a fortnight. It does not learn whose, or what.
- A person who photographs the code within ten minutes and opens it first gets the copy. That is what the code is for; the sentence beside it says it works once.
