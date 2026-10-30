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
  await applyTicks(doc);
  recompute(doc);

  doc.addEventListener("change", (event) => {
    const box = event.target;
    if (box instanceof HTMLInputElement && box.dataset.actionId) {
      void store.setTick(box.dataset.actionId, box.checked).then(() => recompute(doc));
    }
  });

  store.onChange(() => {
    void applyTicks(doc).then(() => recompute(doc));
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
 * @param {Document} doc
 */
async function applyTicks(doc) {
  const ticks = new Set(await store.getTicks().catch(() => []));
  for (const box of doc.querySelectorAll("input[data-action-id]")) {
    if (box instanceof HTMLInputElement && box.dataset.actionId) {
      box.checked = ticks.has(box.dataset.actionId);
    }
  }
}

/**
 * The progress bar on each tier. The figure itself is the board's, drawn by the server.
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
 * @param {Document} doc
 */
async function exportPlan(doc) {
  const data = await store.snapshot();
  const blob = new Blob([JSON.stringify({ selficient: 2, ...data }, null, 2)], { type: "application/json" });
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
