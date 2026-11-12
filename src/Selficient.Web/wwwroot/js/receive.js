// @ts-check
/**
 * The page a copy arrives on. The link's fragment holds the ticket and the key; this reads them,
 * takes them out of the address bar, collects the ciphertext (which the server deletes as it hands
 * it over), opens it here and says what arrived. Nothing is written to this device until the
 * person presses a button, and if a plan is already here the choice is to replace it or keep it,
 * never to merge.
 */
import * as store from "selficient/plan-store.js";
import { fromBase64Url, isEnvelope, unseal } from "selficient/handoff.js";

const PENDING_KEY = "selficient.receive";

const DEVICE_WORDS = /** @type {Record<string, string>} */ ({
  "iphone": "an iPhone",
  "ipad": "an iPad",
  "android-phone": "an Android phone",
  "android-tablet": "an Android tablet",
  "windows": "a Windows computer",
  "mac": "a Mac",
  "chromebook": "a Chromebook",
  "linux": "a Linux computer",
  "other": "another device",
});

/**
 * @param {Document} doc
 */
export async function initReceive(doc) {
  const root = doc.querySelector("[data-receive]");
  if (!(root instanceof HTMLElement)) {
    return;
  }

  /** @param {string} state */
  const show = (state) => {
    root.dataset.receive = state;
    for (const part of root.querySelectorAll("[data-receive-state]")) {
      if (part instanceof HTMLElement) {
        part.hidden = part.dataset.receiveState !== state;
      }
    }
    const heading = root.querySelector(`[data-receive-state="${state}"] h1`);
    if (heading instanceof HTMLElement) {
      heading.focus();
    }
  };

  root.addEventListener("click", (event) => {
    if (event.target instanceof Element && event.target.closest("[data-receive-retry]")) {
      location.reload();
    }
  });
  // A second link opened in a tab that is already on this page changes only the fragment, which
  // loads nothing. Start again so that it is read.
  addEventListener("hashchange", () => {
    if (location.hash.length > 1) {
      location.reload();
    }
  });

  const envelope = await arrive(show);
  if (!envelope) {
    return;
  }

  const here = await store.snapshot().catch(() => null);
  const occupied = here !== null && here.profile !== null;
  say(root, "[data-receive-summary]", `${describe(envelope.plan, "A plan")}, made on ${DEVICE_WORDS[envelope.from]} ${ago(envelope.made)}.`);
  if (occupied) {
    say(root, "[data-receive-here]", `${describe(here, "The plan on this device is")}.`);
  }
  show(occupied ? "choose" : "arrived");

  root.addEventListener("click", (event) => {
    const target = event.target instanceof Element ? event.target : null;
    if (target?.closest("[data-receive-put]")) {
      void store.restore(envelope.plan).then(
        () => {
          forget();
          show("done");
        },
        () => show("unreadable"),
      );
    } else if (target?.closest("[data-receive-keep]")) {
      forget();
      show("kept");
    }
  });
}

/**
 * Finds the copy: from the link, or waiting in this tab from before a reload.
 * @param {(state: string) => void} show
 * @returns {Promise<import("selficient/handoff.js").Envelope | null>}
 */
async function arrive(show) {
  const fragment = location.hash.slice(1);
  if (fragment === "") {
    const pending = recall();
    if (!pending) {
      show("nothing");
    }
    return pending;
  }

  const [id = "", keyText = "", ...rest] = fragment.split(".");
  const key = fromBase64Url(keyText);
  if (rest.length > 0 || !/^[A-Za-z0-9_-]{22}$/.test(id) || !key || key.length !== 32) {
    history.replaceState(null, "", location.pathname);
    show("incomplete");
    return null;
  }

  show("reading");
  /** @type {Response | null} */
  let response;
  try {
    response = await fetch(`/api/handoff/${id}`, { cache: "no-store", referrerPolicy: "no-referrer" });
  } catch {
    response = null;
  }
  if (!response || (!response.ok && response.status !== 404)) {
    // The fragment stays, so trying again can work: the copy has not been taken.
    show("later");
    return null;
  }
  // From here the ticket is spent or was never good. The key leaves the address bar and its history entry.
  history.replaceState(null, "", location.pathname);
  if (!response.ok) {
    show("used");
    return null;
  }
  try {
    const envelope = await unseal(new Uint8Array(await response.arrayBuffer()), key);
    remember(envelope);
    return envelope;
  } catch {
    show("unreadable");
    return null;
  }
}

/**
 * "A plan for 4 people in NP12, 9 things ticked"
 * @param {import("selficient/plan-store.js").Snapshot} plan
 * @param {string} opening
 */
function describe(plan, opening) {
  const people = Number(plan.profile?.People);
  const who = Number.isInteger(people) && people > 0 ? ` for ${people === 1 ? "1 person" : `${people} people`}` : "";
  const district = outward(plan.profile?.Postcode);
  const where = district ? ` in ${district}` : "";
  const ticked = plan.ticks.length === 0 ? "nothing ticked yet" : plan.ticks.length === 1 ? "1 thing ticked" : `${plan.ticks.length} things ticked`;
  return `${opening}${who}${where}, ${ticked}`;
}

/**
 * The outward half of a postcode, which names a district and not a street.
 * @param {string | undefined} postcode
 */
function outward(postcode) {
  const tight = (postcode ?? "").toUpperCase().replaceAll(/\s/g, "");
  const out = tight.length >= 5 ? tight.slice(0, -3) : "";
  return /^[A-Z]{1,2}[0-9][0-9A-Z]?$/.test(out) ? out : "";
}

/**
 * How long ago the copy was made, by the two devices' clocks, which may not agree.
 * @param {number} made
 */
function ago(made) {
  const seconds = (Date.now() - made) / 1000;
  if (seconds < 0 || seconds > 15 * 60) {
    return "a few minutes ago";
  }
  if (seconds < 45) {
    return "just now";
  }
  const minutes = Math.round(seconds / 60);
  return minutes <= 1 ? "a minute ago" : `${minutes} minutes ago`;
}

/**
 * @param {HTMLElement} root
 * @param {string} selector
 * @param {string} sentence
 */
function say(root, selector, sentence) {
  for (const line of root.querySelectorAll(selector)) {
    line.textContent = sentence;
  }
}

/** @param {import("selficient/handoff.js").Envelope} envelope */
function remember(envelope) {
  try {
    sessionStorage.setItem(PENDING_KEY, JSON.stringify(envelope));
  } catch {
    // Without it a reload before the press loses the copy; the page still works.
  }
}

/** @returns {import("selficient/handoff.js").Envelope | null} */
function recall() {
  try {
    const value = /** @type {unknown} */ (JSON.parse(sessionStorage.getItem(PENDING_KEY) ?? "null"));
    return isEnvelope(value) ? value : null;
  } catch {
    return null;
  }
}

function forget() {
  try {
    sessionStorage.removeItem(PENDING_KEY);
  } catch {
    // Nothing to forget.
  }
}
