// @ts-check
/**
 * The board on the plan page. The server draws it; this script redraws it when a tick changes, a
 * scenario is chosen or something the household relies on is added, by posting the profile, the
 * ticks and the dependencies to /plan/board and swapping the fragment in. The source and season
 * switches only show and hide sheets the server has already drawn. Without this script the board
 * for what the house has, in the scenario's own season, is the one on the page.
 */
import * as store from "selficient/plan-store.js";

const DEBOUNCE_MS = 350;

/**
 * @param {Document} doc
 */
export function initBoard(doc) {
  const slot = doc.querySelector("[data-board-slot]");
  const echo = doc.querySelector("[data-profile-echo]");
  if (!(slot instanceof HTMLElement) || !(echo instanceof HTMLFormElement)) {
    return;
  }

  // The lantern pulses once when the figure moves; the attribute comes off when the keyframe ends.
  const plan = doc.querySelector("[data-plan]");
  plan?.addEventListener("animationend", (event) => {
    if (event.target instanceof HTMLElement && event.target.classList.contains("glow")) {
      plan.removeAttribute("data-pulse");
    }
  });

  /** @type {number | undefined} */
  let pending;
  const board = slot.querySelector("[data-board]");
  let scenario = board?.getAttribute("data-scenario") ?? "";
  let source = board?.getAttribute("data-source") ?? "";
  let season = board?.getAttribute("data-season") ?? "";
  // "Show only what changes" narrows a compared board to the needs drawn twice. A redraw brings a
  // fresh switch, so the choice is kept here and put back.
  let onlyChanges = false;

  const refresh = () => {
    window.clearTimeout(pending);
    pending = window.setTimeout(() => {
      void redraw(doc, slot, echo, { scenario, source, season }).then(() => {
        const toggle = slot.querySelector("[data-board-only-changes]");
        if (toggle instanceof HTMLInputElement) {
          toggle.checked = onlyChanges;
        }
      });
    }, DEBOUNCE_MS);
  };

  doc.addEventListener("change", (event) => {
    const target = event.target;
    if (!(target instanceof HTMLInputElement)) {
      return;
    }
    if (target.hasAttribute("data-board-only-changes")) {
      onlyChanges = target.checked;
    } else if (target.dataset.choice) {
      refresh();
    } else if (target.name === "board-source") {
      source = target.value;
      refresh();
    } else if (target.name === "board-season") {
      season = target.value;
      refresh();
    }
  });

  doc.addEventListener("click", (event) => {
    const target = event.target instanceof Element ? event.target.closest("[data-scenario-card]") : null;
    if (target instanceof HTMLButtonElement) {
      event.preventDefault();
      scenario = target.value;
      for (const card of doc.querySelectorAll("[data-scenario-card]")) {
        card.setAttribute("aria-pressed", card === target ? "true" : "false");
      }
      refresh();
    }
  });

  doc.addEventListener("submit", (event) => {
    const form = event.target;
    if (form instanceof HTMLFormElement && form.hasAttribute("data-rely-form")) {
      event.preventDefault();
      void addDependency(doc, form).then(refresh);
    }
    if (form instanceof HTMLFormElement && form.hasAttribute("data-rely-remove")) {
      event.preventDefault();
      void removeDependency(doc, form).then(refresh);
    }
  });

  store.onChange(refresh);
  // A button beside a tally changed several actions at once; plan-page.js says so.
  doc.addEventListener("selficient:choices", refresh);
  void renderDependencies(doc);
  // The server drew the board without the ticks or the dependencies this browser holds.
  refresh();
}

/**
 * @param {Document} doc
 * @param {HTMLElement} slot
 * @param {HTMLFormElement} echo
 * @param {{ scenario: string, source: string, season: string }} choice
 */
async function redraw(doc, slot, echo, choice) {
  // Any form on the page carries the token; the chips form in the hero is the nearest.
  const token = doc.querySelector("input[name='__RequestVerificationToken']");
  if (!(token instanceof HTMLInputElement)) {
    return;
  }
  const body = new FormData();
  // The echo carries the profile under bare names; the page binds them under Form.
  for (const [name, value] of new FormData(echo)) {
    if (typeof value === "string") {
      body.append(name.startsWith("Form.") ? name : `Form.${name}`, value);
    }
  }
  body.set("__RequestVerificationToken", token.value);
  if (choice.scenario) {
    body.set("Form.Scenario", choice.scenario);
  }
  if (choice.source) {
    body.set("Form.Source", choice.source);
  }
  if (choice.season) {
    body.set("Form.Season", choice.season);
  }
  // What the household has moves the figure; what it plans and what it ruled out move the tallies.
  const fields = { have: "Ticks", plan: "PlanTo", skip: "NotForMe" };
  const choices = await store.getChoices().catch(() => /** @type {Record<string, import("selficient/plan-store.js").Choice>} */ ({}));
  for (const [id, choice] of Object.entries(choices)) {
    body.append(fields[choice], id);
  }
  // The household's own figures for the stocks it counted, which decide those stocks whatever is ticked.
  const amounts = await store.getAmounts().catch(() => /** @type {Record<string, number>} */ ({}));
  for (const [id, amount] of Object.entries(amounts)) {
    body.append("Amounts[" + id + "]", String(amount));
  }
  const assumptions = await store.getAssumptions().catch(() => /** @type {import("selficient/plan-store.js").Assumptions} */ ({}));
  if (assumptions.cashRate !== undefined) {
    body.set("CashRate", String(assumptions.cashRate));
  }
  const dependencies = await store.getDependencies().catch(() => []);
  dependencies.forEach((dependency, i) => {
    body.append(`Form.Dependencies[${i}].Name`, dependency.name);
    body.append(`Form.Dependencies[${i}].Kind`, dependency.kind);
    body.append(`Form.Dependencies[${i}].Watts`, String(dependency.watts));
    body.append(`Form.Dependencies[${i}].HoursPerDay`, String(dependency.hoursPerDay));
    body.append(`Form.Dependencies[${i}].DaysOfStock`, String(dependency.daysOfStock));
    body.append(`Form.Dependencies[${i}].MustStayCold`, dependency.mustStayCold ? "true" : "false");
  });

  let response;
  try {
    response = await fetch("/plan/board", { method: "POST", body, headers: { Accept: "text/html" } });
  } catch {
    return;
  }
  if (!response.ok) {
    return;
  }
  const html = await response.text();
  slot.innerHTML = html;
  updateHeadline(doc, slot);
  updateStrip(doc, slot);
  updateChips(doc, slot);
  updateTiers(doc, slot);
  updateTallies(doc, slot);
  placeTurns(doc, slot);
  placeProgress(doc, slot);
  updateTargets(doc, slot);
}

/**
 * The figure in the hero follows the board for what the house has.
 * @param {Document} doc
 * @param {HTMLElement} slot
 */
function updateHeadline(doc, slot) {
  const board = slot.querySelector("[data-board]");
  const figure = doc.querySelector("days-ready");
  if (!(board instanceof HTMLElement) || !(figure instanceof HTMLElement)) {
    return;
  }
  const days = board.dataset.daysReady ?? "";
  if (days && figure.getAttribute("value") !== days) {
    figure.setAttribute("value", days);
    // The lantern pulses once when the figure moves; the attribute comes off when the keyframe ends.
    figure.closest("[data-plan]")?.setAttribute("data-pulse", "");
    const unit = figure.querySelector(".days-ready__unit");
    if (unit) {
      unit.textContent = days === "1" ? "day" : "days";
    }
  }
  const lede = doc.querySelector("[data-hero-scenario]");
  const name = board.dataset.scenarioName;
  const lower = name ? `${name.charAt(0).toLowerCase()}${name.slice(1)}` : "";
  if (lede && name) {
    lede.textContent = `In ${lower}. Say what you already have and the figure moves. Three days is the government's ask; seven is where a storm becomes an inconvenience.`;
  }
  // The verdict bar is the hero's shadow: same figure, same unit, same scenario.
  const barDays = doc.querySelector("[data-verdict-days]");
  const barUnit = doc.querySelector("[data-verdict-unit]");
  const barScenario = doc.querySelector("[data-verdict-scenario]");
  if (barDays && days) {
    barDays.textContent = days;
  }
  if (barUnit && days) {
    barUnit.textContent = days === "1" ? "day" : "days";
  }
  if (barScenario && name) {
    barScenario.textContent = `in ${lower}`;
  }
}

/**
 * The figure on each scenario chip follows the board, as the ticks change what every scenario would do.
 * @param {Document} doc
 * @param {HTMLElement} slot
 */
function updateChips(doc, slot) {
  for (const item of slot.querySelectorAll("[data-board-cards] li")) {
    if (!(item instanceof HTMLElement) || !item.dataset.cardId) {
      continue;
    }
    const days = item.dataset.days ?? "";
    const figure = doc.querySelector(`[data-card-days="${CSS.escape(item.dataset.cardId)}"]`);
    if (figure) {
      figure.replaceChildren(doc.createTextNode(`${days} `), Object.assign(doc.createElement("small"), { textContent: days === "1" ? "day" : "days" }));
    }
    const chip = figure?.closest("[data-scenario-card]");
    chip?.setAttribute("aria-pressed", item.dataset.selected === "true" ? "true" : "false");
  }
}

/**
 * Each tier header says where that tier takes the house; the sentence comes from the fragment.
 * @param {Document} doc
 * @param {HTMLElement} slot
 */
function updateTiers(doc, slot) {
  for (const item of slot.querySelectorAll("[data-board-tiers] li")) {
    if (!(item instanceof HTMLElement) || !item.dataset.tier) {
      continue;
    }
    const outcome = doc.querySelector(`[data-tier-outcome="${CSS.escape(item.dataset.tier)}"]`);
    if (outcome) {
      outcome.textContent = item.textContent ?? "";
    }
  }
}

/**
 * The tallies on the plan follow the board: each takes its counts, its sums and the state of its
 * buttons from the fragment's tally of the same scope. The elements stay where they are and only
 * their text and state change, so a button that was just pressed keeps the keyboard's focus.
 * @param {Document} doc
 * @param {HTMLElement} slot
 */
function updateTallies(doc, slot) {
  for (const fresh of slot.querySelectorAll("[data-board-tallies] [data-tally]")) {
    const scope = fresh.getAttribute("data-tally") ?? "";
    for (const tally of doc.querySelectorAll(`[data-tally="${CSS.escape(scope)}"]`)) {
      if (tally.closest("[data-board-tallies]")) {
        continue;
      }
      for (const item of fresh.querySelectorAll("[data-tally-kind]")) {
        const kind = item.getAttribute("data-tally-kind") ?? "";
        const mine = tally.querySelector(`[data-tally-kind="${CSS.escape(kind)}"]`);
        const figure = mine?.querySelector("[data-tally-figure]");
        const button = mine?.querySelector("button");
        if (figure) {
          figure.textContent = item.querySelector("[data-tally-figure]")?.textContent ?? "";
        }
        if (button) {
          button.setAttribute("aria-disabled", item.querySelector("button")?.getAttribute("aria-disabled") ?? "true");
        }
      }
      const standing = tally.querySelector("[data-tally-standing]");
      if (standing) {
        standing.textContent = fresh.querySelector("[data-tally-standing]")?.textContent ?? "";
      }
    }
  }
}

/**
 * The verdict bar shows once the hero has scrolled away and hides when it is back, without a scroll listener.
 * @param {Document} doc
 */
export function initVerdictBar(doc) {
  const hero = doc.querySelector("[data-hero]");
  const bar = doc.querySelector("[data-verdict]");
  if (!(hero instanceof HTMLElement) || !(bar instanceof HTMLElement) || !("IntersectionObserver" in window)) {
    return;
  }
  const observer = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      // Shown only once the whole hero is above the viewport, so a tall viewport (or a full-page capture) never shows it over the hero.
      const shown = !entry.isIntersecting && entry.boundingClientRect.bottom < 0;
      bar.hidden = !shown;
      bar.setAttribute("aria-hidden", shown ? "false" : "true");
    }
  }, { threshold: 0 });
  observer.observe(hero);
}

/**
 * The strip under the figure follows the board for what the house has: each bar takes its days and
 * its state from the fragment, and a bar for a need the house no longer has is hidden.
 * @param {Document} doc
 * @param {HTMLElement} slot
 */
function updateStrip(doc, slot) {
  const bars = new Map();
  for (const item of slot.querySelectorAll("[data-board-strip] li")) {
    if (item instanceof HTMLElement && item.dataset.stripNeed) {
      bars.set(item.dataset.stripNeed, item);
    }
  }
  // The hero's strip and the verdict bar's both follow.
  for (const bar of doc.querySelectorAll("[data-strip] [data-need]")) {
    if (!(bar instanceof HTMLElement) || !bar.dataset.need) {
      continue;
    }
    const item = bars.get(bar.dataset.need);
    bar.hidden = item === undefined;
    if (item === undefined) {
      continue;
    }
    bar.dataset.days = item.dataset.days ?? "0";
    bar.dataset.reduced = item.dataset.reduced ?? "false";
    bar.dataset.throughout = item.dataset.throughout ?? "false";
    const figure = bar.querySelector("[data-strip-figure]");
    if (figure) {
      figure.textContent = item.dataset.days ?? "0";
    }
    const sentence = bar.querySelector("[data-strip-sentence]");
    if (sentence) {
      sentence.textContent = item.textContent ?? "";
    }
  }
}

/**
 * Each untaken action says which cells it would turn.
 * @param {Document} doc
 * @param {HTMLElement} slot
 */
function placeTurns(doc, slot) {
  for (const old of doc.querySelectorAll("[data-turns-for]")) {
    old.remove();
  }
  for (const item of slot.querySelectorAll("[data-board-turns] li")) {
    if (!(item instanceof HTMLElement) || !item.dataset.turnsAction) {
      continue;
    }
    const row = doc.querySelector(`[data-action-row="${CSS.escape(item.dataset.turnsAction)}"]`);
    const choice = row?.querySelector(".choice");
    if (row && choice) {
      const note = doc.createElement("span");
      note.className = "action__turns";
      note.setAttribute("data-turns-for", item.dataset.turnsAction);
      note.textContent = item.textContent ?? "";
      // Under the choice and above the picks, where the row's grid puts it.
      choice.after(note);
    }
  }
}

/**
 * What each counted action asks for can move with an assumption (the cash rate), so each box takes
 * its target and the sentence beside it from the fragment. plan-page.js is then told, because a
 * figure that fell short of the old target may reach the new one.
 * @param {Document} doc
 * @param {HTMLElement} slot
 */
function updateTargets(doc, slot) {
  const list = slot.querySelector("[data-board-targets]");
  if (!(list instanceof HTMLElement)) {
    return;
  }
  let moved = false;
  for (const item of list.querySelectorAll("li")) {
    const action = item.dataset.targetAction ?? "";
    const counted = doc.querySelector('[data-action-row="' + CSS.escape(action) + '"] [data-amount]');
    if (!(counted instanceof HTMLElement)) {
      continue;
    }
    moved = moved || counted.dataset.amountTarget !== item.dataset.target;
    counted.dataset.amountTarget = item.dataset.target ?? "";
    const hint = counted.querySelector(".amount__hint");
    if (hint) {
      hint.textContent = item.textContent ?? "";
    }
  }
  const says = doc.querySelector("[data-assume-cash-says]");
  if (says) {
    says.textContent = list.dataset.cashSays ?? "";
  }
  if (moved) {
    doc.dispatchEvent(new CustomEvent("selficient:targets"));
  }
}

/**
 * Each action the household put a figure towards says how far the figure goes.
 * @param {Document} doc
 * @param {HTMLElement} slot
 */
function placeProgress(doc, slot) {
  /** @type {Map<string, string>} */
  const said = new Map();
  for (const item of slot.querySelectorAll("[data-board-progress] li")) {
    if (item instanceof HTMLElement && item.dataset.progressAction) {
      said.set(item.dataset.progressAction, item.textContent ?? "");
    }
  }
  for (const line of doc.querySelectorAll("[data-progress-for]")) {
    if (line instanceof HTMLElement) {
      const sentence = said.get(line.dataset.progressFor ?? "") ?? "";
      line.textContent = sentence;
      line.hidden = sentence === "";
    }
  }
}

/**
 * @param {Document} doc
 * @param {HTMLFormElement} form
 */
async function addDependency(doc, form) {
  const data = new FormData(form);
  const name = String(data.get("Form.Dependencies[0].Name") ?? "").trim();
  if (!name) {
    return;
  }
  const dependency = {
    name: name.slice(0, 40),
    kind: String(data.get("Form.Dependencies[0].Kind") ?? "PoweredDevice"),
    watts: Number(data.get("Form.Dependencies[0].Watts") ?? 0),
    hoursPerDay: Number(data.get("Form.Dependencies[0].HoursPerDay") ?? 0),
    daysOfStock: Number(data.get("Form.Dependencies[0].DaysOfStock") ?? 0),
    mustStayCold: data.get("Form.Dependencies[0].MustStayCold") === "true",
  };
  const note = doc.querySelector("[data-rely-note]");
  if (!store.isDependency(dependency)) {
    say(note, "Watts go up to 3,000, hours a day to 24 and days in the house to 365, in whole numbers.");
    return;
  }
  const existing = await store.getDependencies().catch(() => []);
  if (existing.length >= 12) {
    say(note, "Twelve is the most the board will carry. Remove one to add another.");
    return;
  }
  say(note, "");
  await store.saveDependencies([...existing, dependency]);
  form.reset();
  await renderDependencies(doc);
}

/**
 * @param {Document} doc
 * @param {HTMLFormElement} form
 */
async function removeDependency(doc, form) {
  const index = Number(form.getAttribute("data-rely-remove"));
  const existing = await store.getDependencies().catch(() => []);
  await store.saveDependencies(existing.filter((_, i) => i !== index));
  await renderDependencies(doc);
}

/**
 * The list of what the household relies on, from the store.
 * @param {Document} doc
 */
async function renderDependencies(doc) {
  const list = doc.querySelector("[data-rely-list]");
  if (!(list instanceof HTMLElement)) {
    return;
  }
  const dependencies = await store.getDependencies().catch(() => []);
  list.replaceChildren();
  dependencies.forEach((dependency, i) => {
    const item = doc.createElement("li");
    const text = doc.createElement("span");
    text.textContent = describe(dependency);
    const form = doc.createElement("form");
    form.setAttribute("data-rely-remove", String(i));
    const button = doc.createElement("button");
    button.type = "submit";
    button.className = "button button--quiet";
    button.textContent = "Remove";
    form.append(button);
    item.append(text, form);
    list.append(item);
  });
  list.hidden = dependencies.length === 0;
}

/**
 * @param {import("selficient/plan-store.js").Dependency} dependency
 */
function describe(dependency) {
  if (dependency.kind === "PoweredDevice") {
    return `${dependency.name}: ${dependency.watts} W for ${dependency.hoursPerDay} hours a day`;
  }
  const cold = dependency.mustStayCold ? ", kept cold" : "";
  return `${dependency.name}: ${dependency.daysOfStock} days in the house${cold}`;
}

/**
 * Shows a sentence under the form, or hides the line when there is nothing to say.
 * @param {Element | null} note
 * @param {string} sentence
 */
function say(note, sentence) {
  if (!(note instanceof HTMLElement)) {
    return;
  }
  note.textContent = sentence;
  note.hidden = sentence === "";
}
