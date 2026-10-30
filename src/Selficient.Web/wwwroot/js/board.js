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

  /** @type {number | undefined} */
  let pending;
  let scenario = slot.querySelector("[data-board]")?.getAttribute("data-scenario") ?? "";

  const refresh = () => {
    window.clearTimeout(pending);
    pending = window.setTimeout(() => {
      void redraw(doc, slot, echo, scenario);
    }, DEBOUNCE_MS);
  };

  doc.addEventListener("change", (event) => {
    const target = event.target;
    if (!(target instanceof HTMLInputElement)) {
      return;
    }
    if (target.dataset.actionId) {
      refresh();
    } else if (target.name === "board-source" || target.name === "board-season") {
      showSheet(slot);
    }
  });

  doc.addEventListener("click", (event) => {
    const target = event.target instanceof Element ? event.target.closest("[data-scenario-card]") : null;
    if (target instanceof HTMLButtonElement) {
      event.preventDefault();
      scenario = target.value;
      for (const card of slot.querySelectorAll("[data-scenario-card]")) {
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
  void renderDependencies(doc);
  // The server drew the board without the ticks or the dependencies this browser holds.
  refresh();
}

/**
 * @param {Document} doc
 * @param {HTMLElement} slot
 * @param {HTMLFormElement} echo
 * @param {string} scenario
 */
async function redraw(doc, slot, echo, scenario) {
  const token = slot.querySelector("input[name='__RequestVerificationToken']");
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
  if (scenario) {
    body.set("Form.Scenario", scenario);
  }
  const ticks = await store.getTicks().catch(() => []);
  for (const tick of ticks) {
    body.append("Ticks", tick);
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
  const source = currentChoice(slot, "board-source");
  const season = currentChoice(slot, "board-season");
  slot.innerHTML = html;
  restoreChoice(slot, "board-source", source);
  restoreChoice(slot, "board-season", season);
  showSheet(slot);
  updateHeadline(doc, slot);
  placeTurns(doc, slot);
}

/**
 * @param {HTMLElement} slot
 * @param {string} name
 */
function currentChoice(slot, name) {
  const checked = slot.querySelector(`input[name='${name}']:checked`);
  return checked instanceof HTMLInputElement ? checked.value : "";
}

/**
 * @param {HTMLElement} slot
 * @param {string} name
 * @param {string} value
 */
function restoreChoice(slot, name, value) {
  const input = value ? slot.querySelector(`input[name='${name}'][value='${value}']`) : null;
  if (input instanceof HTMLInputElement) {
    input.checked = true;
  }
}

/**
 * Shows the one sheet that matches the chosen source and season.
 * @param {HTMLElement} slot
 */
function showSheet(slot) {
  const source = currentChoice(slot, "board-source");
  const season = currentChoice(slot, "board-season");
  for (const sheet of slot.querySelectorAll("[data-source]")) {
    if (sheet instanceof HTMLElement) {
      sheet.hidden = !(sheet.dataset.source === source && sheet.dataset.season === season);
    }
  }
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
  if (days) {
    figure.setAttribute("value", days);
    const unit = figure.querySelector(".days-ready__unit");
    if (unit) {
      unit.textContent = days === "1" ? "day" : "days";
    }
  }
  const lede = doc.querySelector("[data-hero-scenario]");
  const name = board.dataset.scenarioName;
  if (lede && name) {
    lede.textContent = `In ${name.charAt(0).toLowerCase()}${name.slice(1)}. Tick what you already have and the figure moves. Three days is the government's ask; seven is where a storm becomes an inconvenience.`;
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
    const box = doc.querySelector(`input[data-action-id="${CSS.escape(item.dataset.turnsAction)}"]`);
    const label = box?.closest("label");
    if (label) {
      const note = doc.createElement("span");
      note.className = "action__turns";
      note.setAttribute("data-turns-for", item.dataset.turnsAction);
      note.textContent = item.textContent ?? "";
      label.append(note);
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
