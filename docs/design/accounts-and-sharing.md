# Design: no account needed, send a copy, and an account for those who want one

*11 November 2026. Rob's brief: "We need to front and centre the platform can be used in no-account mode, share to another device and, for those that want it, create an account. I don't want us to end up in a really uncanny valley of non-account, quasi-account management that no one has ever seen a similar UX for. This needs super careful design." His decisions the same day: sign-in by emailed code and by passkey, both; accounts ship at launch.*

## 1. The problem in one paragraph

Most people arrive expecting to be asked for an account. Selficient does not ask, and says so nowhere prominent, so a visitor is left waiting for a sign-up that never comes and unsure whether their plan is being kept. Add a way to move a plan between devices and the risk gets worse: a product that links devices without an account is a thing nobody has seen, and people will guess wrongly about what it does.

## 2. The principle: invent nothing

Three patterns that everybody has already used, under the names they already know, and no fourth state.

| Pattern people know | Where they know it from | What it is here |
|---|---|---|
| Continue as guest | Any checkout | **Saved on this device.** The default. No account. |
| Move to a new phone by scanning a code | WhatsApp, a new iPhone | **Send a copy to another device.** One-off, no link left behind. |
| Create an account to track your order | The end of a guest checkout | **Create an account.** Optional. Keeps devices in step. |

**The rule that keeps it out of the valley:** a copy is a copy. It does not sync, and the screen says so at the moment it is made. Keeping devices in step is the one job of the account. Each rung has exactly one job, so nobody has to work out what state they are in.

There are only ever two identities on screen: **on this device** or **signed in**. There is no device list without an account, no pairing, no "linked" state.

## 3. Where it is said

### 3.1 The header chip (every page)

Where "Sign in" lives on every other site, Selficient shows a chip.

- Guest: a small device mark and **Saved on this device**.
- Signed in: a small person mark and **Signed in as rob@…**.
- Before a plan exists: **No account needed**.

Pressing it opens one panel (a native `<dialog>`, as the rest of the site uses) with three rows. Each is a heading, one sentence and one button.

| Row | Sentence | Button |
|---|---|---|
| On this device | Your plan lives in this browser and nowhere else. You do not need an account. | (none: it is the state) |
| Another device | Send a copy of your plan to your phone or another computer. Changes you make afterwards will not follow it. | Send a copy |
| An account | Keep every device in step and keep your plan if this one is lost. Just an email address, no password. | Create an account |

Under the rows, one quiet line: **Already have an account? Sign in.**

### 3.2 The landing page

- The start button reads **Start your plan** with **No account needed** set beside it, in the hero and in the closing band.
- The "Everything stays with you" strip gains one sentence: **If you want your plan on every device, you can make an account later. You never have to.**

### 3.3 The first plan

Once, the first time a plan is built on a device, a notice under the Days Ready figure: **Your plan is saved on this device. You do not need an account.** with two quiet links, **Send a copy to my phone** and **Create an account**. Dismissed for good by any press or by the next visit.

### 3.4 No nagging

The account is mentioned in exactly four places: the panel, the first-plan notice, the screen shown after a copy has been sent ("An account would keep both in step"), and the storage warning in 3.5. Nowhere else, and never as a blocker.

### 3.5 Telling the truth about guest storage

Safari on iPhone and iPad clears a site's storage after seven days without a visit unless the app has been added to the home screen. That is the one real weakness of guest mode and it is said plainly, on those browsers only, in the panel: **Safari clears a site's data after a week without a visit. Add Selficient to your home screen, or make an account, to keep your plan.** The app also asks the browser for persistent storage (`navigator.storage.persist()`) when a plan is first saved, which Chromium grants to installed apps and engaged sites.

## 4. Send a copy

### 4.1 What the person sees

1. **Send a copy.** The panel shows a QR code, the line "Point your other device's camera at this. It works for ten minutes, once.", and a **Copy the link instead** button for a device with no camera.
2. On the other device the link opens `/receive`, which shows what is arriving before anything is written: "A plan for 4 people in NP12, 9 things ticked, made on a Windows computer a minute ago." and one button, **Put this plan on this device**.
3. If that device already holds a plan: "This device already has a plan. Replace it with the one you sent, or keep the one that is here." Two buttons. Never a merge.
4. Back on the sender: "Sent. Your other device has its own copy now. Changes you make on one will not appear on the other. An account would keep both in step."

### 4.2 How it works

The plan is too big for a reliable QR code, so the code carries a key and a ticket, not the plan.

1. The sender's browser serialises the snapshot the export already makes (profile, ticks, house pin, dependencies, shortlist), compresses it, and encrypts it with a fresh AES-256-GCM key from Web Crypto.
2. It posts the ciphertext to `POST /api/handoff`. The server stores it under a random 128-bit id with a ten-minute expiry and answers the id.
3. The QR code and the link are `https://selficient.app/receive#<id>.<key>`. The fragment never reaches any server, so the server holds ciphertext it cannot read.
4. The receiver's page reads the fragment, fetches `GET /api/handoff/<id>` once (the row is deleted as it is read), decrypts in the browser, validates with the same `isSnapshot` guard the import uses, and shows the summary.

No typed short code is offered. A code short enough to type cannot carry a key strong enough to keep the promise, and a weaker second path is the kind of half-measure this design exists to avoid. The fallbacks are the link and the existing "Export as a file".

### 4.3 Limits and abuse

- Ciphertext capped at 64 KB; one use; ten minutes; rate limited per address with the existing limiter (its own policy).
- The table holds id, ciphertext, created and expiry. No address, no account, nothing about the sender. A sweeper removes expired rows; the nightly job logs the count.
- The privacy page gains a row: "Send a copy: an encrypted copy of your plan that we cannot read, for ten minutes."

## 5. Accounts

### 5.1 What an account is

An email address and a synced copy of the plan. Nothing else: no name, no password, no profile page. It exists to do two things, and the interface says only those two things: keep devices in step, and keep the plan if a device is lost.

### 5.2 Signing in

**No passwords at all.** Two methods, both at launch.

- **Emailed code.** Type the address, receive a six-digit code good for ten minutes, type it. The same screen serves sign-up and sign-in, and says the same sentence whether or not the address is known. Five wrong codes burn the code. The code is single use and bound to the browser that asked for it.
- **Passkey.** Offered straight after the first emailed-code sign-in ("Sign in with your fingerprint or face next time") and on the sign-in screen as **Use a passkey**. WebAuthn through Fido2NetLib, discoverable credentials, user verification required. The emailed code remains the recovery path, so a lost phone is never a lost account.

The operator accounts from M20 stay separate: different table, different cookie, different policy. A household account never reaches `/admin`, and an operator is not a household.

### 5.3 What the first sign-in does to the plan

The moment that most often goes wrong in products like this, so it is explicit.

- Device has a plan, account is new: "Your plan is now kept in your account." Nothing to choose.
- Device has no plan, account has one: the account's plan arrives. Nothing to choose.
- Both have one: the same two-button choice as 4.1 step 3, with a summary of each. Never a silent merge.

### 5.4 Sync

- The server stores the same snapshot shape, per account, with a version number. Whole-snapshot sync with optimistic concurrency: a write names the version it was based on.
- Ticks and the shortlist are sets, so a conflict on them is resolved as a set union with tombstones and never asks the person anything. A conflict on the profile (two devices changed the household facts) keeps the newer and says so once: "Your other device changed your household details. This device now shows those."
- The plan stays in the browser as well. Offline works exactly as it does today, and changes go up when the connection returns. The service worker is not involved in sync; the page does it.
- The server never computes anything from a stored plan. The rules engine still runs on the request, as now.

### 5.5 Leaving

- **Sign out** asks one thing: "Keep a copy of your plan on this device?" Yes is the default.
- **Delete my account** is in the panel, one confirmation, immediate, and removes the address, the plan, the passkeys and the sessions. The device copy stays. The privacy page says so.
- **Download my data** is the existing export.

### 5.6 What is stored, and the rule it changes

| Table | Holds |
|---|---|
| `household_accounts` | id, email (unique, case-insensitive), created, last seen, security stamp |
| `household_plans` | account id, snapshot JSON, version, updated |
| `household_passkeys` | account id, credential id, public key, sign count, label, created, last used |
| `household_signin_codes` | hashed code, email, browser binding, expiry, attempts |
| `handoffs` | id, ciphertext, created, expiry |

This changes a standing rule. `CLAUDE.md` says no personal data about readers on the server in v1. With accounts it becomes: **nothing about a reader is kept unless they make an account, and then only their email address and their plan, which they can delete at any time.** The privacy page gets a section headed "If you make an account" that says exactly that, and the "short version" rail gains a card. Guests keep the full promise, and the landing page goes on making it.

### 5.7 Email

Codes have to be sent by something. **Decision for Rob:** a transactional provider (Postmark or Amazon SES are the two worth considering; both have UK or EU regions) or SMTP from the VPS. The recommendation is a provider, because a sign-in code that lands in spam is a locked-out household, and deliverability from a fresh VPS address is poor. The key is an environment variable. Locally and in tests a fake mailer writes the code to the log and to a test hook.

## 6. Threat model headings

The full model goes in `docs/threat-models/household-accounts.md` and `docs/threat-models/handoff.md` before any code, as the working rules require. The cases it must cover:

- **Handoff:** a guessed id; a replayed link; a link read from browser history after use; an oversized or malformed ciphertext; a decrypted payload that is not a snapshot; a flood of posts; the fragment leaking through a referrer (the receive page sends none and loads nothing third-party).
- **Emailed code:** enumeration of addresses; code guessing; a code used from a different browser; a flood of emails to a victim's address (per-address and per-destination limits); a stolen session cookie.
- **Passkeys:** a credential registered to the wrong account; a cloned authenticator (sign count); downgrade to the emailed code, which is by design and is why the code path has the stricter limits.
- **Sync:** one account writing another's plan; a stale write clobbering a newer one; a snapshot that passes the guard but is hostile to the rules engine (the engine already treats the profile as untrusted input).
- **Sign-in CSRF and session fixation**, and the separation of household and operator sessions.

## 7. Build order

Each is a milestone with its own threat model, adversarial tests, E2E journeys at phone, laptop and 2000px, and screenshots in both themes.

| Milestone | Delivers |
|---|---|
| M21 | The header chip and panel in guest mode, the wording on the landing page and the first-plan notice, persistent-storage request, the Safari warning. No server change. |
| M22 | Send a copy: the handoff endpoints, the encrypted relay, the receive page, the replace-or-keep choice. |
| M23 | Household accounts by emailed code, the mailer port and fake, the sign-in screens, sign out, delete, the privacy page section, the rule change in `CLAUDE.md`. |
| M24 | Sync: the stored snapshot, versions, the set merge, the first-sign-in choice, offline catch-up. |
| M25 | Passkeys: registration after first sign-in, sign-in by passkey, a list of passkeys in the panel with remove. |
| M26 | The shortlist ("save for later", grouped by shop, links through `/out/{id}`), which rides on everything above: it is in the snapshot, so it is copied and synced from its first day. |

## 8. Decisions, settled 11 Nov 2026

Rob settled all three: sign-in codes go through his existing Postmark account (decision 62), a session lasts ninety days sliding (decision 60), and a stored plan is encrypted at rest (decision 61). What follows is the list as it stood.

### As it stood

1. **Email provider** (5.7). Blocks M23 going live, not being built.
2. **Session length for a household.** Recommendation: ninety days sliding on a device, because a household that has to sign in again during a power cut has been failed by the product.
3. **Whether a signed-in household's plan is encrypted at rest with a key the server holds.** Recommendation: yes, with the Data Protection key ring already in the database, as the operators' second-factor secrets are. End-to-end encryption of the synced plan was considered and set aside: it makes the emailed code unable to recover a plan, which defeats the reason for the account.
