// @ts-check
/**
 * The figures the plan works from, as this browser knows them. It asks the server which set is in
 * force whenever a plan is on the screen and the network is there; it keeps the last answer; and it
 * falls back to the figures in its own script when it has never had one. A plan says which set it
 * was built with, so when the server's number has moved on the plan is built again from the answers
 * this browser keeps, and the page says that is what happened.
 */
import { SHIPPED } from "selficient/figures-shipped.js";

/** @typedef {import("selficient/figures-shipped.js").Figures} Figures */

const KEPT = "selficient.figures";
const REBUILT = "selficient.figures-rebuilt";

/**
 * The figures as this browser last heard them, or as shipped when it never has.
 * @returns {Figures}
 */
export function known() {
  try {
    const kept = JSON.parse(localStorage.getItem(KEPT) ?? "null");
    return isFigures(kept) ? kept : SHIPPED;
  } catch {
    return SHIPPED;
  }
}

/**
 * Asks the server which set is in force and keeps the answer. Null when it cannot be reached or
 * the answer is not one: the caller goes on with what it knew.
 * @returns {Promise<Figures | null>}
 */
export async function ask() {
  try {
    const response = await fetch("/api/figures", { cache: "no-store", headers: { accept: "application/json" } });
    if (!response.ok) {
      return null;
    }
    const answer = await response.json();
    if (!isFigures(answer)) {
      return null;
    }
    try {
      localStorage.setItem(KEPT, JSON.stringify(answer));
    } catch {
      // No room or no storage: the answer is still good for this page.
    }
    return answer;
  } catch {
    return null;
  }
}

/**
 * On a plan: learn which set is in force, and build the plan again when it is not the one the plan
 * was built with. A plan that has just been rebuilt says so and is not rebuilt a second time.
 * @param {Document} doc
 */
export async function initFigures(doc) {
  const plan = doc.querySelector("[data-plan][data-figures-version]");
  if (!(plan instanceof HTMLElement)) {
    return;
  }
  if (rebuiltJustNow()) {
    const says = doc.querySelector("[data-figures-updated]");
    if (says instanceof HTMLElement) {
      says.hidden = false;
    }
    return;
  }
  const figures = await ask();
  if (figures !== null && String(figures.version) !== plan.dataset.figuresVersion) {
    try {
      sessionStorage.setItem(REBUILT, "1");
    } catch {
      // Without the mark the page could rebuild for ever, so without storage it is left as it is.
      return;
    }
    location.replace("/plan");
  }
}

/** Reads and clears the mark a rebuild leaves for the page that follows it. */
function rebuiltJustNow() {
  try {
    const marked = sessionStorage.getItem(REBUILT) !== null;
    sessionStorage.removeItem(REBUILT);
    return marked;
  } catch {
    return false;
  }
}

/**
 * @param {unknown} value
 * @returns {value is Figures}
 */
export function isFigures(value) {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const { version, publishedAt, figures } = /** @type {{ version?: unknown, publishedAt?: unknown, figures?: unknown }} */ (value);
  return Number.isInteger(version) && /** @type {number} */ (version) >= 0
    && (publishedAt === null || typeof publishedAt === "string")
    && Array.isArray(figures) && figures.length <= 100 && figures.every(isFigure);
}

/** @param {unknown} value */
function isFigure(value) {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const { id, question, unit, min, max, whole, value: amount } = /** @type {Record<string, unknown>} */ (value);
  return typeof id === "string" && /^[a-z0-9]+([.-][a-z0-9]+)*$/.test(id) && id.length <= 160
    && typeof question === "string" && question.length <= 200 && typeof unit === "string" && unit.length <= 80
    && typeof min === "number" && typeof max === "number" && typeof amount === "number"
    && Number.isFinite(min) && Number.isFinite(max) && Number.isFinite(amount) && min <= max
    && typeof whole === "boolean";
}
