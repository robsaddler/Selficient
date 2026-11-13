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
import { getProfile, getTicks } from "selficient/plan-store.js";

const KEYS = "selficient.streets";
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
    say(page, says(page, "offline"));
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
    say(page, says(page, "offline"));
    return;
  }
  const about = answer.status === 200 ? await aboutOf(key, answer.data.about) : null;
  if (about === null) {
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
  show(page, "group");
  if (said !== undefined) {
    say(page, said);
  }
}

/**
 * Puts the street on the page, as text.
 * @param {Page} page
 * @param {string} gid
 * @param {{ name: string, pass: string }} about
 * @param {Shown[]} shown
 * @param {crypt.Keep} mine
 * @param {boolean} mayRemove
 */
function draw(page, gid, about, shown, mine, mayRemove) {
  const { doc, root } = page;
  put(page, "[data-street-title]", about.name);
  doc.title = about.name;
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

  // What this household shares, as it last saved it.
  setValue(root, "#share-house", mine.house);
  setValue(root, "#share-first", mine.name);
  for (const select of root.querySelectorAll("[data-street-offer]")) {
    if (select instanceof HTMLSelectElement) {
      select.value = mine.offers[select.dataset.streetOffer ?? ""] ?? "";
    }
  }
  void showPlanSigns(root);
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

  const link = `${location.origin}/street/join#${gid}.${toBase64Url(keyFor(gid) ?? new Uint8Array())}.${about.pass}`;
  const input = root.querySelector("[data-street-link]");
  if (input instanceof HTMLInputElement) {
    input.value = link;
  }
  root.querySelector("[data-street-qr]")?.replaceChildren(qrSvg(doc, link));
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
  location.assign("/street");
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
 * Says, beside each offer this browser's plan says the household could make, that it does. The
 * plan is read from this browser's own store and goes nowhere; the page carries what to look for.
 * It is a prompt: nothing is chosen for the household. With no plan, or a store that will not
 * open, nothing is said.
 * @param {HTMLElement} root
 */
async function showPlanSigns(root) {
  /** @type {Set<string>} */
  let held = new Set();
  /** @type {Record<string, string>} */
  let profile = {};
  try {
    held = new Set(await getTicks());
    profile = (await getProfile()) ?? {};
  } catch {
    // No store: no signs.
  }
  for (const item of root.querySelectorAll("[data-street-offers] li")) {
    const says = item instanceof HTMLElement ? root.querySelector(`[data-offer-plan="${item.dataset.offer ?? ""}"]`) : null;
    if (!(item instanceof HTMLElement) || !(says instanceof HTMLElement)) {
      continue;
    }
    const holds = (item.dataset.held ?? "").split(" ").some((action) => held.has(action));
    const answered = (item.dataset.answers ?? "").split(" ").some((answer) => {
      const [field, value] = answer.split("=");
      return field !== undefined && field !== "" && profile[field] === value;
    });
    says.hidden = !(holds || answered);
  }
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
