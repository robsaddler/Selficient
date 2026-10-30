// @ts-check
/**
 * Where the plan lives: IndexedDB, in this browser, and nowhere else (ADR-001). One object store
 * for the profile, one for ticks, one for the pin on the map. Writes are serialised with the Web
 * Locks API; other tabs hear about changes over a BroadcastChannel.
 */

const DB_NAME = "selficient";
const DB_VERSION = 3;
const CHANNEL = "selficient-plan";
const LOCK = "selficient-plan-write";

/** @typedef {Record<string, string>} Profile */
/** @typedef {{ lat: number, lng: number }} House */
/** @typedef {{ name: string, kind: "PoweredDevice" | "Medicine" | "Supply", watts: number, hoursPerDay: number, daysOfStock: number, mustStayCold: boolean }} Dependency */
/** @typedef {{ profile: Profile | null, ticks: string[], house?: House | null, dependencies?: Dependency[] }} Snapshot */

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
      // Version 2 (M5): where the household put their pin on the map.
      if (!db.objectStoreNames.contains("house")) {
        db.createObjectStore("house");
      }
      // Version 3 (M13): the things only this household relies on.
      if (!db.objectStoreNames.contains("dependencies")) {
        db.createObjectStore("dependencies");
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
 * @param {"profile" | "ticks" | "house" | "dependencies"} store
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

/** @returns {Promise<House | null>} */
export async function getHouse() {
  const value = await withStore("house", "readonly", (s) => s.get("pin"));
  return isHouse(value) ? value : null;
}

/** @param {House} house */
export async function saveHouse(house) {
  if (!isHouse(house)) {
    throw new Error("That is not a place in the United Kingdom.");
  }
  await serialised(async () => {
    await withStore("house", "readwrite", (s) => s.put({ lat: house.lat, lng: house.lng }, "pin"));
  });
  announce();
}

export async function clearHouse() {
  await serialised(async () => {
    await withStore("house", "readwrite", (s) => s.delete("pin"));
  });
  announce();
}

export async function clearAll() {
  await serialised(async () => {
    await withStore("profile", "readwrite", (s) => s.clear());
    await withStore("ticks", "readwrite", (s) => s.clear());
    await withStore("house", "readwrite", (s) => s.clear());
    await withStore("dependencies", "readwrite", (s) => s.clear());
  });
  announce();
}

/** @returns {Promise<Dependency[]>} */
export async function getDependencies() {
  const value = await withStore("dependencies", "readonly", (s) => s.get("list"));
  return Array.isArray(value) ? value.filter(isDependency) : [];
}

/** @param {Dependency[]} dependencies */
export async function saveDependencies(dependencies) {
  const kept = dependencies.filter(isDependency).slice(0, 12);
  await serialised(async () => {
    await withStore("dependencies", "readwrite", (s) => s.put(kept, "list"));
  });
  announce();
}

/** @returns {Promise<Snapshot>} */
export async function snapshot() {
  return { profile: await getProfile(), ticks: await getTicks(), house: await getHouse(), dependencies: await getDependencies() };
}

/**
 * Replaces everything with an imported snapshot after checking its shape. Anything that is not a
 * flat string map, a list of kebab-case ids and an optional pin in the United Kingdom is refused.
 * @param {unknown} data
 */
export async function restore(data) {
  if (!isSnapshot(data)) {
    throw new Error("That file is not a Selficient plan.");
  }
  await serialised(async () => {
    await withStore("profile", "readwrite", (s) => s.clear());
    await withStore("ticks", "readwrite", (s) => s.clear());
    await withStore("house", "readwrite", (s) => s.clear());
    if (data.profile) {
      await withStore("profile", "readwrite", (s) => s.put(data.profile, "current"));
    }
    for (const id of data.ticks) {
      await withStore("ticks", "readwrite", (s) => s.put(true, id));
    }
    const house = data.house;
    if (house) {
      await withStore("house", "readwrite", (s) => s.put({ lat: house.lat, lng: house.lng }, "pin"));
    }
    await withStore("dependencies", "readwrite", (s) => s.put((data.dependencies ?? []).slice(0, 12), "list"));
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
 * A pin somewhere in the United Kingdom and its waters. Anything else is refused, so an imported
 * file cannot park the map on the far side of the world.
 * @param {unknown} value
 * @returns {value is House}
 */
export function isHouse(value) {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const { lat, lng } = /** @type {{ lat?: unknown, lng?: unknown }} */ (value);
  return typeof lat === "number" && typeof lng === "number" && Number.isFinite(lat) && Number.isFinite(lng) && lat >= 49 && lat <= 61 && lng >= -9 && lng <= 2.5;
}

/**
 * @param {unknown} value
 * @returns {value is Snapshot}
 */
export function isSnapshot(value) {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const { profile, ticks, house, dependencies } = /** @type {{ profile?: unknown, ticks?: unknown, house?: unknown, dependencies?: unknown }} */ (value);
  const profileOk = profile === null || isProfile(profile);
  const ticksOk = Array.isArray(ticks) && ticks.length <= 200 && ticks.every((t) => typeof t === "string" && ID.test(t));
  const houseOk = house === undefined || house === null || isHouse(house);
  const dependenciesOk = dependencies === undefined || (Array.isArray(dependencies) && dependencies.length <= 12 && dependencies.every(isDependency));
  return profileOk && ticksOk && houseOk && dependenciesOk;
}

const KINDS = ["PoweredDevice", "Medicine", "Supply"];

/**
 * Something the household relies on, within the bounds the server also checks, so an imported
 * file cannot smuggle in a thousand-watt name or a negative stock.
 * @param {unknown} value
 * @returns {value is Dependency}
 */
export function isDependency(value) {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const d = /** @type {Record<string, unknown>} */ (value);
  return typeof d.name === "string" && d.name.trim().length > 0 && d.name.length <= 40
    && typeof d.kind === "string" && KINDS.includes(d.kind)
    && typeof d.watts === "number" && d.watts >= 0 && d.watts <= 3000
    && typeof d.hoursPerDay === "number" && d.hoursPerDay >= 0 && d.hoursPerDay <= 24
    && typeof d.daysOfStock === "number" && Number.isInteger(d.daysOfStock) && d.daysOfStock >= 0 && d.daysOfStock <= 365
    && typeof d.mustStayCold === "boolean";
}
