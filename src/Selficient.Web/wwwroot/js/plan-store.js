// @ts-check
/**
 * Where the plan lives: IndexedDB, in this browser, and nowhere else (ADR-001). One object store
 * for the profile, one for ticks. Writes are serialised with the Web Locks API; other tabs hear about
 * changes over a BroadcastChannel.
 */

const DB_NAME = "selficient";
const DB_VERSION = 1;
const CHANNEL = "selficient-plan";
const LOCK = "selficient-plan-write";

/** @typedef {Record<string, string>} Profile */
/** @typedef {{ profile: Profile | null, ticks: string[] }} Snapshot */

/** @returns {Promise<IDBDatabase>} */
function open() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, DB_VERSION);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains("profile")) {
        db.createObjectStore("profile");
      }
      if (!db.objectStoreNames.contains("ticks")) {
        db.createObjectStore("ticks");
      }
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error("The browser refused to open local storage."));
    request.onblocked = () => reject(new Error("Local storage is open in another tab and needs upgrading."));
  });
}

/**
 * @template T
 * @param {IDBRequest<T>} request
 * @returns {Promise<T>}
 */
function done(request) {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error("Local storage failed."));
  });
}

/**
 * @template T
 * @param {"profile" | "ticks"} store
 * @param {IDBTransactionMode} mode
 * @param {(store: IDBObjectStore) => IDBRequest<T> | Promise<T>} work
 * @returns {Promise<T>}
 */
async function withStore(store, mode, work) {
  const db = await open();
  try {
    const result = work(db.transaction(store, mode).objectStore(store));
    return result instanceof IDBRequest ? await done(result) : await result;
  } finally {
    db.close();
  }
}

/**
 * @param {() => Promise<void>} write
 */
async function serialised(write) {
  if (navigator.locks) {
    await navigator.locks.request(LOCK, write);
  } else {
    await write();
  }
}

function announce() {
  try {
    new BroadcastChannel(CHANNEL).postMessage({ type: "changed" });
  } catch {
    // No channel support: the other tab will see the change on its next load.
  }
}

/** @returns {Promise<Profile | null>} */
export async function getProfile() {
  const value = await withStore("profile", "readonly", (s) => s.get("current"));
  return isProfile(value) ? value : null;
}

/** @param {Profile} profile */
export async function saveProfile(profile) {
  await serialised(async () => {
    await withStore("profile", "readwrite", (s) => s.put(profile, "current"));
  });
  announce();
}

/** @returns {Promise<string[]>} */
export async function getTicks() {
  const keys = await withStore("ticks", "readonly", (s) => s.getAllKeys());
  return keys.filter((k) => typeof k === "string");
}

/**
 * @param {string} actionId
 * @param {boolean} ticked
 */
export async function setTick(actionId, ticked) {
  await serialised(async () => {
    if (ticked) {
      await withStore("ticks", "readwrite", (s) => s.put(true, actionId));
    } else {
      await withStore("ticks", "readwrite", (s) => s.delete(actionId));
    }
  });
  announce();
}

export async function clearAll() {
  await serialised(async () => {
    await withStore("profile", "readwrite", (s) => s.clear());
    await withStore("ticks", "readwrite", (s) => s.clear());
  });
  announce();
}

/** @returns {Promise<Snapshot>} */
export async function snapshot() {
  return { profile: await getProfile(), ticks: await getTicks() };
}

/**
 * Replaces everything with an imported snapshot after checking its shape. Anything that is not a
 * flat string map and a list of kebab-case ids is refused.
 * @param {unknown} data
 */
export async function restore(data) {
  if (!isSnapshot(data)) {
    throw new Error("That file is not a Selficient plan.");
  }
  await serialised(async () => {
    await withStore("profile", "readwrite", (s) => s.clear());
    await withStore("ticks", "readwrite", (s) => s.clear());
    if (data.profile) {
      await withStore("profile", "readwrite", (s) => s.put(data.profile, "current"));
    }
    for (const id of data.ticks) {
      await withStore("ticks", "readwrite", (s) => s.put(true, id));
    }
  });
  announce();
}

/**
 * @param {(event: MessageEvent) => void} handler
 */
export function onChange(handler) {
  try {
    new BroadcastChannel(CHANNEL).addEventListener("message", handler);
  } catch {
    // Unsupported: single-tab behaviour only.
  }
}

const ID = /^[a-z0-9]+(-[a-z0-9]+)*$/;
const FIELD = /^[A-Za-z]{1,40}$/;

/**
 * @param {unknown} value
 * @returns {value is Profile}
 */
export function isProfile(value) {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return false;
  }
  const entries = Object.entries(value);
  return entries.length > 0 && entries.length <= 40 && entries.every(([k, v]) => FIELD.test(k) && typeof v === "string" && v.length <= 40);
}

/**
 * @param {unknown} value
 * @returns {value is Snapshot}
 */
export function isSnapshot(value) {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const { profile, ticks } = /** @type {{ profile?: unknown, ticks?: unknown }} */ (value);
  const profileOk = profile === null || isProfile(profile);
  const ticksOk = Array.isArray(ticks) && ticks.length <= 200 && ticks.every((t) => typeof t === "string" && ID.test(t));
  return profileOk && ticksOk;
}
