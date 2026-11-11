// @ts-check
/**
 * Where the plan lives: IndexedDB, in this browser, and nowhere else (ADR-001). One object store
 * for the profile, one for what the household said about each action, one for the pin on the map.
 * The second is still called "ticks": it began as a set of ids the household had, and a value of
 * true from that time reads as "have". Writes are serialised with the Web
 * Locks API; other tabs hear about changes over a BroadcastChannel.
 */

const DB_NAME = "selficient";
const DB_VERSION = 4;
const CHANNEL = "selficient-plan";
const LOCK = "selficient-plan-write";

/** @typedef {Record<string, string>} Profile */
/** @typedef {{ lat: number, lng: number }} House */
/** @typedef {{ name: string, kind: "PoweredDevice" | "Medicine" | "Supply", watts: number, hoursPerDay: number, daysOfStock: number, mustStayCold: boolean }} Dependency */
/** @typedef {"have" | "plan" | "skip"} Choice */
/** @typedef {Record<string, number>} Amounts */
/** @typedef {{ profile: Profile | null, ticks: string[], planned?: string[], skipped?: string[], amounts?: Amounts, house?: House | null, dependencies?: Dependency[] }} Snapshot */

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
      // Version 4: the household's own figures for the stocks it counted (ten days of tins).
      if (!db.objectStoreNames.contains("amounts")) {
        db.createObjectStore("amounts");
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
 * @param {"profile" | "ticks" | "house" | "dependencies" | "amounts"} store
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

/**
 * What the household said about each action it has said anything about. An action that is not
 * here is not decided. Anything in the store that is not an action id with a known choice is left
 * out, so a tampered store cannot put a strange value on the page or in a post.
 * @returns {Promise<Record<string, Choice>>}
 */
export async function getChoices() {
  const db = await open();
  try {
    const store = db.transaction("ticks", "readonly").objectStore("ticks");
    const [keys, values] = await Promise.all([done(store.getAllKeys()), done(store.getAll())]);
    /** @type {Record<string, Choice>} */
    const choices = Object.create(null);
    keys.forEach((key, i) => {
      const choice = asChoice(values[i]);
      if (typeof key === "string" && ID.test(key) && choice) {
        choices[key] = choice;
      }
    });
    return choices;
  } finally {
    db.close();
  }
}

/** @returns {Promise<string[]>} the actions the household has */
export async function getTicks() {
  const choices = await getChoices();
  return Object.keys(choices).filter((id) => choices[id] === "have");
}

/**
 * Sets or clears what the household said about several actions at once, as one write, so a
 * button that changes a whole stretch is one change to every listener and one thing to undo.
 * @param {Record<string, Choice | null>} changes null puts an action back to not decided
 */
export async function setChoices(changes) {
  const entries = Object.entries(changes).filter(([id, choice]) => ID.test(id) && (choice === null || asChoice(choice) !== null));
  if (entries.length === 0) {
    return;
  }
  await serialised(async () => {
    const db = await open();
    try {
      const transaction = db.transaction("ticks", "readwrite");
      const store = transaction.objectStore("ticks");
      for (const [id, choice] of entries) {
        if (choice === null) {
          store.delete(id);
        } else {
          store.put(choice, id);
        }
      }
      await new Promise((resolve, reject) => {
        transaction.oncomplete = () => resolve(undefined);
        transaction.onerror = () => reject(transaction.error ?? new Error("Local storage failed."));
        transaction.onabort = () => reject(transaction.error ?? new Error("Local storage failed."));
      });
    } finally {
      db.close();
    }
  });
  announce();
}

/**
 * @param {string} actionId
 * @param {Choice | null} choice null puts the action back to not decided
 */
export async function setChoice(actionId, choice) {
  await setChoices({ [actionId]: choice });
}

/**
 * Marks an action as one the household has, or clears that. Clearing leaves a plan or a ruling
 * out alone: this is the photos page saying what it saw, not the household changing its mind.
 * @param {string} actionId
 * @param {boolean} ticked
 */
export async function setTick(actionId, ticked) {
  if (ticked) {
    await setChoice(actionId, "have");
    return;
  }
  const choices = await getChoices();
  if (choices[actionId] === "have") {
    await setChoice(actionId, null);
  }
}

/**
 * The household's own figures, by the id of the stock each counts. A stock that is not here has
 * no figure. Anything that is not an id with a whole number in range is left out.
 * @returns {Promise<Amounts>}
 */
export async function getAmounts() {
  const value = await withStore("amounts", "readonly", (s) => s.get("all"));
  return isAmounts(value) ? { ...value } : {};
}

/**
 * Sets or clears several figures at once, as one write.
 * @param {Record<string, number | null>} changes null takes the figure away
 */
export async function setAmounts(changes) {
  await serialised(async () => {
    const amounts = await getAmounts();
    for (const [id, amount] of Object.entries(changes)) {
      if (amount === null) {
        delete amounts[id];
      } else {
        amounts[id] = amount;
      }
    }
    if (isAmounts(amounts)) {
      await withStore("amounts", "readwrite", (s) => s.put(amounts, "all"));
    }
  });
  announce();
}

/**
 * @param {unknown} value
 * @returns {Choice | null}
 */
function asChoice(value) {
  if (value === true || value === "have") {
    return "have";
  }
  return value === "plan" || value === "skip" ? value : null;
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
    await withStore("amounts", "readwrite", (s) => s.clear());
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
  const choices = await getChoices();
  const of = (/** @type {Choice} */ kind) => Object.keys(choices).filter((id) => choices[id] === kind);
  return { profile: await getProfile(), ticks: of("have"), planned: of("plan"), skipped: of("skip"), amounts: await getAmounts(), house: await getHouse(), dependencies: await getDependencies() };
}

/**
 * Replaces everything with an imported snapshot after checking its shape. Anything that is not a
 * flat string map, lists of kebab-case ids and an optional pin in the United Kingdom is refused.
 * An id in more than one list is read once: having beats planning, and planning beats ruling out,
 * as the server reads them.
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
    // Written weakest first, so a later list overwrites an earlier one for the same id.
    for (const [ids, choice] of /** @type {[string[], Choice][]} */ ([[data.skipped ?? [], "skip"], [data.planned ?? [], "plan"], [data.ticks, "have"]])) {
      for (const id of ids) {
        await withStore("ticks", "readwrite", (s) => s.put(choice, id));
      }
    }
    const house = data.house;
    if (house) {
      await withStore("house", "readwrite", (s) => s.put({ lat: house.lat, lng: house.lng }, "pin"));
    }
    await withStore("dependencies", "readwrite", (s) => s.put((data.dependencies ?? []).slice(0, 12), "list"));
    await withStore("amounts", "readwrite", (s) => s.put(data.amounts ?? {}, "all"));
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
  const { profile, ticks, planned, skipped, amounts, house, dependencies } = /** @type {{ profile?: unknown, ticks?: unknown, planned?: unknown, skipped?: unknown, amounts?: unknown, house?: unknown, dependencies?: unknown }} */ (value);
  const profileOk = profile === null || isProfile(profile);
  const ids = (/** @type {unknown} */ list) => Array.isArray(list) && list.length <= 200 && list.every((t) => typeof t === "string" && ID.test(t));
  // The two newer lists are optional, so a file exported before they existed still opens.
  const ticksOk = ids(ticks) && (planned === undefined || ids(planned)) && (skipped === undefined || ids(skipped));
  const houseOk = house === undefined || house === null || isHouse(house);
  const dependenciesOk = dependencies === undefined || (Array.isArray(dependencies) && dependencies.length <= 12 && dependencies.every(isDependency));
  const amountsOk = amounts === undefined || isAmounts(amounts);
  return profileOk && ticksOk && houseOk && dependenciesOk && amountsOk;
}

/**
 * Figures a household put on its stocks: at most twenty, each a kebab-case id with a whole number
 * from nothing to ten thousand. The server holds each to its own stock's range as well.
 * @param {unknown} value
 * @returns {value is Amounts}
 */
export function isAmounts(value) {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return false;
  }
  const entries = Object.entries(value);
  return entries.length <= 20 && entries.every(([id, amount]) => ID.test(id) && Number.isInteger(amount) && amount >= 0 && amount <= 10000);
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
