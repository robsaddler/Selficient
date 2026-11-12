// @ts-check
/**
 * Sending a copy of the plan to another device. The plan is encrypted here, with a key made here,
 * and only the ciphertext goes to the server, which keeps it for ten minutes and hands it over
 * once. The key travels in the link's fragment, which a browser never sends, drawn as a code for
 * the other device's camera. A copy is a copy: nothing links the two devices afterwards, and the
 * screen says so.
 */
import * as store from "selficient/plan-store.js";
import { qrSvg } from "selficient/qr.js";

/** How long the server keeps a copy, and so how long the code is shown. */
const LIFETIME_MS = 10 * 60 * 1000;
const POLL_MS = 3000;
/** The most a copy may inflate to on arrival: far more than any plan, far less than a tab's memory. */
const MAX_PLAIN_BYTES = 1024 * 1024;

/** The kinds of device a copy can say it was made on. Anything else reads as "another device". */
export const DEVICES = ["iphone", "ipad", "android-phone", "android-tablet", "windows", "mac", "chromebook", "linux", "other"];

/** @typedef {{ v: 1, made: number, from: string, plan: import("selficient/plan-store.js").Snapshot }} Envelope */

/**
 * @param {Uint8Array} bytes
 * @returns {string}
 */
export function toBase64Url(bytes) {
  let binary = "";
  for (const b of bytes) {
    binary += String.fromCharCode(b);
  }
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}

/**
 * @param {string} text
 * @returns {Uint8Array<ArrayBuffer> | null} null when the text is not base64url
 */
export function fromBase64Url(text) {
  if (!/^[A-Za-z0-9_-]*$/.test(text)) {
    return null;
  }
  try {
    const binary = atob(text.replaceAll("-", "+").replaceAll("_", "/"));
    return Uint8Array.from(binary, (c) => c.charCodeAt(0));
  } catch {
    return null;
  }
}

/** What this device is, in one of the words the other device knows how to say. */
export function deviceKind() {
  const agent = navigator.userAgent;
  if (/iPhone|iPod/.test(agent)) {
    return "iphone";
  }
  if (/iPad/.test(agent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1)) {
    return "ipad";
  }
  if (/Android/.test(agent)) {
    return /Mobile/.test(agent) ? "android-phone" : "android-tablet";
  }
  if (/CrOS/.test(agent)) {
    return "chromebook";
  }
  if (/Windows/.test(agent)) {
    return "windows";
  }
  if (/Macintosh|Mac OS X/.test(agent)) {
    return "mac";
  }
  return /Linux|X11/.test(agent) ? "linux" : "other";
}

/**
 * Encrypts an envelope with a fresh key. The first byte inside says whether the rest is gzip.
 * @param {Envelope} envelope
 * @returns {Promise<{ body: Uint8Array<ArrayBuffer>, key: Uint8Array }>} the nonce and ciphertext to post, and the key to keep
 */
export async function seal(envelope) {
  const json = new TextEncoder().encode(JSON.stringify(envelope));
  const squeezed = typeof CompressionStream === "function" ? await pipe(json, new CompressionStream("gzip"), Infinity) : null;
  const plain = new Uint8Array(1 + (squeezed ?? json).length);
  plain[0] = squeezed ? 1 : 0;
  plain.set(squeezed ?? json, 1);

  const key = await crypto.subtle.generateKey({ name: "AES-GCM", length: 256 }, true, ["encrypt"]);
  const nonce = crypto.getRandomValues(new Uint8Array(12));
  const sealed = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv: nonce }, key, plain));
  const body = new Uint8Array(nonce.length + sealed.length);
  body.set(nonce, 0);
  body.set(sealed, nonce.length);
  return { body, key: new Uint8Array(await crypto.subtle.exportKey("raw", key)) };
}

/**
 * Opens what {@link seal} made. Throws when the key is wrong, a bit has changed, the copy inflates
 * past the cap, or what is inside is not an envelope holding a plan.
 * @param {Uint8Array<ArrayBuffer>} body
 * @param {Uint8Array<ArrayBuffer>} keyBytes
 * @returns {Promise<Envelope>}
 */
export async function unseal(body, keyBytes) {
  if (body.length < 29 || keyBytes.length !== 32) {
    throw new Error("Not a copy.");
  }
  const key = await crypto.subtle.importKey("raw", keyBytes, { name: "AES-GCM" }, false, ["decrypt"]);
  const plain = new Uint8Array(await crypto.subtle.decrypt({ name: "AES-GCM", iv: body.slice(0, 12) }, key, body.slice(12)));
  /** @type {Uint8Array} */
  let json;
  if (plain[0] === 1) {
    json = await pipe(plain.slice(1), new DecompressionStream("gzip"), MAX_PLAIN_BYTES);
  } else if (plain[0] === 0) {
    json = plain.slice(1);
  } else {
    throw new Error("Not a copy.");
  }
  const envelope = /** @type {unknown} */ (JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(json)));
  if (!isEnvelope(envelope)) {
    throw new Error("Not a plan.");
  }
  return envelope;
}

/**
 * @param {unknown} value
 * @returns {value is Envelope}
 */
export function isEnvelope(value) {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const { v, made, from, plan } = /** @type {{ v?: unknown, made?: unknown, from?: unknown, plan?: unknown }} */ (value);
  return v === 1 && typeof made === "number" && Number.isFinite(made) && typeof from === "string" && DEVICES.includes(from) && store.isSnapshot(plan);
}

/**
 * Runs bytes through a compression or decompression stream, refusing to read past a cap.
 * @param {Uint8Array<ArrayBuffer>} bytes
 * @param {CompressionStream | DecompressionStream} through
 * @param {number} most
 * @returns {Promise<Uint8Array<ArrayBuffer>>}
 */
async function pipe(bytes, through, most) {
  const reader = new Blob([bytes]).stream().pipeThrough(through).getReader();
  /** @type {Uint8Array[]} */
  const chunks = [];
  let total = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) {
      break;
    }
    total += value.length;
    if (total > most) {
      await reader.cancel();
      throw new Error("Too large.");
    }
    chunks.push(value);
  }
  const out = new Uint8Array(total);
  let at = 0;
  for (const chunk of chunks) {
    out.set(chunk, at);
    at += chunk.length;
  }
  return out;
}

/**
 * The "Another device" row of the panel: the button, the code, the link, and what is said when
 * the other device has collected the copy or the code has run out.
 * @param {Document} doc
 */
export function initSendCopy(doc) {
  const row = doc.querySelector("[data-keep-another]");
  const dialog = doc.querySelector("[data-keep-dialog]");
  if (!(row instanceof HTMLElement) || !(dialog instanceof HTMLDialogElement)) {
    return;
  }
  const code = row.querySelector("[data-send-code]");
  /** @type {{ link: string, timer: number } | null} */
  let live = null;

  const stop = () => {
    if (live) {
      clearInterval(live.timer);
      live = null;
    }
  };

  /** @param {"idle" | "code" | "sent"} state @param {string} [says] */
  const show = (state, says) => {
    for (const part of row.querySelectorAll("[data-send-state]")) {
      if (part instanceof HTMLElement) {
        part.hidden = part.dataset.sendState !== state;
      }
    }
    for (const line of row.querySelectorAll("[data-send-says]")) {
      if (line instanceof HTMLElement) {
        line.hidden = line.dataset.sendSays !== says;
      }
    }
  };

  const send = async () => {
    stop();
    show("idle", "making");
    /** @type {Response} */
    let response;
    /** @type {Uint8Array} */
    let key;
    try {
      const sealed = await seal({ v: 1, made: Date.now(), from: deviceKind(), plan: await store.snapshot() });
      key = sealed.key;
      response = await fetch("/api/handoff", { method: "POST", headers: { "Content-Type": "application/octet-stream" }, body: sealed.body, cache: "no-store" });
    } catch {
      show("idle", navigator.onLine === false ? "offline" : "failed");
      return;
    }
    if (!response.ok) {
      show("idle", response.status === 429 ? "busy" : response.status === 503 ? "full" : response.status === 413 ? "large" : "failed");
      return;
    }
    const ticket = /** @type {{ id?: unknown }} */ (await response.json().catch(() => ({})));
    if (typeof ticket.id !== "string" || !/^[A-Za-z0-9_-]{22}$/.test(ticket.id)) {
      show("idle", "failed");
      return;
    }
    const id = ticket.id;
    const link = `${location.origin}/receive#${id}.${toBase64Url(key)}`;
    if (code instanceof HTMLElement) {
      code.replaceChildren(qrSvg(doc, link));
    }
    show("code");
    const until = Date.now() + LIFETIME_MS;
    const timer = setInterval(() => {
      void (async () => {
        if (Date.now() >= until) {
          stop();
          clear();
          show("idle", "expired");
          return;
        }
        const waiting = await fetch(`/api/handoff/${id}/waiting`, { cache: "no-store" }).then((r) => (r.ok ? r.json() : null)).catch(() => null);
        if (live?.timer === timer && waiting && waiting.waiting === false) {
          stop();
          clear();
          show("sent");
        }
      })();
    }, POLL_MS);
    live = { link, timer };
  };

  // The code is the key: it does not stay on the screen, or in the page, once it is spent.
  const clear = () => {
    if (code instanceof HTMLElement) {
      code.replaceChildren();
    }
  };

  row.addEventListener("click", (event) => {
    const target = event.target instanceof Element ? event.target : null;
    if (target?.closest("[data-send-copy]")) {
      void send();
    } else if (target?.closest("[data-send-link]") && live) {
      const link = live.link;
      navigator.clipboard.writeText(link).then(
        () => show("code", "copied"),
        () => show("code", "not-copied"),
      );
    }
  });

  // "Send a copy to my phone" under the first plan opens the panel and starts at once.
  doc.addEventListener("click", (event) => {
    if (event.target instanceof Element && event.target.closest("[data-send-now]") && !row.hidden) {
      void send();
    }
  });

  // Closing the panel takes the code off the screen. The copy on the server runs out by itself.
  dialog.addEventListener("close", () => {
    stop();
    clear();
    show("idle");
  });
}
