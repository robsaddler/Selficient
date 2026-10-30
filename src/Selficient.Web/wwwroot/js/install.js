// @ts-check
/**
 * "Install the app" in the header, the one place a phone user finds it. Three cases, one button:
 * Chromium fires beforeinstallprompt, so keep the event and prompt on tap; iOS never fires it, so
 * show the button and explain Share then Add to Home Screen; already installed, so no button.
 * A dismissal is remembered on this device for thirty days, an install for good.
 */

const STORAGE_KEY = "selficient.install-dismissed";
const QUIET_DAYS = 30;

/** @typedef {Event & { prompt: () => Promise<void>, userChoice: Promise<{ outcome: "accepted" | "dismissed" }> }} InstallPromptEvent */

/**
 * @param {Document} doc
 */
export function initInstall(doc) {
  const button = doc.querySelector("[data-install]");
  const iosDialog = doc.querySelector("[data-install-ios]");
  if (!(button instanceof HTMLButtonElement) || !(iosDialog instanceof HTMLDialogElement)) {
    return;
  }

  window.addEventListener("appinstalled", () => {
    button.hidden = true;
    remember(true);
  });

  if (isStandalone() || isQuiet()) {
    return;
  }

  /** @type {InstallPromptEvent | null} */
  let deferred = null;

  window.addEventListener("beforeinstallprompt", (event) => {
    event.preventDefault();
    deferred = /** @type {InstallPromptEvent} */ (event);
    button.hidden = false;
  });

  if (isIos()) {
    button.hidden = false;
  }

  button.addEventListener("click", async () => {
    if (deferred) {
      const prompt = deferred;
      deferred = null;
      await prompt.prompt();
      const choice = await prompt.userChoice.catch(() => ({ outcome: "dismissed" }));
      button.hidden = true;
      remember(choice.outcome === "accepted");
      return;
    }
    iosDialog.showModal();
    iosDialog.querySelector("[data-close]")?.addEventListener("click", () => iosDialog.close(), { once: true });
    iosDialog.addEventListener("close", () => {
      remember(false);
      button.hidden = true;
    }, { once: true });
  });
}

function isStandalone() {
  return matchMedia("(display-mode: standalone)").matches || /** @type {{ standalone?: boolean }} */ (navigator).standalone === true;
}

function isIos() {
  return /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
}

function isQuiet() {
  try {
    const at = Number(localStorage.getItem(STORAGE_KEY) || 0);
    return at === -1 || (at > 0 && Date.now() - at < QUIET_DAYS * 86_400_000);
  } catch {
    return false;
  }
}

/** @param {boolean} forever */
function remember(forever) {
  try {
    localStorage.setItem(STORAGE_KEY, forever ? "-1" : String(Date.now()));
  } catch {
    // A per-device convenience only.
  }
}
