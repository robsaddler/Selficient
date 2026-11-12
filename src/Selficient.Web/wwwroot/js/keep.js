// @ts-check
/**
 * Where the plan is kept. Most sites ask for an account and this one does not, so it says so where
 * "Sign in" would be: a chip in the header that reads "No account needed" until a plan exists and
 * "Saved on this device" once one does, opening a panel that says the same in a sentence.
 *
 * The first time a plan is saved on a device the plan page says so once, under the figure, and the
 * browser is asked to keep the site's storage rather than clear it under pressure. On Safari on an
 * iPhone or iPad, which clears a site's data after a week without a visit unless it is on the home
 * screen, the panel says that plainly. Nothing here talks to the server.
 */
import * as store from "selficient/plan-store.js";

const SAID_KEY = "selficient.kept-said";
const ASKED_KEY = "selficient.persist-asked";

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
    say(chip, dialog, kept);
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
 * @param {HTMLElement} chip
 * @param {HTMLDialogElement} dialog
 * @param {boolean} kept
 */
function say(chip, dialog, kept) {
  chip.dataset.kept = kept ? "device" : "none";
  for (const label of chip.querySelectorAll("[data-keep-label]")) {
    if (label instanceof HTMLElement) {
      label.hidden = label.dataset.keepLabel !== chip.dataset.kept;
    }
  }
  for (const line of dialog.querySelectorAll("[data-keep-says]")) {
    if (line instanceof HTMLElement) {
      line.hidden = line.dataset.keepSays !== chip.dataset.kept;
    }
  }
  // There is nothing to send until there is a plan.
  const another = dialog.querySelector("[data-keep-another]");
  if (another instanceof HTMLElement) {
    another.hidden = !kept;
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
  remember(SAID_KEY);
}

/** Asks the browser, once, not to clear this site's storage when the device runs short of room. */
function keepStorage() {
  if (recall(ASKED_KEY) || typeof navigator.storage?.persist !== "function") {
    return;
  }
  remember(ASKED_KEY);
  void navigator.storage.persist().catch(() => false);
}

/** Safari on an iPhone or iPad, in a tab and not from the home screen. */
function clearsAfterAWeek() {
  const ios = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  const installed = matchMedia("(display-mode: standalone)").matches || /** @type {{ standalone?: boolean }} */ (navigator).standalone === true;
  return ios && !installed;
}

/** @param {string} key */
function recall(key) {
  try {
    return localStorage.getItem(key) !== null;
  } catch {
    return true;
  }
}

/** @param {string} key */
function remember(key) {
  try {
    localStorage.setItem(key, "1");
  } catch {
    // A per-device convenience only.
  }
}
