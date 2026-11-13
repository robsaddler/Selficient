// @ts-check
/**
 * What this browser's plan says a household could offer its street. The plan is in this browser's
 * own store and the page carries what to look for (the actions held and the answers given that
 * mean an offer could be made), so the question is answered here and is never put to the server.
 * It is a prompt and no more: nothing is offered until a household chooses to.
 */
import { getProfile, getTicks, onChange } from "selficient/plan-store.js";

const KEYS = "selficient.streets";

/** @typedef {{ held: Set<string>, profile: Record<string, string> | null }} Plan */

/**
 * The plan this browser keeps: what is held, and the answers about the home, or null where there
 * is no plan. A store that will not open is a browser with no plan.
 * @returns {Promise<Plan>}
 */
export async function readPlan() {
  try {
    return { held: new Set(await getTicks()), profile: await getProfile() };
  } catch {
    return { held: new Set(), profile: null };
  }
}

/**
 * The offers the plan says this household could make, in the page's own order.
 * @param {ParentNode} root somewhere that holds the list of offers and their signs
 * @param {Plan} plan
 * @returns {{ id: string, words: string }[]}
 */
export function offersFromPlan(root, plan) {
  /** @type {{ id: string, words: string }[]} */
  const could = [];
  for (const item of root.querySelectorAll("[data-street-offers] li")) {
    if (!(item instanceof HTMLElement) || !item.dataset.offer) {
      continue;
    }
    const holds = (item.dataset.held ?? "").split(" ").some((action) => plan.held.has(action));
    const answered = (item.dataset.answers ?? "").split(" ").some((answer) => {
      const [field, value] = answer.split("=");
      return field !== undefined && field !== "" && plan.profile !== null && plan.profile[field] === value;
    });
    if (holds || answered) {
      could.push({ id: item.dataset.offer, words: item.textContent ?? "" });
    }
  }
  return could;
}

/**
 * Whether this device has opened a street group: it holds a group's key.
 * @returns {boolean}
 */
export function hasGroupHere() {
  try {
    const held = JSON.parse(localStorage.getItem(KEYS) ?? "{}");
    return typeof held === "object" && held !== null && !Array.isArray(held) && Object.keys(held).length > 0;
  } catch {
    return false;
  }
}

/**
 * The plan page's card: what this household could offer its street from what it has ticked, kept
 * in step as boxes are ticked, and the way in to a group or back to the one this device is in.
 * @param {Document} doc
 */
export async function initStreetCard(doc) {
  const card = doc.querySelector("[data-street-card]");
  if (!(card instanceof HTMLElement)) {
    return;
  }
  const inGroup = hasGroupHere();
  for (const way of card.querySelectorAll("[data-street-way]")) {
    if (way instanceof HTMLElement) {
      way.hidden = (way.dataset.streetWay === "open") !== inGroup;
    }
  }

  const redraw = async () => {
    const could = offersFromPlan(card, await readPlan());
    const list = card.querySelector("[data-street-could]");
    if (list instanceof HTMLElement) {
      list.replaceChildren(...could.map((offer) => {
        const item = doc.createElement("li");
        item.setAttribute("data-could", offer.id);
        item.textContent = offer.words;
        return item;
      }));
      list.hidden = could.length === 0;
    }
    for (const says of card.querySelectorAll("[data-street-card-says]")) {
      if (says instanceof HTMLElement) {
        says.hidden = (says.dataset.streetCardSays === "could") !== (could.length > 0);
      }
    }
    card.setAttribute("data-street-card", could.length > 0 ? "could" : "nothing");
  };
  await redraw();
  onChange(() => void redraw());
}
