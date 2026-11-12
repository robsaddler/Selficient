// @ts-check
/**
 * A plan said in a clause, for the moments a person has to tell two plans apart: a copy arriving
 * from another device, or a device and an account that each hold one. It says the three things a
 * household would recognise its plan by, and nothing that identifies a house.
 */

/**
 * "A plan for 4 people in NP12, 9 things ticked"
 * @param {import("selficient/plan-store.js").Snapshot} plan
 * @param {string} opening the words before "for N people"
 */
export function describe(plan, opening) {
  const people = Number(plan.profile?.People);
  const who = Number.isInteger(people) && people > 0 ? ` for ${people === 1 ? "1 person" : `${people} people`}` : "";
  const district = outward(plan.profile?.Postcode);
  const where = district ? ` in ${district}` : "";
  const ticked = plan.ticks.length === 0 ? "nothing ticked yet" : plan.ticks.length === 1 ? "1 thing ticked" : `${plan.ticks.length} things ticked`;
  return `${opening}${who}${where}, ${ticked}`;
}

/**
 * The outward half of a postcode, which names a district and not a street.
 * @param {string | undefined} postcode
 */
export function outward(postcode) {
  const tight = (postcode ?? "").toUpperCase().replaceAll(/\s/g, "");
  const out = tight.length >= 5 ? tight.slice(0, -3) : "";
  return /^[A-Z]{1,2}[0-9][0-9A-Z]?$/.test(out) ? out : "";
}
