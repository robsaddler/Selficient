# Street groups

*13 November 2026. Agreed with Rob over the day; every choice below that was his is marked. Built in stages; the stage each part is in is at the foot.*

## What it is for

When the power, the water or the phones go for days, the house three doors down with a wood stove matters more than anything in a cupboard. A street group lets neighbours tell each other, ahead of time, what each could offer: a warm room, somewhere to charge a phone, a hob that works. It is offers and never needs. Nobody says who in their house is frail, what cash they hold or when they are away, and there is no setting that could.

## What Rob decided

| Decision | Whose |
|---|---|
| Each household controls in its own settings what it shares and with whom | Rob |
| Two circles: a closer circle of homes the household picks, anywhere in the group ("Doris at number 12 as easily as next door"), and everyone else in the group | Rob |
| The closer circle sees a first name and the house number; everyone else sees the house number only | Rob |
| Each offer is set to nobody, the closer circle or the whole group, and everything starts at nobody | Rob agreed |
| Putting someone in your closer circle is one-way: nobody is asked and nobody is refused | Rob agreed |
| Joining needs an account | Rob agreed |
| The person who started a group can remove a home from it | Rob |
| The server cannot read what homes offer. A member who loses every device and the link is invited again | Rob |
| Needs are deferred "as it needs careful consideration" | Rob |

## What a household sees

1. **Starting.** From the plan page, "Start a group for your street". It names the group, gives its house number and first name, and gets an invitation: a link and a code to print or send.
2. **Joining.** A neighbour opens the link, signs in or makes an account, gives a house number and a first name. There is no directory and no search: without the link a group cannot be found.
3. **Sharing settings.** One page. Who is in the closer circle, ticked from the list of homes; then each thing the household could offer, with three choices.
4. **The street page.** By house number, what each home offers this household, given what each chose to show it. Above the list, what the street has between it. It prints on one side of paper, for when the screens are dark.

## The offers

A closed list, each a few plain words. A household is shown every one, with a note beside those its plan says it has; it ticks what is true. There is no free text in an offer, so there is nothing to moderate and nothing that could name a person.

| Id | Words | The plan says you have it when |
|---|---|---|
| `warm-room` | A warm room | there is a wood stove or a gas heater |
| `hot-food` | A hob or stove that works without power | there is a gas hob, a wood stove, or a camping stove held |
| `phone-charging` | Somewhere to charge a phone | a power station, power banks, rooftop solar with backup or a car that gives power is held |
| `cold-storage` | Room in a fridge that is still running | rooftop solar with backup or a power station is held |
| `drinking-water` | Drinking water to spare | water cans or a gravity filter are held |
| `light` | A lantern or torch to lend | lanterns or headtorches are held |
| `news` | A radio that works without power | the wind-up radio is held |
| `first-aid` | A first-aid kit | the safety kit is held |
| `lift` | A car with fuel or charge | there is a car |

## How the server is kept from reading it

The same idea as "send a copy" (decision 35): the key is made in the browser and travels in the part of a link that no server is sent.

- **The group key.** The starter's browser makes a 256-bit key. Everything every member may read (house numbers, offers to the whole group, the group's name) is encrypted with it in the browser before it is sent.
- **The invitation.** `/street/join#<group>.<key>.<pass>`. The fragment never reaches the server. The pass is a second random value: the server holds only its hash, and a browser that presents the pass may join. So the server can let people in without ever seeing the key.
- **The closer circle.** Each member's browser makes a key pair and publishes the public half inside its record. What only the closer circle may read (the first name, the offers made to them) is encrypted with a fresh key, and that key is wrapped once for each chosen home with a key the two homes derive between them. The wrapped keys carry no names and are padded with decoys, so neither the server nor the rest of the street can tell who is in whose circle, or how many are.
- **What the server holds.** Which accounts are in which group, when each joined and last wrote, and for each a blob it cannot read. It does not know the street's name, anyone's house number, anyone's first name or any offer.
- **Removing a home.** The server stops answering that account and the starter's browser sets a new pass, so the old link opens nothing. The group key is not changed: a removed home that kept it has nothing to decrypt, because the server gives it nothing. Changing the key would lock out every member who was away, and the only party it would defend against is the server acting with a removed member.
- **Another device.** The key is on the devices that opened the link, and nowhere else. The group page shows the invitation again so the household can open it on its other device. It is deliberately not kept with the account's plan, which the server can read (decision 61).
- **What a home's own devices need.** Its key pair, its house number and first name, what it offers to whom and who is in its closer circle are sealed with the group's key and kept beside its membership (`keep`). The server answers it only to the account it belongs to and cannot read it. A second device that opens the invitation finds it and is the same home at once.
- **The page.** `/street`, `/street/join` and `/street/group` are one page the server draws the same for everyone (`Pages/Street`, `StreetCopy`). `street.js` chooses the state and `street-crypto.js` does everything that needs a key. Nothing a neighbour wrote is ever put on the page as markup, and everything read from a record is checked first.

## What the plan says

Beside each offer the page can say "Your plan says you have this." The browser works it out from the plan it keeps: `StreetCopy.PlanSigns` lists, for each offer, the plan actions any one of which held says so and the answers about the home that say so, and the page carries that list for `street.js` to hold against the store. Nothing is sent to find out. It is a prompt and no more: every offer stays at nobody until the household chooses.

## Limits

A hundred homes to a group; five groups to an account; a member's record 24 KB at most; the invitation's pass 32 bytes. The longest-standing member is the one who may remove, so a starter who leaves or deletes their account hands the group on without anyone doing anything. A group with nobody in it is swept away.

## The privacy rule

The rule in `CLAUDE.md` says the server keeps, for a household with an account, its email address and its plan. Rob agreed on 13 November to extend it for groups: for a household that joins one, the server also keeps that it is a member, and a record it cannot read. Leaving a group or deleting the account removes both at once.

## Stages

| Stage | What | State |
|---|---|---|
| 1 | The server: groups, members, the pass, removal, leaving, limits, the sweep; the endpoints | Done |
| 2 | The browser: keys, the record, the closer circle; the page for starting, joining, sharing and the street; the printed sheet | Done |
| 3 | The way in from the plan page; what the plan says a household has, beside each offer; the privacy page | Done |
