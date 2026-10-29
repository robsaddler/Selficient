// @ts-check
/**
 * <days-ready value="3.5"> — the signature figure.
 *
 * Light DOM: the server renders the figure and unit as children so the number is meaningful without
 * JavaScript and indexable. This element only animates changes and keeps the text in step with the
 * `value` attribute.
 */
export class DaysReady extends HTMLElement {
  static get observedAttributes() {
    return ["value"];
  }

  /** @type {AbortController | null} */
  #abort = null;

  /** @type {Intl.NumberFormat} */
  #format = new Intl.NumberFormat("en-GB", { maximumFractionDigits: 1 });

  connectedCallback() {
    this.#abort = new AbortController();
    this.#render(this.value, { animate: false });
    // Reveal once per page load; the CSS keyframe lifts the figure into place.
    requestAnimationFrame(() => this.setAttribute("data-revealed", ""));
  }

  disconnectedCallback() {
    this.#abort?.abort();
    this.#abort = null;
  }

  /**
   * @param {string} name
   * @param {string | null} previous
   * @param {string | null} next
   */
  attributeChangedCallback(name, previous, next) {
    if (name !== "value" || previous === next || !this.isConnected) {
      return;
    }
    this.#render(this.value, { animate: true, from: Number(previous ?? 0) });
  }

  /** The number of days, never negative, never NaN. */
  get value() {
    const raw = Number(this.getAttribute("value"));
    return Number.isFinite(raw) && raw >= 0 ? raw : 0;
  }

  set value(days) {
    this.setAttribute("value", String(days));
  }

  /**
   * @param {number} target
   * @param {{ animate: boolean, from?: number }} options
   */
  #render(target, { animate, from = 0 }) {
    const figure = this.querySelector(".days-ready__figure");
    if (!figure) {
      return;
    }

    const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
    if (!animate || reduce) {
      figure.textContent = this.#format.format(target);
      return;
    }

    const start = performance.now();
    const duration = 900;
    const signal = this.#abort?.signal;

    const step = (/** @type {number} */ now) => {
      if (signal?.aborted) {
        return;
      }
      const t = Math.min(1, (now - start) / duration);
      const eased = 1 - Math.pow(1 - t, 3);
      figure.textContent = this.#format.format(from + (target - from) * eased);
      if (t < 1) {
        requestAnimationFrame(step);
      }
    };

    requestAnimationFrame(step);
  }
}
