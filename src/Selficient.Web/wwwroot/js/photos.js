// @ts-check
/**
 * The photos page. Three jobs, all optional: name the chosen file; tick the actions a cupboard photo
 * satisfied straight into the plan's store; and, when the household has kept their own Anthropic
 * key, send the photo from the browser to Anthropic and render the answer here, so the photo and
 * the key never touch our server. The key lives in this browser's storage and nowhere else.
 */
import * as store from "selficient/plan-store.js";

const KEY_STORAGE = "selficient.anthropic-key";
const HOUSEHOLD_STORAGE = "selficient.household";
const ANTHROPIC = "https://api.anthropic.com/v1/messages";
const MAX_EDGE = 1600;

/** @typedef {{ system: string, schema: string, user: string }} Prompt */
/** @typedef {{ model: string, kit: Prompt, boiler: Prompt, consumerUnit: Prompt, kitActions: string[] }} Prompts */

/**
 * @param {Document} doc
 */
export function initPhotos(doc) {
  // The household id goes into every form that carries one, the plan's explain form included.
  stampHousehold(doc);
  if (!doc.querySelector("[data-photo-moments]")) {
    return;
  }
  nameChosenFiles(doc);
  wireTicks(doc);
  wireOwnKey(doc);
}

/**
 * A random id this browser keeps, so the monthly allowance follows the household rather than the
 * address. It is sent with every photo as a plain form field.
 * @param {Document} doc
 */
function stampHousehold(doc) {
  let id = "";
  try {
    id = localStorage.getItem(HOUSEHOLD_STORAGE) ?? "";
    if (!/^[a-z0-9-]{8,64}$/.test(id)) {
      id = crypto.randomUUID();
      localStorage.setItem(HOUSEHOLD_STORAGE, id);
    }
  } catch {
    id = "";
  }
  for (const field of doc.querySelectorAll("[data-household-key]")) {
    if (field instanceof HTMLInputElement) {
      field.value = id;
    }
  }
}

/**
 * @param {Document} doc
 */
function nameChosenFiles(doc) {
  doc.addEventListener("change", (event) => {
    const input = event.target;
    if (!(input instanceof HTMLInputElement) || !input.matches("[data-photo-input]")) {
      return;
    }
    const name = input.closest("form")?.querySelector("[data-photo-name]");
    if (name instanceof HTMLElement) {
      name.textContent = input.files?.[0] ? `Chosen: ${input.files[0].name}` : "No photo chosen.";
    }
  });
}

/**
 * @param {Document} doc
 */
function wireTicks(doc) {
  doc.addEventListener("submit", (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || !form.matches("[data-kit-ticks]")) {
      return;
    }
    event.preventDefault();
    const ids = [...form.querySelectorAll("input[name=tick]:checked")]
      .filter((box) => box instanceof HTMLInputElement)
      .map((box) => box.value);
    const status = form.querySelector("[data-kit-ticks-status]");
    Promise.all(ids.map((id) => store.setTick(id, true)))
      .then(() => {
        if (status) {
          status.textContent = ids.length === 1 ? "Ticked on your plan." : `Ticked ${ids.length} actions on your plan.`;
        }
      })
      .catch(() => {
        if (status) {
          status.textContent = "This browser would not save the ticks. Open your plan and tick them by hand.";
        }
      });
  });
}

/**
 * @param {Document} doc
 */
function wireOwnKey(doc) {
  const input = doc.querySelector("[data-byok-key]");
  const save = doc.querySelector("[data-byok-save]");
  const forget = doc.querySelector("[data-byok-forget]");
  const status = doc.querySelector("[data-byok-status]");
  if (!(input instanceof HTMLInputElement) || !(status instanceof HTMLElement)) {
    return;
  }

  const show = () => {
    status.textContent = readKey()
      ? "A key is kept in this browser. Photos go straight from here to Anthropic and do not count against the allowance."
      : "No key kept. Photos go through our server and count against the monthly allowance.";
  };
  show();

  save?.addEventListener("click", () => {
    const key = input.value.trim();
    if (!/^sk-ant-[A-Za-z0-9_-]{20,200}$/.test(key)) {
      status.textContent = "That does not look like an Anthropic key. They start with sk-ant-.";
      return;
    }
    try {
      localStorage.setItem(KEY_STORAGE, key);
    } catch {
      status.textContent = "This browser would not keep the key.";
      return;
    }
    input.value = "";
    show();
  });

  forget?.addEventListener("click", () => {
    try {
      localStorage.removeItem(KEY_STORAGE);
    } catch {
      // Nothing to forget.
    }
    show();
  });

  doc.addEventListener("submit", (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || !form.matches("[data-photo-form]")) {
      return;
    }
    const key = readKey();
    if (!key) {
      return;
    }
    event.preventDefault();
    const kind = form.dataset.photoForm;
    const file = form.querySelector("[data-photo-input]");
    if (!(file instanceof HTMLInputElement) || !file.files?.[0] || !kind) {
      return;
    }
    void analyseHere(doc, form, kind, file.files[0], key);
  });
}

/** @returns {string | null} */
function readKey() {
  try {
    const key = localStorage.getItem(KEY_STORAGE);
    return key && /^sk-ant-[A-Za-z0-9_-]{20,200}$/.test(key) ? key : null;
  } catch {
    return null;
  }
}

/**
 * The browser-side path: shrink and re-encode the photo (which drops its metadata), fetch the
 * prompts from our server, send the photo to Anthropic with the household's key, and render the
 * answer the same way the server would have.
 * @param {Document} doc
 * @param {HTMLFormElement} form
 * @param {string} kind
 * @param {File} file
 * @param {string} key
 */
async function analyseHere(doc, form, kind, file, key) {
  const name = form.querySelector("[data-photo-name]");
  const say = (/** @type {string} */ text) => {
    if (name instanceof HTMLElement) {
      name.textContent = text;
    }
  };
  try {
    say("Reading the photo in this browser.");
    const jpeg = await reencode(file);
    const prompts = /** @type {Prompts} */ (await (await fetch("/api/ai/prompts", { signal: AbortSignal.timeout(10_000) })).json());
    const prompt = kind === "Kit" ? prompts.kit : kind === "Boiler" ? prompts.boiler : prompts.consumerUnit;
    say("Asking Anthropic with your key.");
    const response = await fetch(ANTHROPIC, {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-api-key": key,
        "anthropic-version": "2026-10-21",
        "anthropic-dangerous-direct-browser-access": "true",
      },
      body: JSON.stringify({
        model: prompts.model,
        max_tokens: 2000,
        system: prompt.system,
        output_config: { effort: "low", format: { type: "json_schema", schema: JSON.parse(prompt.schema) } },
        messages: [{ role: "user", content: [
          { type: "image", source: { type: "base64", media_type: "image/jpeg", data: jpeg } },
          { type: "text", text: prompt.user },
        ] }],
      }),
      signal: AbortSignal.timeout(90_000),
    });
    if (!response.ok) {
      say(response.status === 401 ? "Anthropic did not accept that key. Check it and keep it again." : "Anthropic could not answer just now. Try again in a moment.");
      return;
    }
    const message = /** @type {{ content?: { type: string, text?: string }[] }} */ (await response.json());
    const text = message.content?.find((block) => block.type === "text")?.text;
    if (!text) {
      say("Anthropic answered without a reading. Try another photo.");
      return;
    }
    render(doc, form, kind, JSON.parse(text), prompts.kitActions);
    say("Answered below.");
  } catch {
    say("That did not work from this browser. Forget the key to go through our server instead.");
  }
}

/**
 * Shrinks the photo on a canvas and re-encodes it as a JPEG, which keeps pixels and nothing else.
 * @param {File} file
 * @returns {Promise<string>} base64 JPEG
 */
async function reencode(file) {
  const bitmap = await createImageBitmap(file);
  const scale = Math.min(1, MAX_EDGE / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement("canvas");
  canvas.width = Math.max(1, Math.round(bitmap.width * scale));
  canvas.height = Math.max(1, Math.round(bitmap.height * scale));
  const context = canvas.getContext("2d");
  if (!context) {
    throw new Error("No canvas.");
  }
  context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  const dataUrl = canvas.toDataURL("image/jpeg", 0.8);
  return dataUrl.slice(dataUrl.indexOf(",") + 1);
}

/**
 * Renders a browser-side answer with the same shape the server renders, from data only.
 * @param {Document} doc
 * @param {HTMLFormElement} form
 * @param {string} kind
 * @param {unknown} answer
 * @param {string[]} kitActions
 */
function render(doc, form, kind, answer, kitActions) {
  const moment = form.closest("[data-photo-moment]");
  if (!(moment instanceof HTMLElement) || typeof answer !== "object" || answer === null) {
    return;
  }
  moment.nextElementSibling?.matches("[data-own-key-result]") && moment.nextElementSibling.remove();
  const section = doc.createElement("section");
  section.className = "photo-result";
  section.setAttribute("data-own-key-result", "");
  const heading = doc.createElement("h3");
  const a = /** @type {Record<string, unknown>} */ (answer);

  if (kind === "Kit" && Array.isArray(a.items)) {
    heading.textContent = "What the cupboard holds";
    section.append(heading, paragraph(doc, clip(a.summary)));
    const list = doc.createElement("ul");
    list.className = "kit-items";
    /** @type {string[]} */
    const satisfied = [];
    for (const raw of a.items.slice(0, 40)) {
      const item = /** @type {{ name?: unknown, count?: unknown, action?: unknown }} */ (raw);
      if (typeof item.name !== "string" || typeof item.count !== "number") {
        continue;
      }
      const li = doc.createElement("li");
      li.className = "kit-item";
      li.textContent = `${Math.round(item.count)} ${clip(item.name)}`;
      if (typeof item.action === "string" && kitActions.includes(item.action) && !satisfied.includes(item.action)) {
        satisfied.push(item.action);
        li.append(" ", badge(doc, "covers an action on your plan"));
      }
      list.append(li);
    }
    section.append(list);
    if (satisfied.length > 0) {
      section.append(ticksForm(doc, satisfied));
    }
  } else if (kind === "Boiler") {
    heading.textContent = "What the plate says";
    const watts = typeof a.watts === "number" ? `, ${Math.round(a.watts)} W` : "";
    section.append(heading, paragraph(doc, `${clip(a.make)} ${clip(a.model)}${watts}`), paragraph(doc, clip(a.advice)));
  } else {
    heading.textContent = "What the board shows";
    const spare = typeof a.spareWays === "number" ? `${Math.round(a.spareWays)} spare ways, ` : "";
    section.append(heading, paragraph(doc, `${spare}${clip(a.kind)}.`), paragraph(doc, clip(a.advice)));
  }
  moment.after(section);
}

/**
 * @param {Document} doc
 * @param {string[]} ids
 */
function ticksForm(doc, ids) {
  const form = doc.createElement("form");
  form.className = "kit-ticks";
  form.setAttribute("data-kit-ticks", "");
  form.append(paragraph(doc, "Tick these on your plan:"));
  for (const id of ids) {
    const label = doc.createElement("label");
    label.className = "field field--check";
    const box = doc.createElement("input");
    box.type = "checkbox";
    box.name = "tick";
    box.value = id;
    box.checked = true;
    const text = doc.createElement("span");
    text.textContent = id.replaceAll("-", " ");
    label.append(box, text);
    form.append(label);
  }
  const button = doc.createElement("button");
  button.type = "submit";
  button.className = "button button--primary";
  button.textContent = "Tick them on my plan";
  const status = doc.createElement("p");
  status.className = "field__hint";
  status.setAttribute("data-kit-ticks-status", "");
  status.setAttribute("aria-live", "polite");
  form.append(button, status);
  return form;
}

/**
 * @param {Document} doc
 * @param {string} text
 */
function paragraph(doc, text) {
  const p = doc.createElement("p");
  p.textContent = text;
  return p;
}

/**
 * @param {Document} doc
 * @param {string} text
 */
function badge(doc, text) {
  const span = doc.createElement("span");
  span.className = "kit-item__match";
  span.textContent = text;
  return span;
}

/** @param {unknown} value */
function clip(value) {
  return typeof value === "string" ? value.trim().slice(0, 240) : "";
}
