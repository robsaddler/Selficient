// @ts-check
/**
 * Light and dark. The system preference wins until the person chooses; the choice is one byte in
 * localStorage, which is the only thing localStorage is for (ADR-001).
 */
const STORAGE_KEY = "selficient.theme";

/**
 * @param {Document} doc
 */
export function initTheme(doc) {
  const root = doc.documentElement;
  const stored = readStored();
  if (stored) {
    root.dataset.theme = stored;
  }

  doc.addEventListener("click", (event) => {
    const target = /** @type {HTMLElement | null} */ (event.target);
    const button = target?.closest("[data-theme-toggle]");
    if (!(button instanceof HTMLButtonElement)) {
      return;
    }
    const next = currentTheme(root) === "dark" ? "light" : "dark";
    root.dataset.theme = next;
    button.setAttribute("aria-pressed", String(next === "dark"));
    writeStored(next);
  });

  const toggle = doc.querySelector("[data-theme-toggle]");
  toggle?.setAttribute("aria-pressed", String(currentTheme(root) === "dark"));
}

/**
 * @param {HTMLElement} root
 * @returns {"light" | "dark"}
 */
function currentTheme(root) {
  if (root.dataset.theme === "dark" || root.dataset.theme === "light") {
    return root.dataset.theme;
  }
  return matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

/** @returns {"light" | "dark" | null} */
function readStored() {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return value === "light" || value === "dark" ? value : null;
  } catch {
    return null;
  }
}

/** @param {"light" | "dark"} value */
function writeStored(value) {
  try {
    localStorage.setItem(STORAGE_KEY, value);
  } catch {
    // Private mode or blocked storage: the choice simply does not persist.
  }
}
