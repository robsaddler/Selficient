// @ts-check
/**
 * "Share this figure" on the plan. The link carries the figure and the scenario and nothing else,
 * so it is safe to paste into any group chat: the landing page and its card say "our house could
 * stand for N days; how about yours?". The browser's own share sheet where there is one, the
 * clipboard where there is not. Without script the button is not there.
 */

/**
 * @param {Document} doc
 */
export function initShare(doc) {
  const button = doc.querySelector("[data-share]");
  if (!(button instanceof HTMLButtonElement)) {
    return;
  }
  button.hidden = false;
  button.addEventListener("click", () => {
    void share(doc, button);
  });
}

/**
 * @param {Document} doc
 * @param {HTMLButtonElement} button
 */
async function share(doc, button) {
  const status = doc.querySelector(button.dataset.shareStatus ?? "[data-share-status]");
  const say = (/** @type {string} */ sentence) => {
    if (status instanceof HTMLElement) {
      status.textContent = sentence;
      status.hidden = sentence === "";
    }
  };
  const url = shareUrl(doc);
  const figure = doc.querySelector("days-ready")?.getAttribute("value") ?? "1";
  const text = `Our house could stand on its own for ${figure} ${figure === "1" ? "day" : "days"}. How about yours?`;
  try {
    if (typeof navigator.share === "function") {
      await navigator.share({ title: "How many days could your house stand on its own?", text, url });
      say("Shared.");
      return;
    }
    await navigator.clipboard.writeText(url);
    say("Link copied. Paste it into the group chat.");
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      say("");
      return;
    }
    say(`The link could not be copied. It is ${url}`);
  }
}

/**
 * The address to share: the figure as the page shows it and the scenario the chips have pressed.
 * @param {Document} doc
 * @returns {string}
 */
export function shareUrl(doc) {
  const figure = doc.querySelector("days-ready")?.getAttribute("value") ?? "1";
  const pressed = doc.querySelector("[data-scenario-card][aria-pressed='true']");
  const scenario = pressed instanceof HTMLButtonElement && pressed.value !== "" ? pressed.value : "storm";
  return new URL(`/share/${encodeURIComponent(scenario)}/${encodeURIComponent(figure)}`, doc.location.origin).toString();
}
