// @ts-check
/**
 * Native <dialog> wiring: any element with data-open-dialog="id" opens that dialog modally, and a
 * data-close button inside a dialog closes it. The platform handles focus trapping, Escape, and the
 * backdrop. An opener may be a link to a page that says the same thing, so the control still goes
 * somewhere when this script has not run; once it has, the link opens the dialog instead.
 */

/**
 * @param {Document} doc
 */
export function initDialogs(doc) {
  doc.addEventListener("click", (event) => {
    const target = /** @type {HTMLElement | null} */ (event.target);
    const closer = target?.closest("dialog [data-close]");
    if (closer instanceof HTMLElement) {
      closer.closest("dialog")?.close();
      return;
    }
    const opener = target?.closest("[data-open-dialog]");
    if (!(opener instanceof HTMLElement)) {
      return;
    }
    const id = opener.dataset.openDialog;
    const dialog = id ? doc.getElementById(id) : null;
    if (dialog instanceof HTMLDialogElement) {
      event.preventDefault();
      if (!dialog.open) {
        dialog.showModal();
      }
    }
  });
}
