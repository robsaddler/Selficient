// @ts-check
/**
 * Passkeys: signing in with the device's own fingerprint, face or screen lock in place of a code.
 * Offered once after signing in by code, listed in the panel with a way to remove each, and on the
 * sign-in page as "Use a passkey". Everything here is offered only where the browser can do it.
 * The private key never leaves the device, and the emailed code always still works.
 *
 * A ceremony is two calls to the server: one for the options, which are handed to the browser's
 * own WebAuthn API, and one with what the authenticator answered.
 */
import { signedInAs } from "selficient/keep.js";

const HEADERS = { "Content-Type": "application/json", "X-Selficient-Sync": "1" };
const DECLINED_KEY = "selficient.passkey-declined";

/** @param {Uint8Array} bytes */
function encode(bytes) {
  let binary = "";
  for (const b of bytes) {
    binary += String.fromCharCode(b);
  }
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}

/** @param {string} text */
function decode(text) {
  const binary = atob(text.replaceAll("-", "+").replaceAll("_", "/"));
  return Uint8Array.from(binary, (c) => c.charCodeAt(0));
}

/** @param {ArrayBuffer | null | undefined} buffer */
const part = (buffer) => (buffer ? encode(new Uint8Array(buffer)) : undefined);

/**
 * The server's options as the browser's API takes them: the parts that are bytes arrive as text.
 * @param {any} options
 */
function forCreating(options) {
  return {
    ...options,
    challenge: decode(options.challenge),
    user: { ...options.user, id: decode(options.user.id) },
    excludeCredentials: (options.excludeCredentials ?? []).map((/** @type {any} */ c) => ({ ...c, id: decode(c.id) })),
  };
}

/** @param {any} options */
function forAsserting(options) {
  return {
    ...options,
    challenge: decode(options.challenge),
    allowCredentials: (options.allowCredentials ?? []).map((/** @type {any} */ c) => ({ ...c, id: decode(c.id) })),
  };
}

/**
 * What the authenticator answered, as the server reads it.
 * @param {PublicKeyCredential} credential
 */
function answerOf(credential) {
  const response = /** @type {AuthenticatorAttestationResponse & AuthenticatorAssertionResponse} */ (credential.response);
  return {
    id: credential.id,
    rawId: part(credential.rawId),
    type: credential.type,
    response: {
      clientDataJSON: part(response.clientDataJSON),
      attestationObject: part(response.attestationObject),
      authenticatorData: part(response.authenticatorData),
      signature: part(response.signature),
      userHandle: part(response.userHandle),
      transports: typeof response.getTransports === "function" ? response.getTransports() : undefined,
    },
    clientExtensionResults: credential.getClientExtensionResults(),
  };
}

/**
 * One ceremony, start to finish. Answers "ok", "cancelled" when the person dismissed the
 * browser's prompt, or the sentence the server refused with (or "failed").
 * @param {string} optionsUrl
 * @param {string} answerUrl
 * @param {(options: any) => Promise<Credential | null>} ask
 * @returns {Promise<"ok" | "cancelled" | "failed" | { message: string }>}
 */
async function ceremony(optionsUrl, answerUrl, ask) {
  try {
    const begun = await fetch(optionsUrl, { method: "POST", headers: HEADERS, cache: "no-store" });
    if (!begun.ok) {
      return refusal(begun);
    }
    const credential = await ask(await begun.json());
    if (!(credential instanceof PublicKeyCredential)) {
      return "cancelled";
    }
    const finished = await fetch(answerUrl, { method: "POST", headers: HEADERS, body: JSON.stringify(answerOf(credential)), cache: "no-store" });
    return finished.ok ? "ok" : refusal(finished);
  } catch (error) {
    // The person closed the prompt, or it timed out, or this device has no authenticator for it.
    return error instanceof DOMException && (error.name === "NotAllowedError" || error.name === "AbortError") ? "cancelled" : "failed";
  }
}

/**
 * @param {Response} response
 * @returns {Promise<"failed" | { message: string }>}
 */
async function refusal(response) {
  const body = /** @type {{ message?: unknown }} */ (await response.json().catch(() => ({})));
  return typeof body.message === "string" && body.message !== "" ? { message: body.message } : "failed";
}

const add = () => ceremony("/api/account/passkeys/options", "/api/account/passkeys", (options) => navigator.credentials.create({ publicKey: forCreating(options) }));
const signIn = () => ceremony("/api/passkey/options", "/api/passkey", (options) => navigator.credentials.get({ publicKey: forAsserting(options) }));

/**
 * @param {Document} doc
 */
export async function initPasskeys(doc) {
  if (typeof PublicKeyCredential !== "function" || !navigator.credentials) {
    return;
  }

  // The sign-in page: "Use a passkey", in place of waiting for a code.
  const use = doc.querySelector("[data-passkey-sign-in]");
  if (use instanceof HTMLElement) {
    const says = doc.querySelector("[data-passkey-sign-in-says]");
    use.hidden = false;
    use.addEventListener("click", () => {
      void signIn().then((outcome) => {
        if (outcome === "ok") {
          location.assign("/plan");
        } else if (outcome !== "cancelled" && says instanceof HTMLElement) {
          says.textContent = typeof outcome === "object" ? outcome.message : says.dataset.failed ?? "";
          says.hidden = false;
        }
      });
    });
  }

  const panel = doc.querySelector("[data-passkeys]");
  const offer = doc.querySelector("[data-passkey-offer]");
  if (!(panel instanceof HTMLElement) || (await signedInAs()) === null) {
    return;
  }
  const list = panel.querySelector("[data-passkey-list]");
  const none = panel.querySelector("[data-passkey-none]");
  const says = panel.querySelector("[data-passkey-says]");

  /** @param {string} sentence */
  const say = (sentence) => {
    if (says instanceof HTMLElement) {
      says.textContent = sentence;
      says.hidden = sentence === "";
    }
  };

  const draw = async () => {
    const kept = /** @type {{ id: number, line: string }[] | null} */ (await fetch("/api/account/passkeys", { cache: "no-store" }).then((r) => (r.ok ? r.json() : null)).catch(() => null));
    if (kept === null || !(list instanceof HTMLElement)) {
      return null;
    }
    list.replaceChildren(...kept.map((passkey) => {
      const item = doc.createElement("li");
      const line = doc.createElement("span");
      line.textContent = passkey.line;
      const remove = doc.createElement("button");
      remove.type = "button";
      remove.className = "button button--quiet";
      remove.dataset.passkeyRemove = String(passkey.id);
      // The accessible name starts with the words on the button and then says which.
      remove.textContent = panel.dataset.removeLabel ?? "";
      remove.setAttribute("aria-label", `${remove.textContent}: ${passkey.line}`);
      item.append(line, remove);
      return item;
    }));
    list.hidden = kept.length === 0;
    if (none instanceof HTMLElement) {
      none.hidden = kept.length > 0;
    }
    panel.hidden = false;
    return kept.length;
  };

  const adding = async () => {
    say("");
    const outcome = await add();
    if (outcome === "ok") {
      await draw();
      say(panel.dataset.added ?? "");
      if (offer instanceof HTMLElement) {
        offer.hidden = true;
      }
    } else if (outcome !== "cancelled") {
      say(typeof outcome === "object" ? outcome.message : panel.dataset.failed ?? "");
    }
    return outcome;
  };

  doc.addEventListener("click", (event) => {
    const target = event.target instanceof Element ? event.target : null;
    const remove = target?.closest("[data-passkey-remove]");
    if (remove instanceof HTMLElement) {
      void fetch(`/api/account/passkeys/${remove.dataset.passkeyRemove}`, { method: "DELETE", headers: HEADERS, cache: "no-store" })
        .then(() => draw())
        .then(() => say(panel.dataset.removed ?? ""))
        .catch(() => undefined);
    } else if (target?.closest("[data-passkey-add]")) {
      void adding();
    } else if (target?.closest("[data-passkey-not-now]") && offer instanceof HTMLElement) {
      offer.hidden = true;
      remember();
    }
  });

  // Offered once on a device, to a household with none: after that it is in the panel for whoever wants it.
  const count = await draw();
  if (offer instanceof HTMLElement && count === 0 && !declined()) {
    offer.hidden = false;
  }
}

function declined() {
  try {
    return localStorage.getItem(DECLINED_KEY) !== null;
  } catch {
    return true;
  }
}

function remember() {
  try {
    localStorage.setItem(DECLINED_KEY, "1");
  } catch {
    // Offered again next time, which is no harm.
  }
}
