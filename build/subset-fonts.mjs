// @ts-check
/**
 * Makes the WOFF2 subsets in src/Selficient.Web/wwwroot/fonts from the foundries' variable
 * TrueType files. The full faces are not kept in the repository: fetch them again from Google
 * Fonts (Bricolage Grotesque), the Braille Institute (Atkinson Hyperlegible Next) and IBM (Plex
 * Mono), all under the SIL Open Font Licence, drop them beside this script's inputs and run
 *
 *   node build/subset-fonts.mjs <folder holding the .ttf files>
 *
 * The subset is Latin-1 plus the punctuation, currency, arrows and shapes the site renders, and
 * the variable axes are narrowed to the ranges tokens.css declares. Pinning Bricolage's optical
 * size axis alone takes a third off its file; every display setting sits between 1.5 and 4.5 rem.
 */
import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import subsetFont from "subset-font";

const source = process.argv[2];
if (!source) {
  console.error("Say which folder holds the .ttf files.");
  process.exit(2);
}
const target = path.resolve("src/Selficient.Web/wwwroot/fonts");

/**
 * @param {number} from
 * @param {number} to
 */
function range(from, to) {
  let text = "";
  for (let code = from; code <= to; code += 1) {
    text += String.fromCodePoint(code);
  }
  return text;
}

const text = [
  range(0x20, 0x7e),
  range(0xa0, 0xff),
  "ŒœŠšŸŽžˆ˜",
  range(0x2010, 0x2027),
  range(0x2030, 0x203a),
  "⁄€™−∕≈≠≤≥",
  range(0x2190, 0x2199),
  range(0x25a0, 0x25ff),
  "✓✔",
].join("");

/** @type {Array<[string, string, Record<string, number | { min: number, max: number }> | undefined]>} */
const jobs = [
  // Weight 300 and width 75 are the figure on the hero and the scenario cards, as the design mockup drew them (M19).
  ["BricolageGrotesque-Variable.ttf", "BricolageGrotesque.woff2", { opsz: 48, wght: { min: 300, max: 800 }, wdth: { min: 75, max: 100 } }],
  ["AtkinsonHyperlegibleNext-Variable.ttf", "AtkinsonHyperlegibleNext.woff2", { wght: { min: 400, max: 700 } }],
  ["AtkinsonHyperlegibleNext-Italic-Variable.ttf", "AtkinsonHyperlegibleNext-Italic.woff2", { wght: { min: 400, max: 700 } }],
  ["IBMPlexMono-Regular.ttf", "IBMPlexMono-Regular.woff2", undefined],
  ["IBMPlexMono-Medium.ttf", "IBMPlexMono-Medium.woff2", undefined],
];

for (const [input, output, variationAxes] of jobs) {
  const face = await readFile(path.join(source, input));
  const subset = await subsetFont(face, text, variationAxes ? { targetFormat: "woff2", variationAxes } : { targetFormat: "woff2" });
  await writeFile(path.join(target, output), subset);
  console.log(`${input} ${face.length} bytes -> ${output} ${subset.length} bytes`);
}
