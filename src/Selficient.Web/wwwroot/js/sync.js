// @ts-check
/**
 * Keeping a signed-in household's devices in step. The account holds one copy of the plan and a
 * version; this reads it, and writes it back naming the version it last saw, so a device that has
 * been away cannot overwrite what another wrote since. The plan stays in this browser as well, so
 * everything works without a connection and catches up when there is one.
 *
 * This device remembers the last copy it agreed with the account (the base). When both have
 * changed since, the two are merged against it: what the household said about each action is
 * merged action by action, the base standing as the record of what was taken away; its household
 * details go to the side that changed them, or to the newer when both did, and the page says so.
 * The first time a device's own plan meets a different plan in the account there is no base, and
 * the person chooses one of the two. Two unrelated plans are never mixed.
 *
 * A guest's browser never runs any of this: it starts only once the page knows somebody is
 * signed in. The service worker is not involved.
 */
import * as store from "selficient/plan-store.js";
import { signedInAs } from "selficient/keep.js";
import { describe } from "selficient/plan-words.js";

const STATE_KEY = "selficient.sync";
const SAY_KEY = "selficient.sync-say";
/** How long a sentence goes on being said if the page changes under it, as it does when a plan is rebuilt. */
const SAY_FOR_MS = 20_000;
const DEBOUNCE_MS = 1500;
const POLL_MS = 60_000;
const HEADERS = { "Content-Type": "application/json", "X-Selficient-Sync": "1" };

/** @typedef {import("selficient/plan-store.js").Snapshot} Snapshot */
/** @typedef {import("selficient/plan-store.js").Choice} Choice */
/** @typedef {{ plan: Snapshot, detailsAt: number }} Body */
/**
 * What this device remembers between runs: whose account, the version and copy it last agreed
 * with the account, and when the household details on this device last changed.
 * @typedef {{ account: string, version: number, base: Body | null, detailsKey: string, detailsAt: number }} State
 */
/** @typedef {{ version: number, body: Body | null }} Remote */

const RANK = /** @type {Record<string, number>} */ ({ have: 3, plan: 2, skip: 1 });

/**
 * What a household said about each action, one choice an action. Where an id is in more than one
 * list, having beats planning and planning beats ruling out, as the store reads them.
 * @param {Snapshot} plan
 * @returns {Record<string, Choice>}
 */
function choicesOf(plan) {
  /** @type {Record<string, Choice>} */
  const choices = Object.create(null);
  for (const [ids, choice] of /** @type {[string[] | undefined, Choice][]} */ ([[plan.skipped, "skip"], [plan.planned, "plan"], [plan.ticks, "have"]])) {
    for (const id of ids ?? []) {
      choices[id] = choice;
    }
  }
  return choices;
}

/**
 * @param {Record<string, unknown> | null | undefined} object
 * @returns {Record<string, any>}
 */
function sorted(object) {
  return Object.fromEntries(Object.entries(object ?? {}).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)));
}

/**
 * A plan with everything in one order and nothing optional left out, so that two plans that mean
 * the same are the same text.
 * @param {Snapshot} plan
 * @returns {Required<Snapshot>}
 */
export function canonical(plan) {
  const choices = choicesOf(plan);
  const of = (/** @type {Choice} */ kind) => Object.keys(choices).filter((id) => choices[id] === kind).sort();
  return {
    profile: plan.profile ? sorted(plan.profile) : null,
    ticks: of("have"),
    planned: of("plan"),
    skipped: of("skip"),
    amounts: sorted(plan.amounts),
    assumptions: sorted(plan.assumptions),
    house: plan.house ? { lat: plan.house.lat, lng: plan.house.lng } : null,
    dependencies: (plan.dependencies ?? []).map((d) => ({ name: d.name, kind: d.kind, watts: d.watts, hoursPerDay: d.hoursPerDay, daysOfStock: d.daysOfStock, mustStayCold: d.mustStayCold })),
    shortlist: [...new Set(plan.shortlist ?? [])].sort(),
  };
}

/** @param {unknown} value */
const text = (value) => JSON.stringify(value);

/**
 * @param {Snapshot} a
 * @param {Snapshot} b
 */
export function same(a, b) {
  return text(canonical(a)) === text(canonical(b));
}

/**
 * The household details of a plan, as one text: who lives there, the house, what they rely on,
 * what the plan assumes. These change together and rarely, and a conflict in them is told.
 * @param {Snapshot} plan
 */
function detailsKey(plan) {
  const c = canonical(plan);
  return text([c.profile, c.house, c.dependencies, c.assumptions]);
}

/**
 * Merges two plans that both began as the base.
 * @param {Snapshot} base what this device and the account last agreed on
 * @param {Snapshot} mine the plan on this device now
 * @param {Snapshot} theirs the plan in the account now
 * @param {number} mineAt when the household details here last changed
 * @param {number} theirsAt when the household details there last changed
 * @returns {{ plan: Required<Snapshot>, tookTheirs: boolean }} the merged plan, and whether this device's household details gave way
 */
export function merge(base, mine, theirs, mineAt, theirsAt) {
  const b = canonical(base);
  const m = canonical(mine);
  const t = canonical(theirs);
  const theirsNewer = theirsAt > mineAt;
  let tookTheirs = false;

  // What was said about each action. A side that did not change it since the base gives way to the
  // side that did, which is how a removal survives; where both changed it, the stronger word wins.
  const was = choicesOf(b);
  const here = choicesOf(m);
  const there = choicesOf(t);
  /** @type {Record<string, Choice>} */
  const said = Object.create(null);
  for (const id of new Set([...Object.keys(was), ...Object.keys(here), ...Object.keys(there)])) {
    const before = was[id] ?? null;
    const h = here[id] ?? null;
    const th = there[id] ?? null;
    const chosen = h === before ? th : th === before ? h : (RANK[h ?? ""] ?? 0) >= (RANK[th ?? ""] ?? 0) ? h : th;
    if (chosen) {
      said[id] = chosen;
    }
  }
  const of = (/** @type {Choice} */ kind) => Object.keys(said).filter((id) => said[id] === kind).sort();

  // The figures a household put on its stocks, figure by figure, the newer side winning a clash.
  /** @type {Record<string, number>} */
  const amounts = {};
  for (const id of new Set([...Object.keys(b.amounts), ...Object.keys(m.amounts), ...Object.keys(t.amounts)])) {
    const before = b.amounts[id] ?? null;
    const h = m.amounts[id] ?? null;
    const th = t.amounts[id] ?? null;
    const chosen = h === before ? th : th === before || !theirsNewer ? h : th;
    if (chosen !== null) {
      amounts[id] = chosen;
    }
  }

  // The household details, part by part: the side that changed a part keeps it, and where both
  // did, the newer does.
  const pick = (/** @type {"profile" | "house" | "dependencies" | "assumptions"} */ part) => {
    const before = text(b[part]);
    const h = text(m[part]);
    const th = text(t[part]);
    if (h === before) {
      return t[part];
    }
    if (th === before || th === h || !theirsNewer) {
      return m[part];
    }
    tookTheirs = true;
    return t[part];
  };

  // What was saved for later is a set like the ticks: saved on either side is saved, and taken off on either is off.
  const saved = [...new Set([...b.shortlist, ...m.shortlist, ...t.shortlist])]
    .filter((id) => (m.shortlist.includes(id) === b.shortlist.includes(id) ? t.shortlist.includes(id) : m.shortlist.includes(id)))
    .sort();

  const plan = {
    profile: /** @type {Snapshot["profile"]} */ (pick("profile")),
    ticks: of("have"),
    planned: of("plan"),
    skipped: of("skip"),
    amounts: sorted(amounts),
    assumptions: /** @type {Required<Snapshot>["assumptions"]} */ (pick("assumptions")),
    house: /** @type {Required<Snapshot>["house"]} */ (pick("house")),
    dependencies: /** @type {Required<Snapshot>["dependencies"]} */ (pick("dependencies")),
    shortlist: saved,
  };
  return { plan, tookTheirs };
}

/**
 * @param {unknown} value
 * @returns {value is Body}
 */
function isBody(value) {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const { plan, detailsAt } = /** @type {{ plan?: unknown, detailsAt?: unknown }} */ (value);
  return typeof detailsAt === "number" && Number.isFinite(detailsAt) && detailsAt >= 0 && store.isSnapshot(plan);
}

/** @returns {State | null} */
function load() {
  try {
    const value = /** @type {unknown} */ (JSON.parse(localStorage.getItem(STATE_KEY) ?? "null"));
    if (typeof value !== "object" || value === null) {
      return null;
    }
    const { account, version, base, detailsKey: key, detailsAt } = /** @type {Record<string, unknown>} */ (value);
    const ok = typeof account === "string" && typeof version === "number" && Number.isInteger(version) && version >= 0
      && (base === null || isBody(base)) && typeof key === "string" && typeof detailsAt === "number" && Number.isFinite(detailsAt);
    return ok ? /** @type {State} */ (value) : null;
  } catch {
    return null;
  }
}

/** @param {State} state */
function save(state) {
  try {
    localStorage.setItem(STATE_KEY, JSON.stringify(state));
  } catch {
    // Without it the next run starts from the account's copy, which is safe.
  }
}

/**
 * The account's copy. Null when it could not be fetched; "signed-out" when the session has ended.
 * A copy this browser's own guard does not accept reads as none, and is replaced.
 * @returns {Promise<Remote | "signed-out" | null>}
 */
async function fetchRemote() {
  try {
    const response = await fetch("/api/account/plan", { cache: "no-store" });
    if (response.status === 401) {
      return "signed-out";
    }
    return response.ok ? remoteOf(await response.json()) : null;
  } catch {
    return null;
  }
}

/**
 * @param {unknown} answer
 * @returns {Remote | null}
 */
function remoteOf(answer) {
  const { version, body } = /** @type {{ version?: unknown, body?: unknown }} */ (answer ?? {});
  if (typeof version !== "number" || !Number.isInteger(version) || version < 0) {
    return null;
  }
  let parsed = /** @type {unknown} */ (null);
  try {
    parsed = typeof body === "string" ? JSON.parse(body) : null;
  } catch {
    parsed = null;
  }
  return { version, body: isBody(parsed) ? parsed : null };
}

/**
 * Writes a copy based on a version. Answers the new version; or the copy that is there now, when
 * another device wrote first; or null when it could not be written at all.
 * @param {number} baseVersion
 * @param {Body} body
 * @returns {Promise<{ version: number } | { conflict: Remote } | null>}
 */
async function push(baseVersion, body) {
  try {
    const response = await fetch(`/api/account/plan?base=${baseVersion}`, { method: "PUT", headers: HEADERS, body: JSON.stringify(body), cache: "no-store" });
    if (response.status === 409) {
      const conflict = remoteOf(await response.json());
      return conflict ? { conflict } : null;
    }
    if (!response.ok) {
      return null;
    }
    const answer = remoteOf(await response.json());
    return answer ? { version: answer.version } : null;
  } catch {
    return null;
  }
}

/**
 * @param {Document} doc
 */
export function initSync(doc) {
  // The pages that end a session clear what is kept here themselves; a run in the middle of that would put it back.
  if (doc.querySelector("[data-sign-out], [data-delete-account], [data-account-gone]")) {
    return;
  }
  const notice = doc.querySelector("[data-sync-notice]");
  const dialog = doc.querySelector("[data-sync-choice]");
  /** @type {{ local: Snapshot, remote: Remote, state: State } | null} */
  let choosing = null;
  let asked = false;
  /** @type {Promise<void>} */
  let chain = Promise.resolve();
  /** @type {number | undefined} */
  let timer;
  /** @type {number | undefined} */
  let fade;

  /** @param {"synced" | "waiting" | "choosing"} status */
  const show = (status) => {
    doc.documentElement.dataset.sync = status;
    for (const line of doc.querySelectorAll("[data-sync-status]")) {
      line.toggleAttribute("hidden", line.getAttribute("data-sync-status") !== status);
    }
  };

  /**
   * Says one sentence in the notice. The plan page is rebuilt when a plan arrives, which is two
   * page loads, so the sentence is kept for a few seconds and said again on a page that loads
   * inside them. Dismissing it ends it.
   * @param {string} kind
   * @param {number} [until]
   */
  const say = (kind, until) => {
    try {
      sessionStorage.setItem(SAY_KEY, JSON.stringify({ kind, until: until ?? Date.now() + SAY_FOR_MS }));
    } catch {
      // Said on this page only.
    }
    if (!(notice instanceof HTMLElement)) {
      return;
    }
    for (const line of notice.querySelectorAll("[data-sync-says]")) {
      line.toggleAttribute("hidden", line.getAttribute("data-sync-says") !== kind);
    }
    notice.hidden = false;
    // It goes by itself: a strip nobody dismissed should not sit over the page for good.
    clearTimeout(fade);
    fade = setTimeout(() => {
      notice.hidden = true;
    }, Math.max(0, (until ?? Date.now() + SAY_FOR_MS) - Date.now()));
  };

  const unsay = () => {
    try {
      sessionStorage.removeItem(SAY_KEY);
    } catch {
      // Nothing kept.
    }
  };

  if (notice instanceof HTMLElement) {
    notice.addEventListener("click", (event) => {
      if (event.target instanceof Element && event.target.closest("[data-sync-dismiss]")) {
        notice.hidden = true;
        unsay();
      }
    });
    try {
      const pending = /** @type {{ kind?: unknown, until?: unknown } | null} */ (JSON.parse(sessionStorage.getItem(SAY_KEY) ?? "null"));
      if (pending && typeof pending.kind === "string" && typeof pending.until === "number" && pending.until > Date.now()) {
        say(pending.kind, pending.until);
      } else {
        unsay();
      }
    } catch {
      unsay();
    }
  }

  /**
   * Writes a plan to this device. When the household details changed under a plan page that the
   * server built from the old ones, the page is built again from the new.
   * @param {Snapshot} plan
   * @param {Snapshot} was
   * @param {string} [sentence]
   */
  const apply = async (plan, was, sentence) => {
    // The plan page, built or still empty, is the server's rendering of the details that were here.
    const rebuilt = doc.querySelector("[data-plan], [data-plan-empty]") !== null && detailsKey(plan) !== detailsKey(was);
    if (sentence) {
      say(sentence);
    }
    await store.restore(plan);
    if (rebuilt) {
      // Replaced, not added to: the page built from the old details must not be one press of Back
      // away, because a plan page writes the details it was built from back to the store.
      location.replace("/plan");
    }
  };

  /**
   * Writes a copy to the account, merging and trying again when another device wrote first.
   * @param {State} state
   * @param {Snapshot} local
   * @param {number} baseVersion
   * @param {string} [sentence]
   */
  const send = async (state, local, baseVersion, sentence) => {
    const body = { plan: canonical(local), detailsAt: state.detailsAt };
    const written = await push(baseVersion, body);
    if (written === null) {
      show("waiting");
      return;
    }
    if ("conflict" in written) {
      await settle(state, local, written.conflict);
      return;
    }
    save({ ...state, version: written.version, base: body, detailsKey: detailsKey(local) });
    show("synced");
    if (sentence) {
      say(sentence);
    }
  };

  /**
   * One look at this device and the account, and whatever follows from how they differ.
   * @param {State} state
   * @param {Snapshot} local
   * @param {Remote} remote
   */
  const settle = async (state, local, remote) => {
    const hasLocal = local.profile !== null;
    if (state.base === null) {
      // The first time this device meets this account.
      if (remote.body === null) {
        if (hasLocal) {
          await send(state, local, remote.version, "kept");
        } else {
          save({ ...state, version: remote.version });
          show("synced");
        }
        return;
      }
      if (!hasLocal || same(local, remote.body.plan)) {
        save({ ...state, version: remote.version, base: remote.body, detailsKey: detailsKey(remote.body.plan), detailsAt: remote.body.detailsAt });
        show("synced");
        if (!hasLocal) {
          await apply(remote.body.plan, local, "arrived");
        }
        return;
      }
      // Two plans with nothing in common: the person chooses. Asked once a page; the panel offers it again.
      choosing = { local, remote, state };
      show("choosing");
      if (!asked) {
        asked = true;
        ask();
      }
      return;
    }

    const localChanged = !same(local, state.base.plan);
    const remoteChanged = remote.version !== state.version;
    if (!remoteChanged) {
      if (localChanged) {
        await send(state, local, remote.version);
      } else {
        show("synced");
      }
      return;
    }
    if (remote.body === null) {
      // The account's copy is gone or cannot be read; this device still has the plan.
      await send(state, local, remote.version);
      return;
    }
    if (!localChanged) {
      save({ ...state, version: remote.version, base: remote.body, detailsKey: detailsKey(remote.body.plan), detailsAt: remote.body.detailsAt });
      show("synced");
      await apply(remote.body.plan, local);
      return;
    }
    const merged = merge(state.base.plan, local, remote.body.plan, state.detailsAt, remote.body.detailsAt);
    const next = { ...state, detailsAt: Math.max(state.detailsAt, remote.body.detailsAt), detailsKey: detailsKey(merged.plan) };
    save({ ...next, version: remote.version, base: remote.body });
    if (!same(merged.plan, local)) {
      await apply(merged.plan, local, merged.tookTheirs ? "details" : undefined);
    }
    if (same(merged.plan, remote.body.plan)) {
      show("synced");
    } else {
      await send({ ...next, version: remote.version, base: remote.body }, merged.plan, remote.version);
    }
  };

  const run = async () => {
    const account = await signedInAs();
    if (account === null) {
      return;
    }
    const local = await store.snapshot();
    const remote = await fetchRemote();
    if (remote === "signed-out") {
      return;
    }
    const kept = load();
    /** @type {State} */
    const state = kept && kept.account === account ? kept : { account, version: 0, base: null, detailsKey: detailsKey(local), detailsAt: Date.now() };
    // Household details that are not as this device last saw them were changed here, now.
    const key = detailsKey(local);
    if (key !== state.detailsKey) {
      state.detailsKey = key;
      state.detailsAt = Date.now();
    }
    save(state);
    if (remote === null) {
      show(state.base !== null && same(local, state.base.plan) ? "synced" : "waiting");
      return;
    }
    await settle(state, local, remote);
  };

  /** Runs are strictly one after another, so two never read the same state and write over each other. */
  const queue = () => {
    chain = chain.then(run).catch(() => undefined);
    return chain;
  };

  const ask = () => {
    if (!(dialog instanceof HTMLDialogElement) || !choosing || choosing.remote.body === null) {
      return;
    }
    for (const line of dialog.querySelectorAll("[data-sync-theirs]")) {
      line.textContent = `${describe(choosing.remote.body.plan, "In your account: a plan")}.`;
    }
    for (const line of dialog.querySelectorAll("[data-sync-mine]")) {
      line.textContent = `${describe(choosing.local, "On this device: a plan")}.`;
    }
    if (!dialog.open) {
      dialog.showModal();
    }
  };

  doc.addEventListener("click", (event) => {
    const target = event.target instanceof Element ? event.target : null;
    if (target?.closest("[data-sync-choose]")) {
      // The panel that offered the choice makes way for it.
      for (const open of doc.querySelectorAll("dialog[open]")) {
        if (open instanceof HTMLDialogElement && open !== dialog) {
          open.close();
        }
      }
      ask();
      return;
    }
    const pick = target?.closest("[data-sync-take]")?.getAttribute("data-sync-take");
    if (!pick || !choosing || choosing.remote.body === null) {
      return;
    }
    const { local, remote, state } = choosing;
    const body = /** @type {Body} */ (remote.body);
    choosing = null;
    if (dialog instanceof HTMLDialogElement) {
      dialog.close();
    }
    chain = chain.then(async () => {
      if (pick === "account") {
        save({ ...state, version: remote.version, base: body, detailsKey: detailsKey(body.plan), detailsAt: body.detailsAt });
        show("synced");
        await apply(body.plan, local, "replaced");
      } else {
        // Based on what the account holds now, so the write lands; the account's own plan is what it replaces.
        await send({ ...state, base: body, version: remote.version, detailsAt: Date.now() }, local, remote.version, "kept-here");
      }
    }).catch(() => undefined);
  });

  // The plan page with nothing built is about to post the stored profile and leave; a run started
  // here would be cut off half way. The page it lands on runs.
  void store.getProfile().catch(() => null).then((profile) => {
    if (doc.querySelector("[data-plan-empty]") === null || profile === null) {
      void queue();
    }
  });
  store.onChange(() => {
    clearTimeout(timer);
    timer = setTimeout(() => void queue(), DEBOUNCE_MS);
  });
  addEventListener("online", () => void queue());
  doc.addEventListener("visibilitychange", () => {
    if (doc.visibilityState === "visible") {
      void queue();
    }
  });
  setInterval(() => {
    if (doc.visibilityState === "visible") {
      void queue();
    }
  }, POLL_MS);
}
