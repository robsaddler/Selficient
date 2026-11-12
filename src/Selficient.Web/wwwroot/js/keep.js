// @ts-check
/**
 * Where the plan is kept. Most sites ask for an account and this one does not, so it says so where
 * "Sign in" would be: a chip in the header that reads "No account needed" until a plan exists,
 * "Saved on this device" once one does, and "Signed in as" with the address for a household that
 * chose to make an account. The chip opens a panel that says the same in sentences.
 *
 * The first time a plan is saved on a device the plan page says so once, under the figure, and the
 * browser is asked to keep the site's storage rather than clear it under pressure. On Safari on an
 * iPhone or iPad, which clears a site's data after a week without a visit unless it is on the home
 * screen, the panel says that plainly.
 *
 * The pages are the same for everyone, so they open from the cache without a connection. Who is
 * signed in is asked of the server here, and only when the hint cookie that signing in sets is
 * present: a guest's browser never asks.
 */
import * as store from "selficient/plan-store.js";

const SAID_KEY = "selficient.kept-said";
const ASKED_KEY = "selficient.persist-asked";
const ACCOUNT_KEY = "selficient.account";
const HINT = "selficient.signed-in";

/** @type {Promise<string | null> | null} */
let asked = null;

/**
 * The address signed in on this browser, or null for a guest. Asked once a page.
 * @returns {Promise<string | null>}
 */
export function signedInAs() {
  asked ??= ask();
  return asked;
}

/** Forgets who was signed in, on sign-out and when an account is deleted. */
export function forgetAccount() {
  asked = Promise.resolve(null);
  try {
    localStorage.removeItem(ACCOUNT_KEY);
  } catch {
    // Nothing kept, nothing to forget.
  }
}

/** @returns {Promise<string | null>} */
async function ask() {
  if (!document.cookie.split("; ").some((cookie) => cookie.startsWith(`${HINT}=`))) {
    forgetAccount();
    return null;
  }
  try {
    const response = await fetch("/api/account", { cache: "no-store" });
    if (response.status === 401) {
      // The session ended somewhere else: the account was deleted, or the ninety days ran out.
      document.cookie = `${HINT}=; Max-Age=0; Path=/; Secure; SameSite=Lax`;
      forgetAccount();
      return null;
    }
    const body = /** @type {{ email?: unknown }} */ (response.ok ? await response.json() : {});
    if (typeof body.email === "string" && body.email !== "") {
      remember(ACCOUNT_KEY, body.email);
      return body.email;
    }
  } catch {
    // No connection: what was true the last time there was one.
  }
  return read(ACCOUNT_KEY);
}

/**
 * @param {Document} doc
 */
export async function initKeep(doc) {
  const chip = doc.querySelector("[data-keep-chip]");
  const dialog = doc.querySelector("[data-keep-dialog]");
  if (!(chip instanceof HTMLElement) || !(dialog instanceof HTMLDialogElement)) {
    return;
  }

  const safari = dialog.querySelector("[data-keep-safari]");
  if (safari instanceof HTMLElement) {
    safari.hidden = !clearsAfterAWeek();
  }

  const notice = doc.querySelector("[data-kept-notice]");
  if (notice instanceof HTMLElement) {
    notice.addEventListener("click", (event) => {
      if (event.target instanceof Element && event.target.closest("[data-kept-dismiss]")) {
        notice.hidden = true;
      }
    });
  }

  const show = async () => {
    const kept = (await store.getProfile().catch(() => null)) !== null;
    say(doc, chip, dialog, kept, await signedInAs());
    if (kept) {
      sayOnce(notice);
      keepStorage();
    }
  };

  await show();
  // The plan page saves the plan a moment after it loads, and another tab may save or clear one.
  store.onChange(() => void show());
}

/**
 * @param {Document} doc
 * @param {HTMLElement} chip
 * @param {HTMLDialogElement} dialog
 * @param {boolean} kept
 * @param {string | null} email
 */
function say(doc, chip, dialog, kept, email) {
  chip.dataset.kept = email ? "account" : kept ? "device" : "none";
  for (const label of chip.querySelectorAll("[data-keep-label]")) {
    if (label instanceof HTMLElement) {
      label.hidden = label.dataset.keepLabel !== chip.dataset.kept;
    }
  }
  for (const mark of chip.querySelectorAll("[data-keep-mark]")) {
    mark.toggleAttribute("hidden", mark.getAttribute("data-keep-mark") !== (email ? "account" : "device"));
  }
  // The row for this device says one of four things: a plan or none, a guest or a member.
  const says = `${email ? "account" : "device"}${kept ? "" : "-none"}`;
  for (const line of dialog.querySelectorAll("[data-keep-says]")) {
    if (line instanceof HTMLElement) {
      line.hidden = line.dataset.keepSays !== says;
    }
  }
  // There is nothing to send until there is a plan.
  const another = dialog.querySelector("[data-keep-another]");
  if (another instanceof HTMLElement) {
    another.hidden = !kept;
  }
  // What is said to a guest about accounts, and what is said to a household that has one.
  for (const part of doc.querySelectorAll("[data-keep-guest]")) {
    part.toggleAttribute("hidden", email !== null);
  }
  for (const part of doc.querySelectorAll("[data-keep-member]")) {
    part.toggleAttribute("hidden", email === null);
  }
  for (const who of doc.querySelectorAll("[data-keep-email]")) {
    who.textContent = email ?? "";
  }
}

/**
 * The sentence under the figure, the first time a plan is saved on this device and never again.
 * @param {Element | null} notice
 */
function sayOnce(notice) {
  if (!(notice instanceof HTMLElement) || recall(SAID_KEY)) {
    return;
  }
  notice.hidden = false;
  remember(SAID_KEY, "1");
}

/** Asks the browser, once, not to clear this site's storage when the device runs short of room. */
function keepStorage() {
  if (recall(ASKED_KEY) || typeof navigator.storage?.persist !== "function") {
    return;
  }
  remember(ASKED_KEY, "1");
  void navigator.storage.persist().catch(() => false);
}

/** Safari on an iPhone or iPad, in a tab and not from the home screen. */
function clearsAfterAWeek() {
  const ios = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  const installed = matchMedia("(display-mode: standalone)").matches || /** @type {{ standalone?: boolean }} */ (navigator).standalone === true;
  return ios && !installed;
}

/**
 * Whether a flag has been set. Where storage cannot be read it counts as set, so nothing repeats.
 * @param {string} key
 */
function recall(key) {
  try {
    return localStorage.getItem(key) !== null;
  } catch {
    return true;
  }
}

/** @param {string} key */
function read(key) {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

/**
 * @param {string} key
 * @param {string} value
 */
function remember(key, value) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // A per-device convenience only.
  }
}
