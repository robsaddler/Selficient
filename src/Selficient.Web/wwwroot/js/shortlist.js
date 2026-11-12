// @ts-check
/**
 * Saved for later. Beside each pick on the plan is a button that keeps the offer's number with the
 * plan, in this browser; a line above the plan says how many are saved and leads to the page that
 * lists them by shop. The list is part of the plan, so it is exported, copied to another device
 * and kept in step by an account with the rest of it.
 *
 * The page that lists them is drawn by the server from its own catalogue. This posts the numbers
 * and nothing else, and takes off the list whatever the catalogue no longer shows.
 */
import * as store from "selficient/plan-store.js";

/**
 * @param {Document} doc
 */
export async function initShortlist(doc) {
  const buttons = /** @type {HTMLElement[]} */ ([...doc.querySelectorAll("[data-shortlist]")].filter((b) => b instanceof HTMLElement && /^[0-9]{1,18}$/.test(b.dataset.shortlist ?? "")));
  const link = doc.querySelector("[data-shortlist-link]");

  const show = async () => {
    const saved = await store.getShortlist().catch(() => /** @type {string[]} */ ([]));
    for (const button of buttons) {
      const on = saved.includes(button.dataset.shortlist ?? "");
      button.hidden = false;
      button.setAttribute("aria-pressed", String(on));
      for (const says of button.querySelectorAll("[data-shortlist-says]")) {
        says.toggleAttribute("hidden", says.getAttribute("data-shortlist-says") !== (on ? "on" : "off"));
      }
    }
    if (link instanceof HTMLElement) {
      link.hidden = saved.length === 0;
      const count = link.querySelector("[data-shortlist-count]");
      if (count) {
        count.textContent = String(saved.length);
      }
    }
  };

  if (buttons.length > 0 || link) {
    doc.addEventListener("click", (event) => {
      const button = event.target instanceof Element ? event.target.closest("[data-shortlist]") : null;
      if (button instanceof HTMLElement && buttons.includes(button)) {
        void store.setShortlisted(button.dataset.shortlist ?? "", button.getAttribute("aria-pressed") !== "true").then(show);
      }
    });
    await show();
    // Another tab, a copy received or the account may change the list under this page.
    store.onChange(() => void show());
  }

  await initPage(doc);
}

/**
 * The page that lists what was saved. With nothing drawn yet it posts the list; with a list drawn
 * that is not the one this browser holds, it posts the one it holds; and whatever the catalogue
 * no longer shows comes off.
 * @param {Document} doc
 */
async function initPage(doc) {
  const page = doc.querySelector("[data-shortlist-page]");
  const form = doc.querySelector("[data-shortlist-form]");
  const field = doc.querySelector("[data-shortlist-ids]");
  if (!(page instanceof HTMLElement) || !(form instanceof HTMLFormElement) || !(field instanceof HTMLInputElement)) {
    return;
  }
  const list = (/** @type {string | undefined} */ text) => (text ? text.split(",") : []);
  const asked = list(page.dataset.shortlistAsked);
  const known = list(page.dataset.shortlistKnown);

  /** @param {string[]} ids */
  const draw = (ids) => {
    if (ids.length === 0) {
      // Nothing to post: the page as it is with nothing saved.
      location.replace("/plan/shortlist");
      return;
    }
    field.value = ids.join(",");
    form.requestSubmit();
  };

  const saved = await store.getShortlist().catch(() => /** @type {string[]} */ ([]));
  const sameAsAsked = saved.length === asked.length && saved.every((id) => asked.includes(id));
  if (!sameAsAsked) {
    if (saved.length > 0 || asked.length > 0) {
      draw(saved);
    }
    return;
  }
  if (known.length !== asked.length) {
    await store.saveShortlist(saved.filter((id) => known.includes(id)));
  }

  page.addEventListener("click", (event) => {
    const remove = event.target instanceof Element ? event.target.closest("[data-shortlist-remove]") : null;
    if (remove instanceof HTMLElement) {
      const id = remove.dataset.shortlistRemove ?? "";
      void store.setShortlisted(id, false).then(() => store.getShortlist()).then(draw);
    }
  });
}
