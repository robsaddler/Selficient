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
    void store.setChoice(box.value, box.checked ? box.dataset.choice : null).then(() => recompute(doc));
  });

  doc.addEventListener("click", (event) => {
    const target = event.target instanceof Element ? event.target : null;
    const bulk = target?.closest("[data-bulk]");
    if (bulk instanceof HTMLButtonElement && !bulk.closest("[data-board-tallies]")) {
      void applyBulk(doc, bulk);
    } else if (target?.closest("[data-undo-button]")) {
      void undo(doc);
    } else if (target?.closest("[data-undo-dismiss]")) {
      hideUndo(doc);
    }
  });

  store.onChange(() => {
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
 * Puts what the store holds onto the page: each action's three boxes, one at most ticked.
 * @param {Document} doc
 */
async function applyChoices(doc) {
  const choices = await store.getChoices().catch(() => /** @type {Record<string, string>} */ ({}));
  for (const row of doc.querySelectorAll("[data-action-row]")) {
    const id = row.getAttribute("data-action-row") ?? "";
    for (const box of choiceBoxes(row)) {
      box.checked = choices[id] === box.dataset.choice;
    }
  }
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
 * What the last button changed, as it was before, so one press of Undo puts it back.
 * @type {Record<string, import("selficient/plan-store.js").Choice | null> | null}
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
  await store.setChoices(changes);
  undoable = before;
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
    await store.setChoices(before);
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
