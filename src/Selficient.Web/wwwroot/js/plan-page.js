// @ts-check
/**
 * The plan page: ticks persist, other tabs stay in step, export and import work, and the empty
 * state re-posts a stored profile. The figure and the board are the server's, redrawn by board.js;
 * this only makes the page remember. Without JavaScript the page is still a complete plan.
 */
import * as store from "selficient/plan-store.js";

/**
 * @param {Document} doc
 */
export async function initPlanPage(doc) {
  // Import is offered on the plan and on the empty state alike.
  doc.querySelector("[data-import-plan]")?.addEventListener("change", (event) => {
    const input = event.target;
    if (input instanceof HTMLInputElement && input.files?.[0]) {
      void importPlan(doc, input.files[0]);
    }
  });

  const empty = doc.querySelector("[data-resubmit]");
  if (empty instanceof HTMLFormElement) {
    await resubmitIfStored(empty);
    return;
  }

  const plan = doc.querySelector("[data-plan]");
  if (!(plan instanceof HTMLElement)) {
    return;
  }

  await rememberProfile(doc);
  // The plan on the page was built from the figures it was posted with. Where this browser keeps
  // others (they were changed on another device, or the questions were answered again), the plan
  // is built again from them before anything else is done to it.
  if (await followOwnFigures(doc)) {
    return;
  }
  await applyAssumptions(doc);
  await applyChoices(doc);
  recompute(doc);

  doc.addEventListener("change", (event) => {
    const box = event.target;
    if (!(box instanceof HTMLInputElement) || !isChoice(box.dataset.choice)) {
      return;
    }
    // One at most: pressing a pill lifts the other two, and pressing the pressed one clears it.
    for (const other of choiceBoxes(box.closest("[data-action-row]"))) {
      if (other !== box) {
        other.checked = false;
      }
    }
    // A choice made by hand settles whatever a button did before it; the way back is withdrawn.
    hideUndo(doc);
    const row = box.closest("[data-action-row]");
    const counted = row?.querySelector("[data-amount]");
    if (counted instanceof HTMLElement) {
      void chooseCounted(doc, counted, box.value, box.checked ? box.dataset.choice : null);
    } else {
      void store.setChoice(box.value, box.checked ? box.dataset.choice : null).then(() => recompute(doc));
    }
  });

  // The plan's assumptions, open to the household's own: the cash a person needs for a day.
  doc.addEventListener("change", (event) => {
    const input = event.target;
    if (!(input instanceof HTMLInputElement) || !input.hasAttribute("data-assume-cash")) {
      return;
    }
    const typed = Number.parseInt(input.value, 10);
    const rate = Number.isInteger(typed) ? Math.min(Math.max(typed, Number(input.min)), Number(input.max)) : null;
    void saveCashRate(doc, rate);
  });

  // A figure of the household's own for something it has: kept, and the plan is built again from it.
  doc.addEventListener("change", (event) => {
    const input = event.target;
    if (!(input instanceof HTMLInputElement) || !input.dataset.ownFigure) {
      return;
    }
    const typed = Number(input.value);
    const within = Math.min(Math.max(typed, Number(input.min)), Number(input.max));
    // Kept to four places, which is as fine as the plan reads a figure.
    const figure = input.value.trim() === "" || !Number.isFinite(typed) ? null : input.step === "1" ? Math.round(within) : Math.round(within * 10000) / 10000;
    void saveOwnFigure(doc, input.dataset.ownFigure, figure === Number(input.dataset.ownPlans) ? null : figure);
  });

  // What an action asks for moved with an assumption, so a count may now reach it, or no longer.
  doc.addEventListener("selficient:targets", () => {
    // Whether or not a choice changed, a figure may now be part of what is asked where it was all of it.
    void reconcile(doc).then(() => applyChoices(doc));
  });

  // A figure typed into "How much do you have?": the count decides which of its actions are held.
  doc.addEventListener("change", (event) => {
    const input = event.target;
    const counted = input instanceof HTMLInputElement && input.hasAttribute("data-amount-input") ? input.closest("[data-amount]") : null;
    if (!(input instanceof HTMLInputElement) || !(counted instanceof HTMLElement) || !counted.dataset.amount) {
      return;
    }
    hideUndo(doc);
    const typed = Number.parseInt(input.value, 10);
    const most = Number.parseInt(input.max, 10);
    const amount = Number.isInteger(typed) && typed >= 0 ? Math.min(typed, most) : null;
    void setFigure(doc, counted.dataset.amount, amount).then(() => settle(doc));
  });

  doc.addEventListener("click", (event) => {
    const target = event.target instanceof Element ? event.target : null;
    const bulk = target?.closest("[data-bulk]");
    if (bulk instanceof HTMLButtonElement && !bulk.closest("[data-board-tallies]")) {
      void applyBulk(doc, bulk);
    } else if (target?.closest("[data-assume-reset]")) {
      void saveCashRate(doc, null);
    } else if (target?.closest("[data-own-reset]") instanceof HTMLElement) {
      void saveOwnFigure(doc, target.closest("[data-own-reset]")?.getAttribute("data-own-reset") ?? "", null);
    } else if (target?.closest("[data-undo-button]")) {
      void undo(doc);
    } else if (target?.closest("[data-undo-dismiss]")) {
      hideUndo(doc);
    }
  });

  store.onChange(() => {
    void followOwnFigures(doc);
    void applyAssumptions(doc);
    void applyChoices(doc).then(() => recompute(doc));
  });

  doc.querySelector("[data-export-plan]")?.addEventListener("click", () => {
    void exportPlan(doc);
  });

  doc.querySelector("[data-clear-plan]")?.addEventListener("click", () => {
    void store.clearAll().then(() => window.location.assign("/"));
  });

  // A tick made before this point was not heard; the attribute says the page now remembers.
  plan.setAttribute("data-plan-ready", "");
}

/**
 * @param {HTMLFormElement} form
 */
async function resubmitIfStored(form) {
  const profile = await store.getProfile().catch(() => null);
  if (!profile) {
    return;
  }
  for (const input of form.querySelectorAll("input[data-field]")) {
    if (input instanceof HTMLInputElement && input.dataset.field && input.dataset.field in profile) {
      input.value = profile[input.dataset.field] ?? "";
    }
  }
  await store.addOwnFigures(form);
  form.requestSubmit();
}

/**
 * @param {Document} doc
 */
async function rememberProfile(doc) {
  const echo = doc.querySelector("[data-profile-echo]");
  if (!(echo instanceof HTMLFormElement)) {
    return;
  }
  /** @type {Record<string, string>} */
  const profile = {};
  for (const [name, value] of new FormData(echo)) {
    if (typeof value === "string") {
      profile[name] = value;
    }
  }
  if (store.isProfile(profile)) {
    await store.saveProfile(profile).catch(() => undefined);
  }
}

/**
 * @param {unknown} value
 * @returns {value is import("selficient/plan-store.js").Choice}
 */
function isChoice(value) {
  return value === "have" || value === "plan" || value === "skip";
}

/**
 * The three tick boxes of one action.
 * @param {Element | null} row
 * @returns {HTMLInputElement[]}
 */
function choiceBoxes(row) {
  /** @type {HTMLInputElement[]} */
  const boxes = [];
  for (const box of row?.querySelectorAll("input[data-choice]") ?? []) {
    if (box instanceof HTMLInputElement) {
      boxes.push(box);
    }
  }
  return boxes;
}

/**
 * Puts what the store holds onto the page: each action's three boxes, one at most ticked, and the
 * household's own figure in every box that counts one. A figure's box shows once the action is
 * held or a figure has been given, and an action held with no figure shows what the plan asks for.
 * @param {Document} doc
 */
async function applyChoices(doc) {
  const choices = await store.getChoices().catch(() => /** @type {Record<string, string>} */ ({}));
  const amounts = await store.getAmounts().catch(() => /** @type {Record<string, number>} */ ({}));
  for (const row of doc.querySelectorAll("[data-action-row]")) {
    const id = row.getAttribute("data-action-row") ?? "";
    for (const box of choiceBoxes(row)) {
      box.checked = choices[id] === box.dataset.choice;
    }
    const counted = row.querySelector("[data-amount]");
    const input = counted?.querySelector("[data-amount-input]");
    if (!(counted instanceof HTMLElement) || !(input instanceof HTMLInputElement)) {
      continue;
    }
    const figure = amounts[counted.dataset.amount ?? ""];
    const held = choices[id] === "have";
    counted.hidden = figure === undefined && !held;
    showPart(row, !held && figure !== undefined && figure > 0 && figure < Number(counted.dataset.amountTarget));
    // Never under the fingers of someone typing in it.
    if (doc.activeElement !== input) {
      input.value = figure !== undefined ? String(figure) : held ? counted.dataset.amountTarget ?? "" : "";
    }
  }
}

/**
 * A counted action the household has some of, short of what the plan asks: the first pill reads
 * "Have some" and its box is in the mixed state, so the figure that is being counted is seen to be.
 * Pressing it is "I have all of it", which raises the figure to what the action asks.
 * @param {Element} row
 * @param {boolean} part
 */
function showPart(row, part) {
  const box = row.querySelector('input[data-choice="have"]');
  if (!(box instanceof HTMLInputElement)) {
    return;
  }
  box.indeterminate = part;
  box.closest("[data-choice-pill]")?.toggleAttribute("data-part", part);
  for (const says of row.querySelectorAll("[data-have-says]")) {
    if (says instanceof HTMLElement) {
      says.hidden = (says.dataset.haveSays === "part") !== part;
    }
  }
}

/**
 * Keeps the household's own rate, or goes back to the plan's, shows it in the box and has the board
 * redrawn from it.
 * @param {Document} doc
 * @param {number | null} rate null is the plan's own
 */
async function saveCashRate(doc, rate) {
  const input = doc.querySelector("[data-assume-cash]");
  if (!(input instanceof HTMLInputElement)) {
    return;
  }
  const plans = Number(input.dataset.assumeDefault);
  // The rate is one assumption among several: the figures kept beside it stay as they are.
  const { figures } = await store.getAssumptions().catch(() => /** @type {import("selficient/plan-store.js").Assumptions} */ ({}));
  await store.saveAssumptions({ ...(figures === undefined ? {} : { figures }), ...(rate === null || rate === plans ? {} : { cashRate: rate }) });
  input.value = String(rate ?? plans);
  await settle(doc);
}

/**
 * Puts the household's own rate in its box, where it has one.
 * @param {Document} doc
 */
async function applyAssumptions(doc) {
  const input = doc.querySelector("[data-assume-cash]");
  const assumptions = await store.getAssumptions().catch(() => /** @type {import("selficient/plan-store.js").Assumptions} */ ({}));
  if (input instanceof HTMLInputElement && doc.activeElement !== input) {
    input.value = String(assumptions.cashRate ?? input.dataset.assumeDefault ?? "");
  }
}

const OWN_REBUILT = "selficient.own-rebuilt";

// Set once this page has asked for the plan to be built again, so a second asker does not take the mark the next page is to find.
let leaving = false;

/**
 * Keeps one figure of the household's own, or forgets it, and has the plan built again. The page
 * that comes back is the one that shows the figure in its box: nothing is changed on this one.
 * @param {Document} doc
 * @param {string} id
 * @param {number | null} figure null is the plan's own
 */
async function saveOwnFigure(doc, id, figure) {
  const assumptions = await store.getAssumptions().catch(() => null);
  if (assumptions === null || id === "") {
    return;
  }
  const figures = { ...(assumptions.figures ?? {}) };
  if (figure === null) {
    delete figures[id];
  } else {
    figures[id] = figure;
  }
  const rest = assumptions.cashRate === undefined ? {} : { cashRate: assumptions.cashRate };
  await store.saveAssumptions(Object.keys(figures).length === 0 ? rest : { ...rest, figures });
  await followOwnFigures(doc);
}

/**
 * Holds the figures this browser keeps against the ones the plan on the page was built with, and
 * has the plan built again when they differ. The page that follows a rebuild says so, opens the
 * questions and is not rebuilt a second time, so a figure the server will not take cannot loop.
 * @param {Document} doc
 * @returns {Promise<boolean>} true when the page is on its way out
 */
async function followOwnFigures(doc) {
  const asked = doc.querySelector("[data-own-asked]");
  if (!(asked instanceof HTMLElement) || leaving) {
    return leaving;
  }
  let justRebuilt = false;
  try {
    justRebuilt = sessionStorage.getItem(OWN_REBUILT) !== null;
    sessionStorage.removeItem(OWN_REBUILT);
  } catch {
    // Without the mark the page could rebuild for ever, so without storage it is left as it is.
    return false;
  }
  if (justRebuilt) {
    const says = doc.querySelector("[data-own-rebuilt]");
    const questions = doc.querySelector("[data-own-figures]");
    if (says instanceof HTMLElement && questions instanceof HTMLDetailsElement) {
      says.hidden = false;
      questions.open = true;
      says.scrollIntoView({ block: "center" });
    }
    return false;
  }
  const assumptions = await store.getAssumptions().catch(() => null);
  if (assumptions === null) {
    return false;
  }
  const kept = Object.entries(assumptions.figures ?? {});
  const built = new Map([...asked.querySelectorAll("li")].map((item) => [item.dataset.id ?? "", Number(item.dataset.value)]));
  if (kept.length === built.size && kept.every(([id, figure]) => Math.abs((built.get(id) ?? Number.NaN) - figure) < 0.00005)) {
    return false;
  }
  if (leaving) {
    return true;
  }
  try {
    sessionStorage.setItem(OWN_REBUILT, "1");
  } catch {
    return false;
  }
  leaving = true;
  location.replace("/plan");
  return true;
}

/**
 * Brings the choices into line with every figure the household gave, without touching the figures:
 * for when what an action asks for has moved under a figure that stayed where it was.
 * @param {Document} doc
 */
async function reconcile(doc) {
  const choices = await store.getChoices().catch(() => /** @type {Record<string, string>} */ ({}));
  const amounts = await store.getAmounts().catch(() => /** @type {Record<string, number>} */ ({}));
  /** @type {Record<string, import("selficient/plan-store.js").Choice | null>} */
  const changes = {};
  for (const [stock, amount] of Object.entries(amounts)) {
    Object.assign(changes, figureChanges(doc, stock, amount, choices));
  }
  if (Object.keys(changes).length > 0) {
    await store.setChoices(changes);
    await settle(doc);
  }
}

/**
 * What a figure asks of the choices on its actions: held where it reaches, not held where it does not.
 * @param {Document} doc
 * @param {string} stock
 * @param {number | null} amount
 * @param {Record<string, string>} choices
 * @returns {Record<string, import("selficient/plan-store.js").Choice | null>}
 */
function figureChanges(doc, stock, amount, choices) {
  /** @type {Record<string, import("selficient/plan-store.js").Choice | null>} */
  const changes = {};
  for (const action of countedActions(doc, stock)) {
    const reached = amount !== null && amount >= action.target;
    if (reached && choices[action.id] !== "have") {
      changes[action.id] = "have";
    } else if (!reached && choices[action.id] === "have") {
      changes[action.id] = null;
    }
  }
  return changes;
}

/**
 * The actions on the page that one counted stock decides, with what each asks for.
 * @param {Document} doc
 * @param {string} stock
 * @returns {{ id: string, target: number }[]}
 */
function countedActions(doc, stock) {
  /** @type {{ id: string, target: number }[]} */
  const actions = [];
  for (const counted of doc.querySelectorAll("[data-amount]")) {
    const id = counted.closest("[data-action-row]")?.getAttribute("data-action-row");
    if (counted instanceof HTMLElement && counted.dataset.amount === stock && id) {
      actions.push({ id, target: Number(counted.dataset.amountTarget) });
    }
  }
  return actions;
}

/**
 * Sets or clears a household's figure, and brings the choices on its actions into line with it:
 * every action the figure reaches is held, and none it falls short of is. With the figure cleared,
 * none of its actions is held.
 * @param {Document} doc
 * @param {string} stock
 * @param {number | null} amount
 */
async function setFigure(doc, stock, amount) {
  const choices = await store.getChoices().catch(() => /** @type {Record<string, string>} */ ({}));
  const changes = figureChanges(doc, stock, amount, choices);
  await store.setAmounts({ [stock]: amount });
  await store.setChoices(changes);
}

/**
 * A choice on an action whose stock the household can count. "Have it" raises the figure to what
 * the action asks, if it is short of it. Taking "Have it" away, or planning or ruling out an action
 * the figure already reaches, lowers the figure to the next action down, or clears it.
 * @param {Document} doc
 * @param {HTMLElement} counted
 * @param {string} id
 * @param {import("selficient/plan-store.js").Choice | null} choice
 */
async function chooseCounted(doc, counted, id, choice) {
  const stock = counted.dataset.amount ?? "";
  const target = Number(counted.dataset.amountTarget);
  const amounts = await store.getAmounts().catch(() => /** @type {Record<string, number>} */ ({}));
  const choices = await store.getChoices().catch(() => /** @type {Record<string, string>} */ ({}));
  const figure = amounts[stock];
  if (choice === "have") {
    await setFigure(doc, stock, Math.max(figure ?? 0, target));
  } else {
    if (choices[id] === "have" || (figure !== undefined && figure >= target)) {
      const below = countedActions(doc, stock).map((action) => action.target).filter((other) => other < target);
      await setFigure(doc, stock, below.length > 0 ? Math.max(...below) : null);
    }
    await store.setChoice(id, choice);
  }
  await settle(doc);
}

/**
 * The progress bar on each tier counts what the household has. The tallies beside it, and the
 * figure, are the server's, redrawn with the board.
 * @param {Document} doc
 */
function recompute(doc) {
  for (const tier of doc.querySelectorAll("[data-tier]")) {
    const boxes = [...tier.querySelectorAll("input[data-action-id]")].filter((b) => b instanceof HTMLInputElement);
    const ticked = boxes.filter((box) => box.checked).length;
    const progress = tier.querySelector("[data-tier-progress]");
    if (progress instanceof HTMLProgressElement) {
      progress.value = ticked;
      progress.textContent = `${ticked} of ${boxes.length}`;
    }
  }
}

/**
 * What the last button changed, as it was before, so one press of Undo puts it back: the choices,
 * and any figures that were cleared with them.
 * @type {{ choices: Record<string, import("selficient/plan-store.js").Choice | null>, amounts: Record<string, number | null> } | null}
 */
let undoable = null;

/**
 * A button beside a tally: clear everything of one kind, or plan everything not decided, in its
 * stretch or across the plan. The change is made at once and the way back is offered, rather than
 * asking first.
 * @param {Document} doc
 * @param {HTMLButtonElement} button
 */
async function applyBulk(doc, button) {
  if (button.getAttribute("aria-disabled") === "true") {
    return;
  }
  const kind = button.dataset.bulk;
  const scope = button.closest("[data-tier]") ?? doc;
  const choices = await store.getChoices().catch(() => null);
  if (!choices) {
    return;
  }
  /** @type {Record<string, import("selficient/plan-store.js").Choice | null>} */
  const changes = {};
  /** @type {Record<string, import("selficient/plan-store.js").Choice | null>} */
  const before = {};
  for (const row of scope.querySelectorAll("[data-action-row]")) {
    const id = row.getAttribute("data-action-row") ?? "";
    const now = choices[id] ?? null;
    if (kind === "plan-all" ? now === null : now === kind) {
      before[id] = now;
      changes[id] = kind === "plan-all" ? "plan" : null;
    }
  }
  if (Object.keys(changes).length === 0) {
    return;
  }
  // Clearing what is held clears the count behind it, or the count would tick the action again.
  /** @type {Record<string, number | null>} */
  const figures = {};
  /** @type {Record<string, number | null>} */
  const figuresBefore = {};
  if (kind === "have") {
    const amounts = await store.getAmounts().catch(() => /** @type {Record<string, number>} */ ({}));
    for (const id of Object.keys(changes)) {
      const counted = doc.querySelector('[data-action-row="' + CSS.escape(id) + '"] [data-amount]');
      const stock = counted instanceof HTMLElement ? counted.dataset.amount ?? "" : "";
      if (stock && amounts[stock] !== undefined) {
        figures[stock] = null;
        figuresBefore[stock] = amounts[stock];
        // Its other actions go with it, wherever on the plan they sit.
        for (const action of countedActions(doc, stock)) {
          if (choices[action.id] === "have" && !(action.id in changes)) {
            before[action.id] = "have";
            changes[action.id] = null;
          }
        }
      }
    }
  }
  await store.setAmounts(figures);
  await store.setChoices(changes);
  undoable = { choices: before, amounts: figuresBefore };
  await settle(doc);
  const bar = doc.querySelector("[data-undo]");
  const said = doc.querySelector("[data-undo-said]");
  if (bar instanceof HTMLElement && said) {
    said.textContent = button.dataset.said ?? "";
    bar.hidden = false;
  }
}

/**
 * @param {Document} doc
 */
async function undo(doc) {
  const before = undoable;
  hideUndo(doc);
  if (before) {
    await store.setAmounts(before.amounts);
    await store.setChoices(before.choices);
    await settle(doc);
  }
}

/**
 * @param {Document} doc
 */
function hideUndo(doc) {
  undoable = null;
  const bar = doc.querySelector("[data-undo]");
  if (bar instanceof HTMLElement) {
    bar.hidden = true;
  }
}

/**
 * After a change made by a button rather than a box: the page follows the store, and the board is
 * told to redraw, which brings the tallies and the figure with it.
 * @param {Document} doc
 */
async function settle(doc) {
  await applyChoices(doc);
  recompute(doc);
  doc.dispatchEvent(new CustomEvent("selficient:choices"));
}

/**
 * @param {Document} doc
 */
async function exportPlan(doc) {
  const data = await store.snapshot();
  const blob = new Blob([JSON.stringify({ selficient: 3, ...data }, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const link = doc.createElement("a");
  link.href = url;
  link.download = "selficient-plan.json";
  doc.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/**
 * @param {Document} doc
 * @param {File} file
 */
async function importPlan(doc, file) {
  if (file.size > 64 * 1024) {
    notify(doc, "That file is too large to be a Selficient plan.");
    return;
  }
  try {
    const parsed = JSON.parse(await file.text());
    await store.restore(parsed);
    const form = doc.querySelector("[data-resubmit]");
    if (form instanceof HTMLFormElement) {
      await resubmitIfStored(form);
    } else {
      location.assign("/plan");
    }
  } catch (error) {
    notify(doc, error instanceof Error && error.message.startsWith("That file") ? error.message : "That file could not be read as a Selficient plan.");
  }
}

/**
 * @param {Document} doc
 * @param {string} message
 */
function notify(doc, message) {
  let out = doc.querySelector("[data-plan-notice]");
  if (!out) {
    out = doc.createElement("p");
    out.className = "callout callout--warning";
    out.setAttribute("role", "alert");
    out.setAttribute("data-plan-notice", "");
    doc.querySelector(".plan-tools")?.append(out);
  }
  out.textContent = message;
}
