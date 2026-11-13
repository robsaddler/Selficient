// @ts-check
/**
 * The wizard, when this browser already keeps a plan: the form is filled in with the answers that
 * plan was built from, so a household can change one answer without typing the other twenty again.
 * Nothing is posted or fetched; the answers come from the browser's own store and go into the boxes.
 * A postcode handed over from the landing page that is not the stored one is a different house, and
 * the form is left empty for it.
 */
import * as store from "selficient/plan-store.js";

/**
 * @param {Document} doc
 */
export async function initWizard(doc) {
  const form = doc.querySelector("[data-wizard-start] [data-plan-form]");
  if (!(form instanceof HTMLFormElement)) {
    return;
  }
  const profile = await store.getProfile().catch(() => null);
  if (!profile) {
    return;
  }
  // The figures this household gave for things it has go with its answers, so the plan that comes
  // back is built from them the first time.
  await store.addOwnFigures(form);
  const typed = control(form, "Postcode");
  const handed = typed instanceof HTMLInputElement ? squash(typed.value) : "";
  if (handed !== "" && handed !== squash(profile.Postcode ?? "")) {
    return;
  }
  for (const [field, value] of Object.entries(profile)) {
    fill(control(form, field), value);
  }
  const kept = doc.querySelector("[data-wizard-kept]");
  if (kept instanceof HTMLElement) {
    kept.hidden = false;
  }
  form.setAttribute("data-wizard-filled", "");
}

/**
 * The control for one answer. The tag helper gives each the id "Form_" and the property's name.
 * @param {HTMLFormElement} form
 * @param {string} field
 * @returns {Element | null}
 */
function control(form, field) {
  return /^[A-Za-z]+$/.test(field) ? form.querySelector("#Form_" + field) : null;
}

/**
 * Puts a stored answer into its control: a tick for true, an option only if the list has it, and
 * the text otherwise. An answer with no control, or one the list does not offer, changes nothing.
 * @param {Element | null} target
 * @param {string} value
 */
function fill(target, value) {
  if (target instanceof HTMLInputElement && target.type === "checkbox") {
    target.checked = value === "true";
  } else if (target instanceof HTMLSelectElement) {
    if ([...target.options].some((option) => option.value === value)) {
      target.value = value;
    }
  } else if (target instanceof HTMLInputElement) {
    target.value = value;
  }
}

/**
 * A postcode with its spaces out and in capitals, so "np12 1gw" is "NP121GW".
 * @param {string} postcode
 */
function squash(postcode) {
  return postcode.replace(/\s+/g, "").toUpperCase();
}
