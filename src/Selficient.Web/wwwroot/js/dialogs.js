// @ts-check
/**
 * Native <dialog> wiring: any element with data-open-dialog="id" opens that dialog modally. The
 * platform handles focus trapping, Escape, and the backdrop.
 */

/**
 * @param {Document} doc
 */
export function initDialogs(doc) {
  doc.addEventListener("click", (event) => {
    const target = /** @type {HTMLElement | null} */ (event.target);
    const opener = target?.closest("[data-open-dialog]");
    if (!(opener instanceof HTMLElement)) {
      return;
    }
    const id = opener.dataset.openDialog;
    const dialog = id ? doc.getElementById(id) : null;
    if (dialog instanceof HTMLDialogElement && !dialog.open) {
      dialog.showModal();
    }
  });
}
