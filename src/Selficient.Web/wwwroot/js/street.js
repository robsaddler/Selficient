// @ts-check
/**
 * The street group pages. The server keeps who is in a group and a record for each home that it
 * cannot read; everything here that a neighbour sees is sealed in this browser first and opened in
 * theirs. The group's key arrives in the fragment of an invitation and is kept on this device,
 * under the group's id, and nowhere the server could read it. Every sentence this script says is
 * carried on the page, so the words are the page's and are pinned there.
 *
 * What a neighbour's browser wrote is not trusted: it is opened, checked and cut to length in
 * street-crypto.js, and only ever put on the page as text.
 */
import { toBase64Url, fromBase64Url } from "selficient/handoff.js";
import { qrSvg } from "selficient/qr.js";
import * as crypt from "selficient/street-crypto.js";
import { offersFromPlan, readPlan } from "selficient/street-signs.js";

const KEYS = "selficient.streets";
const SEEN = "selficient.street-seen";
const PENDING = "selficient.street-invite";
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

/** @typedef {{ status: number, data: any }} Answer */
/** @typedef {{ gid: string, key: Uint8Array<ArrayBuffer>, pass: Uint8Array<ArrayBuffer> }} Invite */
/** @typedef {{ doc: Document, root: HTMLElement }} Page */
/** @typedef {{ memberId: string, house: string, name: string | null, offers: string[], readable: boolean, mine: boolean, pub: string | null }} Shown */

/**
 * @param {Document} doc
 */
export async function initStreet(doc) {
  const root = doc.querySelector("[data-street]");
  if (!(root instanceof HTMLElement)) {
    // Signing in lands on the plan. A household that signed in on its way to an invitation is taken on to it.
    if (location.pathname === "/plan" && recall(PENDING) !== null && doc.cookie.split("; ").some((cookie) => cookie.startsWith("selficient.signed-in="))) {
      location.replace("/street");
    }
    return;
  }
  /** @type {Page} */
  const page = { doc, root };
  const view = location.pathname.replace(/\/+$/, "").split("/").pop() ?? "";
  const fragment = location.hash.slice(1);
  let invite = view === "join" ? parseInvite(fragment) : null;
  if (view === "join" && invite === null) {
    show(page, "gone");
    put(page, "[data-street-gone-says]", says(page, "badInvitation"));
    return;
  }
  if (invite !== null) {
    // Kept for this tab only, so signing in on the way does not lose it; it is never sent anywhere.
    remember(PENDING, fragment);
  }

  const mine = await api("GET", "/api/street");
  if (mine.status === 401) {
    const invited = invite !== null || recall(PENDING) !== null;
    put(page, "[data-street-guest-says]", says(page, invited ? "guestInvited" : "guest"));
    show(page, "guest");
    return;
  }
  if (mine.status !== 200 || !Array.isArray(mine.data)) {
    // No connection is the day a street is for: the copy this device kept is shown, to be read and printed.
    if (!(view === "group" && mine.status === 0 && drawSeen(page, fragment))) {
      say(page, says(page, "offline"));
    }
    return;
  }
  if (invite === null && view !== "group") {
    invite = parseInvite(recall(PENDING) ?? "");
  }
  forget(PENDING);

  if (invite !== null) {
    await join(page, invite);
  } else if (view === "group") {
    await group(page, fragment);
  } else {
    await list(page, mine.data);
  }
}

/**
 * The invitation in a link's fragment: the group, its key and the pass that lets a browser join.
 * @param {string} fragment
 * @returns {Invite | null}
 */
export function parseInvite(fragment) {
  const [gid, key, pass, ...more] = fragment.split(".");
  const keyBytes = fromBase64Url(key ?? "");
  const passBytes = fromBase64Url(pass ?? "");
  if (more.length > 0 || gid === undefined || !GUID.test(gid) || keyBytes === null || keyBytes.length !== 32 || passBytes === null || passBytes.length !== 32) {
    return null;
  }
  return { gid, key: keyBytes, pass: passBytes };
}

/**
 * The list of the household's groups, and the form that starts one.
 * @param {Page} page
 * @param {{ groupId: string }[]} mine
 */
async function list(page, mine) {
  show(page, "list");
  const groups = page.root.querySelector("[data-street-groups]");
  if (groups instanceof HTMLElement) {
    groups.replaceChildren();
    for (const membership of mine) {
      const item = page.doc.createElement("li");
      const key = keyFor(membership.groupId);
      const read = key === null ? null : await api("GET", `/api/street/${membership.groupId}`);
      const about = key === null || read === null || read.status !== 200 ? null : await aboutOf(key, read.data.about);
      if (about === null) {
        item.textContent = says(page, "noKey");
      } else {
        const link = page.doc.createElement("a");
        link.href = `/street/group#${membership.groupId}`;
        link.textContent = about.name;
        item.append(link);
      }
      groups.append(item);
    }
    if (mine.length === 0) {
      const none = page.doc.createElement("li");
      none.textContent = says(page, "none");
      groups.append(none);
    }
  }

  const form = page.root.querySelector("[data-street-start]");
  form?.addEventListener("submit", (event) => {
    event.preventDefault();
    if (form instanceof HTMLFormElement) {
      void start(page, form);
    }
  });
}

/**
 * Starts a group: makes its key and its invitation's pass here, and sends the server only what it
 * cannot read and the hash of the pass.
 * @param {Page} page
 * @param {HTMLFormElement} form
 */
async function start(page, form) {
  const data = new FormData(form);
  const name = crypt.tidy(data.get("name"), crypt.MAX_GROUP_NAME);
  const house = crypt.tidy(data.get("house"), crypt.MAX_HOUSE);
  const first = crypt.tidy(data.get("first"), crypt.MAX_NAME);
  if (name === "") {
    say(page, says(page, "unnamed"));
    return;
  }
  if (house === "" || first === "") {
    say(page, says(page, "missing"));
    return;
  }
  const key = crypt.randomBytes(32);
  const pass = crypt.randomBytes(32);
  const identity = await crypt.newIdentity();
  const made = await crypt.makeRecord(key, "", identity, { house, name: first, offers: {}, close: [] }, new Map());
  const answer = await api("POST", "/api/street", {
    passHash: await crypt.passHash(pass),
    about: await crypt.seal(key, { name, pass: toBase64Url(pass) }),
    record: made.record,
    keep: await crypt.seal(key, { v: 1, ...identity, house, name: first, offers: {}, close: [], sealedFor: {} }),
  });
  if (answer.status !== 200 || !GUID.test(String(answer.data?.groupId))) {
    say(page, refusal(page, answer));
    return;
  }
  keepKey(answer.data.groupId, key);
  location.assign(`/street/group#${answer.data.groupId}`);
}

/**
 * Joining by an invitation. A household that is in the group already, on another device, is taken
 * straight in: the key is all this device was missing.
 * @param {Page} page
 * @param {Invite} invite
 */
async function join(page, invite) {
  const already = await api("GET", `/api/street/${invite.gid}`);
  if (already.status === 200) {
    keepKey(invite.gid, invite.key);
    location.replace(`/street/group#${invite.gid}`);
    return;
  }
  show(page, "join");
  const form = page.root.querySelector("[data-street-join]");
  form?.addEventListener("submit", (event) => {
    event.preventDefault();
    if (form instanceof HTMLFormElement) {
      void joinWith(page, invite, form);
    }
  });
}

/**
 * @param {Page} page
 * @param {Invite} invite
 * @param {HTMLFormElement} form
 */
async function joinWith(page, invite, form) {
  const data = new FormData(form);
  const house = crypt.tidy(data.get("house"), crypt.MAX_HOUSE);
  const first = crypt.tidy(data.get("first"), crypt.MAX_NAME);
  if (house === "" || first === "") {
    say(page, says(page, "missing"));
    return;
  }
  const identity = await crypt.newIdentity();
  const made = await crypt.makeRecord(invite.key, invite.gid, identity, { house, name: first, offers: {}, close: [] }, new Map());
  const answer = await api("POST", `/api/street/${invite.gid}/join`, {
    pass: crypt.toBase64(invite.pass),
    record: made.record,
    keep: await crypt.seal(invite.key, { v: 1, ...identity, house, name: first, offers: {}, close: [], sealedFor: {} }),
  });
  if (answer.status !== 200) {
    say(page, refusal(page, answer));
    return;
  }
  keepKey(invite.gid, invite.key);
  location.replace(`/street/group#${invite.gid}`);
}

/**
 * One group: the street, home by home; what this household shares; the invitation; leaving.
 * @param {Page} page
 * @param {string} gid
 * @param {string} [said] a sentence to say once the group is drawn
 */
async function group(page, gid, said) {
  const key = GUID.test(gid) ? keyFor(gid) : null;
  if (key === null) {
    put(page, "[data-street-gone-says]", says(page, GUID.test(gid) ? "noKey" : "gone"));
    show(page, "gone");
    return;
  }
  const answer = await api("GET", `/api/street/${gid}`);
  if (answer.status === 0) {
    if (!drawSeen(page, gid)) {
      say(page, says(page, "offline"));
    }
    return;
  }
  const about = answer.status === 200 ? await aboutOf(key, answer.data.about) : null;
  if (about === null) {
    dropSeen(gid);
    put(page, "[data-street-gone-says]", says(page, "gone"));
    show(page, "gone");
    return;
  }

  /** @type {{ memberId: string, record: string, mayRemove: boolean }[]} */
  const homes = Array.isArray(answer.data.homes) ? answer.data.homes : [];
  const you = String(answer.data.you);
  const kept = crypt.asKeep(await crypt.open(key, answer.data.keep));
  // A membership with nothing kept (it should not happen) is given a fresh key pair, so the page still works and the next save mends it.
  const mine = kept ?? { v: /** @type {1} */ (1), ...(await crypt.newIdentity()), house: "", name: "", offers: {}, close: [], sealedFor: {} };

  /** @type {Shown[]} */
  const shown = [];
  for (const home of homes) {
    if (home.memberId === you) {
      shown.push({ memberId: you, house: mine.house, name: mine.name, offers: crypt.OFFERS.filter((offer) => mine.offers[offer] !== undefined), readable: true, mine: true, pub: mine.pub });
    } else {
      const read = await crypt.readHome(key, gid, mine, home.record);
      shown.push(read === null
        ? { memberId: home.memberId, house: "", name: null, offers: [], readable: false, mine: false, pub: null }
        : { memberId: home.memberId, house: read.house, name: read.name, offers: crypt.OFFERS.filter((offer) => read.offers.includes(offer) || read.close.includes(offer)), readable: true, mine: false, pub: read.pub });
    }
  }
  shown.sort(byHouse);
  const mayRemove = homes.some((home) => home.memberId === you && home.mayRemove === true);
  const others = shown.filter((home) => !home.mine && home.pub !== null);
  const pubs = new Map(others.map((home) => [home.memberId, home.pub ?? ""]));

  // A home in the closer circle that has a new key (it lost its devices and was invited again) is sealed for again, without being asked.
  const stale = mine.close.some((member) => pubs.has(member) && mine.sealedFor[member] !== pubs.get(member));
  if (kept !== null && stale) {
    await publish(gid, key, mine, { house: mine.house, name: mine.name, offers: mine.offers, close: mine.close }, pubs);
  }

  draw(page, gid, about, shown, mine, mayRemove);
  wire(page, gid, key, about, mine, pubs);
  keepSeen(gid, about.name, shown);
  show(page, "group");
  void nextSteps(page, mine);
  if (said !== undefined) {
    say(page, said);
  }
}

/**
 * Puts a group on the page: the street, then what this household shares and how to invite.
 * @param {Page} page
 * @param {string} gid
 * @param {{ name: string, pass: string }} about
 * @param {Shown[]} shown
 * @param {crypt.Keep} mine
 * @param {boolean} mayRemove
 */
function draw(page, gid, about, shown, mine, mayRemove) {
  const { doc, root } = page;
  drawStreet(page, about.name, shown, mayRemove);

  // What this household shares, as it last saved it.
  setValue(root, "#share-house", mine.house);
  setValue(root, "#share-first", mine.name);
  for (const select of root.querySelectorAll("[data-street-offer]")) {
    if (select instanceof HTMLSelectElement) {
      select.value = mine.offers[select.dataset.streetOffer ?? ""] ?? "";
    }
  }
  const circle = root.querySelector("[data-street-circle]");
  const noCircle = root.querySelector("[data-street-circle-none]");
  if (circle instanceof HTMLElement) {
    circle.replaceChildren();
    for (const home of shown.filter((other) => !other.mine && other.readable)) {
      const item = doc.createElement("li");
      const label = doc.createElement("label");
      const box = doc.createElement("input");
      box.type = "checkbox";
      box.name = "close";
      box.value = home.memberId;
      box.checked = mine.close.includes(home.memberId);
      label.append(box, " ", says(page, "houseLabel").replace("{house}", home.house));
      item.append(label);
      circle.append(item);
    }
    if (noCircle instanceof HTMLElement) {
      noCircle.hidden = circle.childElementCount > 0;
    }
  }

  // The invitation: as a link, as a code, and on the slips that go through doors.
  const link = `${location.origin}/street/join#${gid}.${toBase64Url(keyFor(gid) ?? new Uint8Array())}.${about.pass}`;
  const input = root.querySelector("[data-street-link]");
  if (input instanceof HTMLInputElement) {
    input.value = link;
  }
  root.querySelector("[data-street-qr]")?.replaceChildren(qrSvg(doc, link));
  for (const slip of root.querySelectorAll("[data-street-slips] .street__slip")) {
    const name = slip.querySelector("[data-slip-name]");
    const from = slip.querySelector("[data-slip-from]");
    if (name && from) {
      name.textContent = about.name;
      from.textContent = says(page, "slipFrom").replace("{house}", mine.house);
    }
    slip.querySelector("[data-slip-qr]")?.replaceChildren(qrSvg(doc, link));
  }
  const share = root.querySelector("[data-street-share]");
  if (share instanceof HTMLElement) {
    share.hidden = typeof navigator.share !== "function";
  }

  // A group of one home is an invitation and nothing else yet: it goes first until a neighbour joins, and beside the sharing after.
  const invite = root.querySelector("[data-street-invite]");
  const slot = root.querySelector(`[data-invite-slot="${shown.length === 1 ? "first" : "aside"}"]`);
  if (invite !== null && slot !== null && invite.parentElement !== slot) {
    slot.append(invite);
  }
  // The place it is not in takes no room: an empty box in a grid is still a gap.
  for (const place of root.querySelectorAll("[data-invite-slot]")) {
    if (place instanceof HTMLElement) {
      place.hidden = place !== slot;
    }
  }
}

/**
 * Puts the street on the page, as text: its name, what it has between it, what nobody has offered
 * yet, and each home. Used for the street as the server has it and for the copy this device kept.
 * @param {Page} page
 * @param {string} name
 * @param {Shown[]} shown
 * @param {boolean} mayRemove
 */
function drawStreet(page, name, shown, mayRemove) {
  const { doc, root } = page;
  put(page, "[data-street-title]", name);
  doc.title = name;
  put(page, "[data-street-count]", shown.length === 1 ? says(page, "homesOne") : says(page, "homesMany").replace("{n}", String(shown.length)));

  const words = offerWords(page);
  const between = root.querySelector("[data-street-between]");
  if (between instanceof HTMLElement) {
    between.replaceChildren();
    for (const offer of crypt.OFFERS) {
      const count = shown.filter((home) => home.offers.includes(offer)).length;
      if (count > 0) {
        const item = doc.createElement("li");
        item.setAttribute("data-between-offer", offer);
        const what = words.get(offer)?.between ?? "";
        item.textContent = (count === 1 ? says(page, "betweenOne") : says(page, "betweenMany").replace("{n}", String(count))).replace("{what}", what);
        between.append(item);
      }
    }
    if (between.childElementCount === 0) {
      const none = doc.createElement("li");
      none.textContent = says(page, "nothingBetween");
      between.append(none);
    }
  }

  // What nobody has offered this household, said once there is somebody who might: it is what one more home could add.
  const gaps = root.querySelector("[data-street-gaps]");
  if (gaps instanceof HTMLElement) {
    const missing = shown.length < 2 ? [] : crypt.OFFERS.filter((offer) => !shown.some((home) => home.offers.includes(offer)));
    const list = missing.map((offer) => lowered(words.get(offer)?.words ?? "")).filter((offer) => offer !== "");
    gaps.textContent = says(page, "gaps").replace("{list}", list.join(", "));
    gaps.hidden = list.length === 0;
  }

  const list = root.querySelector("[data-street-homes]");
  if (list instanceof HTMLElement) {
    list.replaceChildren();
    for (const home of shown) {
      const item = doc.createElement("li");
      item.className = "street__home";
      item.setAttribute("data-home", home.memberId);
      const head = doc.createElement("p");
      head.className = "street__house";
      const house = doc.createElement("span");
      house.className = "data";
      house.textContent = home.house === "" ? "?" : home.house;
      head.append(house);
      if (home.name !== null && home.name !== "") {
        const name = doc.createElement("span");
        name.className = "street__name";
        name.setAttribute("data-home-name", "");
        name.textContent = home.name;
        head.append(" ", name);
      }
      if (home.mine) {
        const yours = doc.createElement("span");
        yours.className = "street__you";
        yours.textContent = says(page, "yourHome");
        head.append(" ", yours);
        item.setAttribute("data-home-mine", "");
      }
      item.append(head);
      if (home.offers.length > 0) {
        const offers = doc.createElement("ul");
        offers.className = "street__home-offers";
        for (const offer of home.offers) {
          const line = doc.createElement("li");
          line.setAttribute("data-home-offer", offer);
          line.textContent = words.get(offer)?.words ?? "";
          offers.append(line);
        }
        item.append(offers);
      } else {
        const none = doc.createElement("p");
        none.className = "review__none";
        none.textContent = says(page, home.readable ? "nothing" : "unreadable");
        item.append(none);
      }
      if (mayRemove && !home.mine) {
        const remove = doc.createElement("button");
        remove.type = "button";
        remove.className = "button button--small street__screen";
        remove.setAttribute("data-street-remove", home.memberId);
        remove.setAttribute("aria-label", says(page, "removeLabel").replace("{house}", home.house));
        remove.textContent = "Remove";
        item.append(remove);
      }
      list.append(item);
    }
  }
}

/**
 * A phrase as it reads in the middle of a sentence.
 * @param {string} words
 */
function lowered(words) {
  return words.charAt(0).toLowerCase() + words.slice(1);
}

/**
 * What a household that has not yet got anything out of the group should do next, one thing at a
 * time: make a plan, so the page can say what it could offer; or choose who sees what its plan
 * says it has. Beside each such offer the page says so. All of it is worked out here, from the
 * plan this browser keeps.
 * @param {Page} page
 * @param {crypt.Keep} mine
 */
async function nextSteps(page, mine) {
  const { root } = page;
  const plan = await readPlan();
  const could = offersFromPlan(root, plan);
  for (const hint of root.querySelectorAll("[data-offer-plan]")) {
    if (hint instanceof HTMLElement) {
      hint.hidden = !could.some((offer) => offer.id === hint.dataset.offerPlan);
    }
  }
  const offering = Object.keys(mine.offers).length > 0;
  const next = plan.profile === null ? "plan" : !offering && could.length > 0 ? "offer" : "";
  for (const block of root.querySelectorAll("[data-street-next]")) {
    if (block instanceof HTMLElement) {
      block.hidden = block.dataset.streetNext !== next;
      if (block.dataset.streetNext === "offer") {
        block.textContent = could.length === 1 ? says(page, "nextOfferOne") : says(page, "nextOfferMany").replace("{n}", String(could.length));
      }
    }
  }
}

/**
 * The street as this device last saw it, kept beside the group's key so that it can be read with
 * no connection. It is what the page had already opened and shown, and it is on no other device.
 * @returns {Record<string, any>}
 */
function seenAll() {
  try {
    const kept = JSON.parse(localStorage.getItem(SEEN) ?? "{}");
    return typeof kept === "object" && kept !== null && !Array.isArray(kept) ? kept : {};
  } catch {
    return {};
  }
}

/**
 * @param {string} gid
 * @param {string} name
 * @param {Shown[]} shown
 */
function keepSeen(gid, name, shown) {
  try {
    const homes = shown.map((home) => ({ house: home.house, name: home.name, offers: home.offers, mine: home.mine, readable: home.readable }));
    localStorage.setItem(SEEN, JSON.stringify({ ...seenAll(), [gid]: { name, at: new Date().toISOString(), homes } }));
  } catch {
    // No storage: without a connection the group says it could not be reached.
  }
}

/**
 * @param {string} gid
 */
function dropSeen(gid) {
  try {
    const kept = seenAll();
    delete kept[gid];
    localStorage.setItem(SEEN, JSON.stringify(kept));
  } catch {
    // Nothing was kept.
  }
}

/**
 * Shows the street from the copy this device kept, to be read and printed and nothing else. Only
 * for a household that is signed in on this device, and everything in the copy is checked as a
 * neighbour's record is, because it has been sitting in storage.
 * @param {Page} page
 * @param {string} gid
 * @returns {boolean} whether there was a copy to show
 */
function drawSeen(page, gid) {
  const signedIn = page.doc.cookie.split("; ").some((cookie) => cookie.startsWith("selficient.signed-in="));
  const seen = GUID.test(gid) ? seenAll()[gid] : null;
  if (!signedIn || typeof seen !== "object" || seen === null || !Array.isArray(seen.homes)) {
    return false;
  }
  const name = crypt.tidy(seen.name, crypt.MAX_GROUP_NAME);
  const at = new Date(String(seen.at));
  if (name === "" || Number.isNaN(at.getTime())) {
    return false;
  }
  /** @type {Shown[]} */
  const shown = seen.homes.slice(0, 100).map((/** @type {any} */ home) => ({
    memberId: "",
    house: crypt.tidy(home?.house, crypt.MAX_HOUSE),
    name: typeof home?.name === "string" ? crypt.tidy(home.name, crypt.MAX_NAME) : null,
    offers: crypt.OFFERS.filter((offer) => Array.isArray(home?.offers) && home.offers.includes(offer)),
    readable: home?.readable !== false,
    mine: home?.mine === true,
    pub: null,
  }));
  page.root.setAttribute("data-street-offline", "");
  drawStreet(page, name, shown, false);
  const note = page.root.querySelector("[data-street-seen]");
  if (note instanceof HTMLElement) {
    note.textContent = says(page, "seen").replace("{date}", at.toLocaleDateString("en-GB", { day: "numeric", month: "long", year: "numeric" }));
    note.hidden = false;
  }
  page.root.querySelector("[data-street-print]")?.addEventListener("click", () => window.print());
  show(page, "group");
  return true;
}

/**
 * The buttons and the form of a group, wired once for the page.
 * @param {Page} page
 * @param {string} gid
 * @param {Uint8Array<ArrayBuffer>} key
 * @param {{ name: string, pass: string }} about
 * @param {crypt.Keep} mine
 * @param {Map<string, string>} pubs
 */
function wire(page, gid, key, about, mine, pubs) {
  const { root } = page;
  if (root.hasAttribute("data-street-wired")) {
    // Drawn again after a save or a removal: the handlers read the page's own latest state.
    state = { gid, key, about, mine, pubs };
    return;
  }
  root.setAttribute("data-street-wired", "");
  state = { gid, key, about, mine, pubs };

  root.querySelector("[data-street-sharing]")?.addEventListener("submit", (event) => {
    event.preventDefault();
    const form = event.target;
    if (form instanceof HTMLFormElement && state !== null) {
      void save(page, form, state);
    }
  });
  root.addEventListener("click", (event) => {
    const target = event.target instanceof Element ? event.target : null;
    const remove = target?.closest("[data-street-remove]");
    if (state === null) {
      return;
    }
    if (remove instanceof HTMLElement) {
      void removeHome(page, remove.getAttribute("data-street-remove") ?? "", state);
    } else if (target?.closest("[data-street-leave]")) {
      void leave(page, state);
    } else if (target?.closest("[data-street-copy]")) {
      void copy(page);
    } else if (target?.closest("[data-street-share]")) {
      void shareInvite(page, state);
    } else if (target?.closest("[data-street-print-slips]")) {
      // The slips are a second thing this page can print: the page says which for as long as the print takes.
      root.setAttribute("data-print", "slips");
      window.addEventListener("afterprint", () => root.removeAttribute("data-print"), { once: true });
      window.print();
    } else if (target?.closest("[data-street-print]")) {
      window.print();
    }
  });
}

/** @type {{ gid: string, key: Uint8Array<ArrayBuffer>, about: { name: string, pass: string }, mine: crypt.Keep, pubs: Map<string, string> } | null} */
let state = null;

/**
 * Saves what the household shares: its record for the neighbours and what its own devices keep.
 * @param {Page} page
 * @param {HTMLFormElement} form
 * @param {NonNullable<typeof state>} now
 */
async function save(page, form, now) {
  const data = new FormData(form);
  const house = crypt.tidy(data.get("house"), crypt.MAX_HOUSE);
  const first = crypt.tidy(data.get("first"), crypt.MAX_NAME);
  if (house === "" || first === "") {
    say(page, says(page, "missing"));
    return;
  }
  /** @type {Record<string, crypt.Circle>} */
  const offers = {};
  for (const offer of crypt.OFFERS) {
    const circle = data.get(`offer-${offer}`);
    if (circle === "street" || circle === "close") {
      offers[offer] = circle;
    }
  }
  const close = data.getAll("close").filter((member) => typeof member === "string" && now.pubs.has(member)).map(String);
  const answer = await publish(now.gid, now.key, now.mine, { house, name: first, offers, close }, now.pubs);
  if (answer.status !== 200) {
    say(page, refusal(page, answer));
    return;
  }
  await group(page, now.gid, says(page, "saved"));
}

/**
 * Seals and sends the household's record and what its devices keep.
 * @param {string} gid
 * @param {Uint8Array<ArrayBuffer>} key
 * @param {crypt.Identity} identity
 * @param {crypt.Sharing} sharing
 * @param {Map<string, string>} pubs
 * @returns {Promise<Answer>}
 */
async function publish(gid, key, identity, sharing, pubs) {
  const made = await crypt.makeRecord(key, gid, identity, sharing, pubs);
  return api("PUT", `/api/street/${gid}/record`, {
    record: made.record,
    keep: await crypt.seal(key, { v: 1, priv: identity.priv, pub: identity.pub, house: sharing.house, name: sharing.name, offers: sharing.offers, close: sharing.close, sealedFor: made.sealedFor }),
  });
}

/**
 * Removes a home, and makes the invitation it had worthless: a new pass, whose hash goes to the
 * server and whose value goes to the members inside the group's name, sealed.
 * @param {Page} page
 * @param {string} memberId
 * @param {NonNullable<typeof state>} now
 */
async function removeHome(page, memberId, now) {
  const pass = crypt.randomBytes(32);
  const answer = await api("POST", `/api/street/${now.gid}/remove`, {
    memberId,
    passHash: await crypt.passHash(pass),
    about: await crypt.seal(now.key, { name: now.about.name, pass: toBase64Url(pass) }),
  });
  if (answer.status !== 200) {
    say(page, refusal(page, answer));
    return;
  }
  await group(page, now.gid, says(page, "removed"));
}

/**
 * @param {Page} page
 * @param {NonNullable<typeof state>} now
 */
async function leave(page, now) {
  const answer = await api("POST", `/api/street/${now.gid}/leave`, {});
  if (answer.status !== 200) {
    say(page, refusal(page, answer));
    return;
  }
  dropKey(now.gid);
  dropSeen(now.gid);
  location.assign("/street");
}

/**
 * Hands the invitation to the device's own sharing, which is how it reaches the street's chat.
 * @param {Page} page
 * @param {NonNullable<typeof state>} now
 */
async function shareInvite(page, now) {
  const input = page.root.querySelector("[data-street-link]");
  if (!(input instanceof HTMLInputElement)) {
    return;
  }
  try {
    await navigator.share({ title: now.about.name, text: says(page, "shareText"), url: input.value });
  } catch {
    // Dismissed, or refused by the device: the link is still there to copy.
  }
}

/**
 * @param {Page} page
 */
async function copy(page) {
  const input = page.root.querySelector("[data-street-link]");
  if (!(input instanceof HTMLInputElement)) {
    return;
  }
  try {
    await navigator.clipboard.writeText(input.value);
    say(page, says(page, "copied"));
  } catch {
    // No clipboard here: the link is selected, for the keyboard to copy.
    input.select();
  }
}

/**
 * The group's name and the pass of its current invitation, opened with the group's key.
 * @param {Uint8Array<ArrayBuffer>} key
 * @param {unknown} sealed
 * @returns {Promise<{ name: string, pass: string } | null>}
 */
async function aboutOf(key, sealed) {
  const about = await crypt.open(key, sealed);
  if (typeof about !== "object" || about === null) {
    return null;
  }
  const { name, pass } = /** @type {{ name?: unknown, pass?: unknown }} */ (about);
  const named = crypt.tidy(name, crypt.MAX_GROUP_NAME);
  return typeof pass === "string" && fromBase64Url(pass)?.length === 32 && named !== "" ? { name: named, pass } : null;
}

/**
 * By house number, then by what else the house is called.
 * @param {Shown} a
 * @param {Shown} b
 */
function byHouse(a, b) {
  const [x, y] = [Number.parseInt(a.house, 10), Number.parseInt(b.house, 10)];
  if (Number.isFinite(x) && Number.isFinite(y) && x !== y) {
    return x - y;
  }
  if (Number.isFinite(x) !== Number.isFinite(y)) {
    return Number.isFinite(x) ? -1 : 1;
  }
  return a.house.localeCompare(b.house, "en-GB");
}

/**
 * The offers' words, read from the page.
 * @param {Page} page
 * @returns {Map<string, { words: string, between: string }>}
 */
function offerWords(page) {
  const words = new Map();
  for (const item of page.root.querySelectorAll("[data-street-offers] li")) {
    if (item instanceof HTMLElement && item.dataset.offer) {
      words.set(item.dataset.offer, { words: item.textContent ?? "", between: item.dataset.between ?? "" });
    }
  }
  return words;
}

/**
 * Asks the server. A redirect to the sign-in is "not signed in"; no answer at all is nought.
 * @param {string} method
 * @param {string} path
 * @param {unknown} [body]
 * @returns {Promise<Answer>}
 */
async function api(method, path, body) {
  try {
    /** @type {RequestInit} */
    const asking = { method, redirect: "manual", cache: "no-store", headers: { accept: "application/json" } };
    if (body !== undefined) {
      // The header only this site's own script can add, which every write must carry.
      asking.headers = { accept: "application/json", "content-type": "application/json", "X-Selficient-Sync": "1" };
      asking.body = JSON.stringify(body);
    }
    const response = await fetch(path, asking);
    if (response.type === "opaqueredirect" || response.status === 401) {
      return { status: 401, data: null };
    }
    return { status: response.status, data: await response.json().catch(() => null) };
  } catch {
    return { status: 0, data: null };
  }
}

/**
 * What to say when the server refused: its own sentence, which is one of ours, or that it could not be reached.
 * @param {Page} page
 * @param {Answer} answer
 */
function refusal(page, answer) {
  return answer.status !== 0 && typeof answer.data?.message === "string" ? answer.data.message : says(page, "offline");
}

/**
 * A sentence the page carries for the script.
 * @param {Page} page
 * @param {string} name
 */
function says(page, name) {
  return page.root.dataset[`says${name.charAt(0).toUpperCase()}${name.slice(1)}`] ?? "";
}

/**
 * @param {Page} page
 * @param {string} sentence
 */
function say(page, sentence) {
  const line = page.root.querySelector("[data-street-says]");
  if (line instanceof HTMLElement) {
    line.textContent = sentence;
    line.hidden = sentence === "";
    line.scrollIntoView({ block: "nearest" });
  }
}

/**
 * @param {Page} page
 * @param {string} selector
 * @param {string} text
 */
function put(page, selector, text) {
  const target = page.root.querySelector(selector);
  if (target) {
    target.textContent = text;
  }
}

/**
 * @param {ParentNode} root
 * @param {string} selector
 * @param {string} value
 */
function setValue(root, selector, value) {
  const input = root.querySelector(selector);
  if (input instanceof HTMLInputElement) {
    input.value = value;
  }
}

/**
 * Shows one state of the page and moves the focus to its heading, so a screen reader hears where it is.
 * @param {Page} page
 * @param {string} which
 */
function show(page, which) {
  page.root.setAttribute("data-street", which);
  for (const block of page.root.querySelectorAll("[data-street-state]")) {
    if (block instanceof HTMLElement) {
      block.hidden = block.dataset.streetState !== which;
    }
  }
  const heading = page.root.querySelector(`[data-street-state="${which}"] h1`);
  if (heading instanceof HTMLElement && which !== "what") {
    heading.focus({ preventScroll: true });
  }
}

/**
 * The keys this device holds, by group. Nothing else in the browser, and nothing on the server, has them.
 * @returns {Record<string, string>}
 */
function keys() {
  try {
    const held = JSON.parse(localStorage.getItem(KEYS) ?? "{}");
    return typeof held === "object" && held !== null && !Array.isArray(held) ? held : {};
  } catch {
    return {};
  }
}

/**
 * @param {string} gid
 * @returns {Uint8Array<ArrayBuffer> | null}
 */
function keyFor(gid) {
  const held = keys()[gid];
  const bytes = typeof held === "string" ? fromBase64Url(held) : null;
  return bytes !== null && bytes.length === 32 ? bytes : null;
}

/**
 * @param {string} gid
 * @param {Uint8Array<ArrayBuffer>} key
 */
function keepKey(gid, key) {
  try {
    localStorage.setItem(KEYS, JSON.stringify({ ...keys(), [gid]: toBase64Url(key) }));
  } catch {
    // No storage: the group opens from its invitation each time.
  }
}

/**
 * @param {string} gid
 */
function dropKey(gid) {
  try {
    const held = keys();
    delete held[gid];
    localStorage.setItem(KEYS, JSON.stringify(held));
  } catch {
    // Nothing was kept, so there is nothing to drop.
  }
}

/**
 * @param {string} name
 * @param {string} value
 */
function remember(name, value) {
  try {
    sessionStorage.setItem(name, value);
  } catch {
    // Without it, signing in on the way means opening the invitation again.
  }
}

/**
 * @param {string} name
 * @returns {string | null}
 */
function recall(name) {
  try {
    return sessionStorage.getItem(name);
  } catch {
    return null;
  }
}

/**
 * @param {string} name
 */
function forget(name) {
  try {
    sessionStorage.removeItem(name);
  } catch {
    // Nothing to forget.
  }
}
