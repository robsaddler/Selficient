// @ts-check
/**
 * A QR code drawn in the browser. The link a household sends to its other device carries the key
 * that opens the copy, and that key must never reach the server, so the server cannot draw this
 * one (it draws the operator's enrolment code, whose secret it already holds).
 *
 * Byte mode, error correction level M, versions 1 to 9: up to 180 bytes, where a link is about
 * 100. Written from ISO/IEC 18004; the browser journeys read every code back with an independent
 * decoder. The code is always dark on white whatever the theme, because one inverted in the dark
 * does not scan.
 */

/** Error-correction codewords per block, then [blocks, data codewords in each] groups, by version, at level M. */
const BLOCKS = /** @type {[number, [number, number][]][]} */ ([
  [0, []],
  [10, [[1, 16]]],
  [16, [[1, 28]]],
  [26, [[1, 44]]],
  [18, [[2, 32]]],
  [24, [[2, 43]]],
  [16, [[4, 27]]],
  [18, [[4, 31]]],
  [22, [[2, 38], [2, 39]]],
  [22, [[3, 36], [2, 37]]],
]);

/** Where the alignment patterns centre, by version. */
const ALIGNMENT = [[], [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34], [6, 22, 38], [6, 24, 42], [6, 26, 46]];

const QUIET = 4;

/** The most bytes a code here can carry. */
export const MAX_BYTES = dataCodewords(9) - 2;

/**
 * The blocks of a version: how many error-correction codewords each has, and the groups.
 * @param {number} version
 * @returns {[number, [number, number][]]}
 */
function blocksOf(version) {
  return BLOCKS[version] ?? [0, []];
}

/** @param {number} version */
function dataCodewords(version) {
  return blocksOf(version)[1].reduce((sum, [count, size]) => sum + count * size, 0);
}

/**
 * A square of modules. Reading outside it is light, which is what the quiet zone is.
 */
class Grid {
  /** @param {number} size */
  constructor(size) {
    this.size = size;
    this.cells = new Uint8Array(size * size);
  }

  /** @param {number} x @param {number} y */
  get(x, y) {
    return this.cells[y * this.size + x] === 1;
  }

  /** @param {number} x @param {number} y @param {boolean} on */
  set(x, y, on) {
    this.cells[y * this.size + x] = on ? 1 : 0;
  }

  /** @returns {boolean[][]} */
  rows() {
    return Array.from({ length: this.size }, (_, y) => Array.from({ length: this.size }, (_, x) => this.get(x, y)));
  }
}

/**
 * @param {ArrayLike<number>} list
 * @param {number} i
 */
function nth(list, i) {
  return list[i] ?? 0;
}

/**
 * The modules of the code for a text, as rows of booleans, true for dark. No quiet zone.
 * @param {string} text
 * @returns {boolean[][]}
 */
export function qrMatrix(text) {
  const bytes = new TextEncoder().encode(text);
  let version = 1;
  while (version <= 9 && dataCodewords(version) - 2 < bytes.length) {
    version++;
  }
  if (version > 9) {
    throw new RangeError("That text is too long for a code.");
  }

  const codewords = interleave(version, pad(bytes, dataCodewords(version)));
  const size = 17 + 4 * version;
  const dark = new Grid(size);
  const fixed = new Grid(size);
  drawFunctionPatterns(version, dark, fixed);
  drawCodewords(codewords, dark, fixed);

  let best = 0;
  let least = Infinity;
  for (let mask = 0; mask < 8; mask++) {
    applyMask(mask, dark, fixed);
    drawFormat(mask, dark, fixed);
    const penalty = score(dark);
    if (penalty < least) {
      least = penalty;
      best = mask;
    }
    applyMask(mask, dark, fixed);
  }
  applyMask(best, dark, fixed);
  drawFormat(best, dark, fixed);
  return dark.rows();
}

/**
 * The code as an SVG element: one white square and one path, a bar for each run of dark modules.
 * @param {Document} doc
 * @param {string} text
 * @returns {SVGSVGElement}
 */
export function qrSvg(doc, text) {
  const modules = qrMatrix(text);
  const size = modules.length + QUIET * 2;
  const svgNs = "http://www.w3.org/2000/svg";
  const svg = doc.createElementNS(svgNs, "svg");
  svg.setAttribute("viewBox", `0 0 ${size} ${size}`);
  svg.setAttribute("shape-rendering", "crispEdges");
  svg.setAttribute("focusable", "false");
  svg.setAttribute("aria-hidden", "true");
  const ground = doc.createElementNS(svgNs, "rect");
  ground.setAttribute("width", String(size));
  ground.setAttribute("height", String(size));
  ground.setAttribute("fill", "#fff");
  const bars = doc.createElementNS(svgNs, "path");
  bars.setAttribute("fill", "#000");
  bars.setAttribute("d", qrPath(modules));
  svg.append(ground, bars);
  return svg;
}

/**
 * @param {boolean[][]} modules
 * @returns {string}
 */
export function qrPath(modules) {
  let d = "";
  modules.forEach((row, y) => {
    let x = 0;
    while (x < row.length) {
      if (!row[x]) {
        x++;
        continue;
      }
      let run = 1;
      while (x + run < row.length && row[x + run]) {
        run++;
      }
      d += `M${x + QUIET} ${y + QUIET}h${run}v1h-${run}z`;
      x += run;
    }
  });
  return d;
}

/**
 * The mode, the count and the bytes, then the terminator and the pad codewords up to capacity.
 * Twelve bits of header and four of terminator make the stream whole bytes with nothing to align.
 * @param {Uint8Array} bytes
 * @param {number} capacity
 * @returns {number[]}
 */
function pad(bytes, capacity) {
  const n = bytes.length;
  const out = [0x40 | (n >> 4)];
  let carried = (n & 15) << 4;
  for (const b of bytes) {
    out.push(carried | (b >> 4));
    carried = (b & 15) << 4;
  }
  out.push(carried);
  for (let fill = 0xec; out.length < capacity; fill ^= 0xec ^ 0x11) {
    out.push(fill);
  }
  return out;
}

/**
 * Splits the data into the version's blocks, gives each its Reed-Solomon codewords, and deals
 * them out a codeword at a time across the blocks, data first.
 * @param {number} version
 * @param {number[]} data
 * @returns {number[]}
 */
function interleave(version, data) {
  const [ecLength, groups] = blocksOf(version);
  const divisor = rsDivisor(ecLength);
  /** @type {{ data: number[], ec: number[] }[]} */
  const blocks = [];
  let at = 0;
  for (const [count, length] of groups) {
    for (let i = 0; i < count; i++) {
      const block = data.slice(at, at + length);
      at += length;
      blocks.push({ data: block, ec: rsRemainder(block, divisor) });
    }
  }
  const out = [];
  const longest = Math.max(...blocks.map((block) => block.data.length));
  for (let i = 0; i < longest; i++) {
    for (const block of blocks) {
      if (i < block.data.length) {
        out.push(nth(block.data, i));
      }
    }
  }
  for (let i = 0; i < ecLength; i++) {
    for (const block of blocks) {
      out.push(nth(block.ec, i));
    }
  }
  return out;
}

/**
 * Multiplication in GF(2^8) over the polynomial x^8 + x^4 + x^3 + x^2 + 1.
 * @param {number} x
 * @param {number} y
 */
function gfMultiply(x, y) {
  let z = 0;
  for (let i = 7; i >= 0; i--) {
    z = (z << 1) ^ ((z >> 7) * 0x11d);
    z ^= ((y >> i) & 1) * x;
  }
  return z;
}

/**
 * The generator polynomial of a degree, without its leading one.
 * @param {number} degree
 * @returns {number[]}
 */
function rsDivisor(degree) {
  const result = new Uint8Array(degree);
  result[degree - 1] = 1;
  let root = 1;
  for (let i = 0; i < degree; i++) {
    for (let j = 0; j < degree; j++) {
      result[j] = gfMultiply(nth(result, j), root) ^ nth(result, j + 1);
    }
    root = gfMultiply(root, 2);
  }
  return [...result];
}

/**
 * @param {number[]} data
 * @param {number[]} divisor
 * @returns {number[]}
 */
function rsRemainder(data, divisor) {
  /** @type {number[]} */
  let result = new Array(divisor.length).fill(0);
  for (const b of data) {
    const factor = b ^ nth(result, 0);
    const before = result;
    result = divisor.map((coefficient, i) => nth(before, i + 1) ^ gfMultiply(coefficient, factor));
  }
  return result;
}

/**
 * The patterns a reader finds the code by: timing lines, the three finders, the alignment
 * squares, the version on larger codes, and room kept for the format bits.
 * @param {number} version
 * @param {Grid} dark
 * @param {Grid} fixed
 */
function drawFunctionPatterns(version, dark, fixed) {
  const size = dark.size;
  const set = (/** @type {number} */ x, /** @type {number} */ y, /** @type {boolean} */ on) => {
    dark.set(x, y, on);
    fixed.set(x, y, true);
  };

  for (let i = 0; i < size; i++) {
    set(6, i, i % 2 === 0);
    set(i, 6, i % 2 === 0);
  }

  for (const [cx, cy] of /** @type {[number, number][]} */ ([[3, 3], [size - 4, 3], [3, size - 4]])) {
    for (let dy = -4; dy <= 4; dy++) {
      for (let dx = -4; dx <= 4; dx++) {
        const x = cx + dx;
        const y = cy + dy;
        if (x >= 0 && x < size && y >= 0 && y < size) {
          const ring = Math.max(Math.abs(dx), Math.abs(dy));
          set(x, y, ring !== 2 && ring !== 4);
        }
      }
    }
  }

  const centres = ALIGNMENT[version] ?? [];
  const last = centres.length - 1;
  centres.forEach((cy, i) => {
    centres.forEach((cx, j) => {
      // The three corners that hold a finder have no alignment square.
      if ((i === 0 && j === 0) || (i === 0 && j === last) || (i === last && j === 0)) {
        return;
      }
      for (let dy = -2; dy <= 2; dy++) {
        for (let dx = -2; dx <= 2; dx++) {
          set(cx + dx, cy + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
        }
      }
    });
  });

  // The format bits are written once the mask is chosen; any mask reserves the same modules.
  drawFormat(0, dark, fixed);

  if (version >= 7) {
    let remainder = version;
    for (let i = 0; i < 12; i++) {
      remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1f25);
    }
    const bits = (version << 12) | remainder;
    for (let i = 0; i < 18; i++) {
      const on = ((bits >> i) & 1) === 1;
      const a = size - 11 + (i % 3);
      const b = Math.floor(i / 3);
      set(a, b, on);
      set(b, a, on);
    }
  }
}

/**
 * The two copies of the fifteen format bits: level M and the mask, with their error correction.
 * @param {number} mask
 * @param {Grid} dark
 * @param {Grid} fixed
 */
function drawFormat(mask, dark, fixed) {
  const size = dark.size;
  const set = (/** @type {number} */ x, /** @type {number} */ y, /** @type {boolean} */ on) => {
    dark.set(x, y, on);
    fixed.set(x, y, true);
  };
  // Level M is written as two zero bits, so the data is the mask alone.
  let remainder = mask;
  for (let i = 0; i < 10; i++) {
    remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
  }
  const bits = ((mask << 10) | remainder) ^ 0x5412;
  const bit = (/** @type {number} */ i) => ((bits >> i) & 1) === 1;

  for (let i = 0; i <= 5; i++) {
    set(8, i, bit(i));
  }
  set(8, 7, bit(6));
  set(8, 8, bit(7));
  set(7, 8, bit(8));
  for (let i = 9; i < 15; i++) {
    set(14 - i, 8, bit(i));
  }
  for (let i = 0; i < 8; i++) {
    set(size - 1 - i, 8, bit(i));
  }
  for (let i = 8; i < 15; i++) {
    set(8, size - 15 + i, bit(i));
  }
  set(8, size - 8, true);
}

/**
 * Lays the codewords in the zigzag the standard asks for: two columns at a time from the right,
 * up then down, stepping over the vertical timing line.
 * @param {number[]} codewords
 * @param {Grid} dark
 * @param {Grid} fixed
 */
function drawCodewords(codewords, dark, fixed) {
  const size = dark.size;
  let i = 0;
  for (let right = size - 1; right >= 1; right -= 2) {
    if (right === 6) {
      right = 5;
    }
    for (let step = 0; step < size; step++) {
      for (let j = 0; j < 2; j++) {
        const x = right - j;
        const upward = ((right + 1) & 2) === 0;
        const y = upward ? size - 1 - step : step;
        if (!fixed.get(x, y) && i < codewords.length * 8) {
          dark.set(x, y, ((nth(codewords, i >> 3) >> (7 - (i & 7))) & 1) === 1);
          i++;
        }
      }
    }
  }
}

/**
 * Flips the data modules a mask selects. Applying the same mask twice puts them back.
 * @param {number} mask
 * @param {Grid} dark
 * @param {Grid} fixed
 */
function applyMask(mask, dark, fixed) {
  const size = dark.size;
  const chooses = MASKS[mask] ?? (() => false);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      if (!fixed.get(x, y) && chooses(x, y)) {
        dark.set(x, y, !dark.get(x, y));
      }
    }
  }
}

/** @type {((x: number, y: number) => boolean)[]} */
const MASKS = [
  (x, y) => (x + y) % 2 === 0,
  (_, y) => y % 2 === 0,
  (x) => x % 3 === 0,
  (x, y) => (x + y) % 3 === 0,
  (x, y) => (Math.floor(x / 3) + Math.floor(y / 2)) % 2 === 0,
  (x, y) => ((x * y) % 2) + ((x * y) % 3) === 0,
  (x, y) => (((x * y) % 2) + ((x * y) % 3)) % 2 === 0,
  (x, y) => (((x + y) % 2) + ((x * y) % 3)) % 2 === 0,
];

/**
 * How hard a masked code is to read, by the standard's four rules: long runs, solid blocks,
 * shapes that look like a finder, and an uneven share of dark. Lower is better.
 * @param {Grid} dark
 */
function score(dark) {
  const size = dark.size;
  let penalty = 0;
  /** @type {string[]} */
  const lines = [];
  for (let a = 0; a < size; a++) {
    let across = "";
    let down = "";
    for (let b = 0; b < size; b++) {
      across += dark.get(b, a) ? "1" : "0";
      down += dark.get(a, b) ? "1" : "0";
    }
    lines.push(across, down);
  }
  for (const line of lines) {
    for (const run of line.match(/0{5,}|1{5,}/g) ?? []) {
      penalty += run.length - 2;
    }
    penalty += 40 * (count(line, "10111010000") + count(line, "00001011101"));
  }
  for (let y = 0; y < size - 1; y++) {
    for (let x = 0; x < size - 1; x++) {
      const on = dark.get(x, y);
      if (on === dark.get(x + 1, y) && on === dark.get(x, y + 1) && on === dark.get(x + 1, y + 1)) {
        penalty += 3;
      }
    }
  }
  const total = size * size;
  const filled = dark.cells.reduce((sum, cell) => sum + cell, 0);
  penalty += 10 * (Math.ceil(Math.abs(filled * 20 - total * 10) / total) - 1);
  return penalty;
}

/**
 * @param {string} line
 * @param {string} shape
 */
function count(line, shape) {
  let found = 0;
  for (let at = line.indexOf(shape); at !== -1; at = line.indexOf(shape, at + 1)) {
    found++;
  }
  return found;
}
