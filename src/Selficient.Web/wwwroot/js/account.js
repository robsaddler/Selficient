// @ts-check
/**
 * The account pages' one piece of behaviour. Signing out asks whether a copy of the plan should
 * stay on this device; the plan is in the browser's own store, so only the browser can remove it,
 * and it does so before the form goes. Without this script the question is not asked and the plan
 * stays, which is the safe way to be wrong.
 */
import * as store from "selficient/plan-store.js";
import { forgetAccount } from "selficient/keep.js";

/** What the sync keeps beside the plan: forgotten with the account. */
const SYNC_KEYS = ["selficient.sync"];

/**
 * @param {Document} doc
 */
export function initAccount(doc) {
  const signOut = doc.querySelector("[data-sign-out]");
  if (signOut instanceof HTMLFormElement) {
    const field = signOut.querySelector("[data-keep-copy]");
    const box = field?.querySelector("input[type=checkbox]");
    if (field instanceof HTMLElement) {
      field.hidden = false;
    }
    signOut.addEventListener("submit", (event) => {
      forget();
      if (!(box instanceof HTMLInputElement) || box.checked) {
        return;
      }
      // Not keeping a copy: take the plan out of this browser, then sign out. A form sent by
      // script raises no submit event, so this does not come round again.
      event.preventDefault();
      void store.clearAll().catch(() => undefined).then(() => signOut.submit());
    });
  }

  const remove = doc.querySelector("[data-delete-account]");
  if (remove instanceof HTMLFormElement) {
    remove.addEventListener("submit", forget);
  }

  if (doc.querySelector("[data-account-gone]")) {
    forget();
  }
}

function forget() {
  forgetAccount();
  try {
    for (const key of SYNC_KEYS) {
      localStorage.removeItem(key);
    }
  } catch {
    // Nothing kept, nothing to forget.
  }
}
