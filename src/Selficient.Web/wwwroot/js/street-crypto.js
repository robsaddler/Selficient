// @ts-check
/**
 * The cryptography of a street group, all of it in the browser. The server is sent only what
 * these functions seal, and is never sent a key.
 *
 * A group has a key that every member holds, which arrives in the fragment of the invitation.
 * What the whole group may read is sealed with it. Each home also has a key pair. What only a
 * home's closer circle may read is sealed with a fresh key, and that key is wrapped once for each
 * chosen home with a key the two homes derive between them, among decoys, so nobody who is not in
 * the circle can read it, tell who is in it, or tell how many are.
 */

const encoder = new TextEncoder();
const decoder = new TextDecoder();

/** The offers a home can make. A closed list: anything else in a record is dropped when it is read. */
export const OFFERS = ["warm-room", "hot-food", "phone-charging", "cold-storage", "drinking-water", "light", "news", "first-aid", "lift"];

export const MAX_HOUSE = 12;
export const MAX_NAME = 30;
export const MAX_GROUP_NAME = 40;
const MAX_WRAPS = 104;
const WRAP_BYTES = 60;
const CLOSE_INFO = "selficient street closer circle";

/** @typedef {"street" | "close"} Circle */
/** @typedef {{ priv: string, pub: string }} Identity */
/** @typedef {{ house: string, name: string, offers: Record<string, Circle>, close: string[] }} Sharing */
/** @typedef {{ v: 1, priv: string, pub: string, house: string, name: string, offers: Record<string, Circle>, close: string[], sealedFor: Record<string, string> }} Keep */
/** @typedef {{ house: string, pub: string, name: string | null, offers: string[], close: string[] }} Home */

/**
 * @param {Uint8Array} bytes
 * @returns {string} standard base64, which is what the server checks for
 */
export function toBase64(bytes) {
  let binary = "";
  for (const b of bytes) {
    binary += String.fromCharCode(b);
  }
  return btoa(binary);
}

/**
 * @param {unknown} text
 * @returns {Uint8Array<ArrayBuffer> | null}
 */
export function fromBase64(text) {
  if (typeof text !== "string" || !/^[A-Za-z0-9+/]*={0,2}$/.test(text)) {
    return null;
  }
  try {
    return Uint8Array.from(atob(text), (c) => c.charCodeAt(0));
  } catch {
    return null;
  }
}

/**
 * @param {number} length
 * @returns {Uint8Array<ArrayBuffer>}
 */
export function randomBytes(length) {
  return crypto.getRandomValues(new Uint8Array(length));
}

/**
 * The SHA-256 of the invitation's pass, which is all the server ever holds of it.
 * @param {Uint8Array<ArrayBuffer>} pass
 * @returns {Promise<string>}
 */
export async function passHash(pass) {
  return toBase64(new Uint8Array(await crypto.subtle.digest("SHA-256", pass)));
}

/**
 * @param {Uint8Array<ArrayBuffer>} keyBytes
 * @returns {Promise<CryptoKey>}
 */
function aesKey(keyBytes) {
  return crypto.subtle.importKey("raw", keyBytes, "AES-GCM", false, ["encrypt", "decrypt"]);
}

/**
 * Seals bytes with a key: a fresh nonce, then the ciphertext and its tag.
 * @param {Uint8Array<ArrayBuffer>} keyBytes
 * @param {Uint8Array<ArrayBuffer>} plain
 * @returns {Promise<string>}
 */
export async function sealBytes(keyBytes, plain) {
  const iv = randomBytes(12);
  const sealed = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv }, await aesKey(keyBytes), plain));
  const out = new Uint8Array(iv.length + sealed.length);
  out.set(iv);
  out.set(sealed, iv.length);
  return toBase64(out);
}

/**
 * Opens what sealBytes sealed, or answers null: the wrong key, or anything that has been altered.
 * @param {Uint8Array<ArrayBuffer>} keyBytes
 * @param {unknown} sealed
 * @returns {Promise<Uint8Array<ArrayBuffer> | null>}
 */
export async function openBytes(keyBytes, sealed) {
  const bytes = fromBase64(sealed);
  if (bytes === null || bytes.length < 28) {
    return null;
  }
  try {
    return new Uint8Array(await crypto.subtle.decrypt({ name: "AES-GCM", iv: bytes.slice(0, 12) }, await aesKey(keyBytes), bytes.slice(12)));
  } catch {
    return null;
  }
}

/**
 * @param {Uint8Array<ArrayBuffer>} keyBytes
 * @param {unknown} value
 * @returns {Promise<string>}
 */
export function seal(keyBytes, value) {
  return sealBytes(keyBytes, encoder.encode(JSON.stringify(value)));
}

/**
 * @param {Uint8Array<ArrayBuffer>} keyBytes
 * @param {unknown} sealed
 * @returns {Promise<unknown>} what was sealed, or null
 */
export async function open(keyBytes, sealed) {
  const plain = await openBytes(keyBytes, sealed);
  if (plain === null) {
    return null;
  }
  try {
    return JSON.parse(decoder.decode(plain));
  } catch {
    return null;
  }
}

/**
 * A home's key pair, made once and kept, sealed, with its membership.
 * @returns {Promise<Identity>}
 */
export async function newIdentity() {
  const pair = await crypto.subtle.generateKey({ name: "ECDH", namedCurve: "P-256" }, true, ["deriveBits"]);
  return {
    priv: toBase64(new Uint8Array(await crypto.subtle.exportKey("pkcs8", pair.privateKey))),
    pub: toBase64(new Uint8Array(await crypto.subtle.exportKey("raw", pair.publicKey))),
  };
}

/**
 * The key two homes share, which each can work out from its own private key and the other's public
 * one and nobody else can work out at all. Null when either key is not a key.
 * @param {string} priv
 * @param {string} pub
 * @param {string} groupId
 * @returns {Promise<Uint8Array<ArrayBuffer> | null>}
 */
export async function pairKey(priv, pub, groupId) {
  const privBytes = fromBase64(priv);
  const pubBytes = fromBase64(pub);
  if (privBytes === null || pubBytes === null || pubBytes.length !== 65) {
    return null;
  }
  try {
    const mine = await crypto.subtle.importKey("pkcs8", privBytes, { name: "ECDH", namedCurve: "P-256" }, false, ["deriveBits"]);
    const theirs = await crypto.subtle.importKey("raw", pubBytes, { name: "ECDH", namedCurve: "P-256" }, false, []);
    const shared = await crypto.subtle.deriveBits({ name: "ECDH", public: theirs }, mine, 256);
    const material = await crypto.subtle.importKey("raw", shared, "HKDF", false, ["deriveBits"]);
    return new Uint8Array(await crypto.subtle.deriveBits({ name: "HKDF", hash: "SHA-256", salt: encoder.encode(groupId), info: encoder.encode(CLOSE_INFO) }, material, 256));
  } catch {
    return null;
  }
}

/**
 * Text a person typed, as it is kept: one line, trimmed, no longer than it may be.
 * @param {unknown} value
 * @param {number} most
 * @returns {string}
 */
export function tidy(value, most) {
  // eslint-disable-next-line no-control-regex -- control characters are exactly what is being taken out
  return typeof value === "string" ? value.replace(/[\u0000-\u001f\u007f]/g, " ").replace(/\s+/g, " ").trim().slice(0, most) : "";
}

/**
 * The offers in a list that are offers, once each.
 * @param {unknown} list
 * @returns {string[]}
 */
function offersIn(list) {
  return Array.isArray(list) ? OFFERS.filter((offer) => list.includes(offer)) : [];
}

/**
 * What a home publishes: one part sealed for the whole group, one for its closer circle, and the
 * closer circle's key wrapped for each chosen home, among decoys.
 * @param {Uint8Array<ArrayBuffer>} groupKey
 * @param {string} groupId
 * @param {Identity} identity
 * @param {Sharing} sharing
 * @param {Map<string, string>} pubs the public key of every other home, by member id
 * @returns {Promise<{ record: string, sealedFor: Record<string, string> }>}
 */
export async function makeRecord(groupKey, groupId, identity, sharing, pubs) {
  const forStreet = OFFERS.filter((offer) => sharing.offers[offer] === "street");
  const forClose = OFFERS.filter((offer) => sharing.offers[offer] === "close");
  const street = await seal(groupKey, { house: tidy(sharing.house, MAX_HOUSE), pub: identity.pub, offers: forStreet });
  const closeKey = randomBytes(32);
  const close = await seal(closeKey, { name: tidy(sharing.name, MAX_NAME), offers: forClose });
  /** @type {string[]} */
  const wraps = [];
  /** @type {Record<string, string>} */
  const sealedFor = {};
  for (const member of sharing.close) {
    const pub = pubs.get(member);
    const shared = pub === undefined ? null : await pairKey(identity.priv, pub, groupId);
    if (pub !== undefined && shared !== null) {
      wraps.push(await sealBytes(shared, closeKey));
      sealedFor[member] = pub;
    }
  }
  // Decoys, so the number of real ones is not told: up to the next eight, and always at least eight.
  const padded = Math.min(MAX_WRAPS, Math.max(8, Math.ceil(wraps.length / 8) * 8));
  while (wraps.length < padded) {
    wraps.push(toBase64(randomBytes(WRAP_BYTES)));
  }
  shuffle(wraps);
  return { record: toBase64(encoder.encode(JSON.stringify({ v: 1, s: street, c: close, w: wraps }))), sealedFor };
}

/**
 * @param {string[]} list shuffled in place, with the browser's own randomness
 */
function shuffle(list) {
  for (let i = list.length - 1; i > 0; i--) {
    const j = (randomBytes(4).reduce((sum, b) => sum * 256 + b, 0)) % (i + 1);
    const a = list[i] ?? "";
    list[i] = list[j] ?? "";
    list[j] = a;
  }
}

/**
 * Reads another home's record as this home may: what it shares with the whole group, and what it
 * shares with its closer circle if this home is in it. Null when the record is not one, or was
 * sealed with some other key. Everything in the answer has been checked: a record is written by a
 * neighbour's browser, which is not a thing to trust.
 * @param {Uint8Array<ArrayBuffer>} groupKey
 * @param {string} groupId
 * @param {Identity} identity
 * @param {unknown} record
 * @returns {Promise<Home | null>}
 */
export async function readHome(groupKey, groupId, identity, record) {
  const outerBytes = fromBase64(record);
  /** @type {unknown} */
  let outer = null;
  try {
    outer = outerBytes === null ? null : JSON.parse(decoder.decode(outerBytes));
  } catch {
    outer = null;
  }
  if (typeof outer !== "object" || outer === null) {
    return null;
  }
  const { s, c, w } = /** @type {{ s?: unknown, c?: unknown, w?: unknown }} */ (outer);
  const street = await open(groupKey, s);
  if (typeof street !== "object" || street === null) {
    return null;
  }
  const { house, pub, offers } = /** @type {{ house?: unknown, pub?: unknown, offers?: unknown }} */ (street);
  const pubBytes = fromBase64(pub);
  if (typeof pub !== "string" || pubBytes === null || pubBytes.length !== 65) {
    return null;
  }
  /** @type {Home} */
  const home = { house: tidy(house, MAX_HOUSE), pub, name: null, offers: offersIn(offers), close: [] };
  const shared = await pairKey(identity.priv, pub, groupId);
  const wraps = Array.isArray(w) ? w.slice(0, MAX_WRAPS) : [];
  for (const wrap of shared === null ? [] : wraps) {
    const closeKey = await openBytes(/** @type {Uint8Array<ArrayBuffer>} */ (shared), wrap);
    if (closeKey !== null && closeKey.length === 32) {
      const inner = await open(closeKey, c);
      if (typeof inner === "object" && inner !== null) {
        const { name, offers: closeOffers } = /** @type {{ name?: unknown, offers?: unknown }} */ (inner);
        home.name = tidy(name, MAX_NAME);
        home.close = offersIn(closeOffers);
      }
      break;
    }
  }
  return home;
}

/**
 * What a home's own devices keep, as it is read back: null unless it is whole.
 * @param {unknown} value
 * @returns {Keep | null}
 */
export function asKeep(value) {
  if (typeof value !== "object" || value === null) {
    return null;
  }
  const { priv, pub, house, name, offers, close, sealedFor } = /** @type {Record<string, unknown>} */ (value);
  if (typeof priv !== "string" || typeof pub !== "string" || fromBase64(priv) === null || fromBase64(pub)?.length !== 65) {
    return null;
  }
  /** @type {Record<string, Circle>} */
  const chosen = {};
  for (const offer of OFFERS) {
    const circle = typeof offers === "object" && offers !== null ? /** @type {Record<string, unknown>} */ (offers)[offer] : undefined;
    if (circle === "street" || circle === "close") {
      chosen[offer] = circle;
    }
  }
  /** @type {Record<string, string>} */
  const sealed = {};
  for (const [member, key] of Object.entries(typeof sealedFor === "object" && sealedFor !== null ? sealedFor : {})) {
    if (typeof key === "string") {
      sealed[member] = key;
    }
  }
  return {
    v: 1,
    priv,
    pub,
    house: tidy(house, MAX_HOUSE),
    name: tidy(name, MAX_NAME),
    offers: chosen,
    close: Array.isArray(close) ? close.filter((member) => typeof member === "string").slice(0, 100) : [],
    sealedFor: sealed,
  };
}
