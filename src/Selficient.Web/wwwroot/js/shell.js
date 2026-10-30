// @ts-check
/**
 * The app shell: registers the service worker, heals a partial precache, and keeps a device on
 * one deploy at a time. Also the honest offline banner. Lifted from SiteWise's shell.js and cut
 * to what a household needs; everything here is optional and the page is complete without it.
 */

const RELOAD_GUARD_KEY = "selficient.sw-reload";
const RELOAD_GUARD_MS = 120_000;

/**
 * @param {Document} doc
 */
export function initShell(doc) {
  watchConnectivity(doc);
  if (!("serviceWorker" in navigator)) {
    return;
  }
  registerWorker(doc);
}

/**
 * @param {Document} doc
 */
function registerWorker(doc) {
  const workers = navigator.serviceWorker;
  workers.register("/sw.js").catch(() => undefined);

  // Every online load asks the worker to fetch any shell file its precache is missing.
  if (navigator.onLine) {
    workers.ready.then((registration) => registration.active?.postMessage("sw:verify-shell")).catch(() => undefined);
    workers.getRegistration().then((registration) => registration?.update()).catch(() => undefined);
  }

  // Build skew: the page carries this deploy's fingerprint; if the controlling worker answers
  // with another, nudge the update and reload once.
  const pageBuild = doc.documentElement.dataset.swBuild;
  if (navigator.onLine && pageBuild && workers.controller) {
    const channel = new MessageChannel();
    channel.port1.onmessage = (event) => {
      if (event.data === pageBuild || !reloadAllowed()) {
        return;
      }
      workers.getRegistration()
        .then((registration) => registration?.update())
        .catch(() => undefined)
        .finally(() => location.reload());
    };
    workers.controller.postMessage("sw:version?", [channel.port2]);
  }

  // When a new worker replaces an old one, reload once so the new build runs, but never over a
  // form someone is typing into or a dialog that is open. The first worker a device ever gets
  // is not a replacement: the page it took over is already this build, so it stays put.
  let replacing = workers.controller !== null;
  workers.addEventListener("controllerchange", () => {
    const wasReplacing = replacing;
    replacing = true;
    if (!wasReplacing || !reloadAllowed() || doc.querySelector("dialog[open]") || hasTypedText(doc)) {
      return;
    }
    location.reload();
  });
}

/** One reload per two minutes, so a flapping server can never loop a device. */
function reloadAllowed() {
  try {
    const last = Number(sessionStorage.getItem(RELOAD_GUARD_KEY) || 0);
    if (Date.now() - last < RELOAD_GUARD_MS) {
      return false;
    }
    sessionStorage.setItem(RELOAD_GUARD_KEY, String(Date.now()));
    return true;
  } catch {
    return false;
  }
}

/**
 * @param {Document} doc
 */
function hasTypedText(doc) {
  for (const field of doc.querySelectorAll("input[type=text], input[type=number], textarea")) {
    if ((field instanceof HTMLInputElement || field instanceof HTMLTextAreaElement) && field.value !== field.defaultValue) {
      return true;
    }
  }
  return false;
}

/**
 * The banner follows the browser's own online and offline events. No polling: a health probe
 * every few seconds from every open tab is a cost the server pays for nothing.
 * @param {Document} doc
 */
function watchConnectivity(doc) {
  const banner = doc.querySelector("[data-offline-banner]");
  if (!(banner instanceof HTMLElement)) {
    return;
  }
  const apply = () => {
    banner.hidden = navigator.onLine;
    doc.documentElement.toggleAttribute("data-offline", !navigator.onLine);
  };
  apply();
  window.addEventListener("online", apply);
  window.addEventListener("offline", apply);
}
