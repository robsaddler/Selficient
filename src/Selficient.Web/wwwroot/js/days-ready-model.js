// @ts-check
/**
 * The browser-side mirror of DaysReadyCalculator, so the figure moves instantly and offline. The
 * server remains the source of truth: it supplies the baseline and every action's provisions as
 * data attributes, and this only takes the minimum. Rounded towards zero to one decimal, like the
 * server.
 */

/** @typedef {{ water: number, food: number, light: number }} Provisions */

export const WATER_LITRES_PER_PERSON_PER_DAY = 3;
export const LIGHT_HOURS_PER_DAY = 5;

/**
 * @param {Provisions} provisions
 * @param {number} people
 * @returns {number}
 */
export function daysReady(provisions, people) {
  const count = Math.max(1, Math.floor(people));
  const water = provisions.water / (count * WATER_LITRES_PER_PERSON_PER_DAY);
  const food = provisions.food;
  const light = provisions.light / LIGHT_HOURS_PER_DAY;
  const weakest = Math.max(0, Math.min(water, food, light));
  return Math.floor(weakest * 10) / 10;
}

/**
 * @param {Provisions} a
 * @param {Provisions} b
 * @returns {Provisions}
 */
export function plus(a, b) {
  return { water: a.water + b.water, food: a.food + b.food, light: a.light + b.light };
}

/**
 * Reads a non-negative number from a data attribute, treating anything else as zero.
 * @param {string | undefined} raw
 */
export function quantity(raw) {
  const value = Number(raw);
  return Number.isFinite(value) && value >= 0 ? value : 0;
}
