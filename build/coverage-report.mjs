/*
 * Coverage gate for Selficient. Carried forward from SmartReminder and extended to branches.
 *
 * Reads Cobertura reports, merges them, and prints per-assembly line and branch coverage, failing
 * when a gated assembly is below threshold or missing from every report.
 *
 * Why a script rather than a tool flag: the headline rate in a Cobertura report averages every
 * assembly the run touched, so a well-covered assembly can hide a neglected one. This recomputes
 * from line data for the assemblies that matter, excluding source-generated files (*.g.cs) because
 * covering a LoggerMessage delegate cache is covering the SDK.
 *
 * Usage: node build/coverage-report.mjs <report.xml>[,<report.xml>...] [Assembly=threshold ...]
 */
import { readFileSync } from "node:fs";

const [reports, ...targets] = process.argv.slice(2);

const thresholds = new Map(
  targets.map((target) => {
    const [name, value] = target.split("=");
    return [name, value === undefined ? 1 : Number(value)];
  }),
);

/** Per assembly: "file:line" -> { hit: boolean, covered: number, total: number } merged by max. */
const lines = new Map();

const isGenerated = (file) => /\.g\.cs$/i.test(file) || file.includes("Generators") || /\.cshtml\.g\.cs$/i.test(file);

// The composition root's entry point (docs/coverage-exclusions.md): it builds the real host and
// starts Kestrel. Everything it wires is exercised through WebApplicationFactory, which replaces the
// entry point rather than running it. Only a file literally named Program.cs qualifies.
const isEntryPoint = (file) => /[\\/]Program\.cs$/i.test(file);

for (const reportPath of reports.split(",")) {
  const xml = readFileSync(reportPath, "utf8");

  for (const packageBlock of xml.split("<package ").slice(1)) {
    const assembly = /name="([^"]+)"/.exec(packageBlock)?.[1];
    if (!assembly) continue;

    for (const classBlock of packageBlock.split("<class ").slice(1)) {
      const file = /filename="([^"]+)"/.exec(classBlock)?.[1] ?? "";
      if (isGenerated(file) || isEntryPoint(file)) continue;

      const seen = lines.get(assembly) ?? new Map();

      for (const match of classBlock.matchAll(/<line number="(\d+)" hits="(\d+)"([^>]*)>/g)) {
        const [, number, hits, rest] = match;
        const key = `${file}:${number}`;
        const previous = seen.get(key) ?? { hit: false, covered: 0, total: 0 };
        const condition = /condition-coverage="\d+% \((\d+)\/(\d+)\)"/.exec(rest);
        const covered = condition ? Number(condition[1]) : 0;
        const total = condition ? Number(condition[2]) : 0;
        seen.set(key, {
          hit: previous.hit || hits !== "0",
          covered: Math.max(previous.covered, covered),
          total: Math.max(previous.total, total),
        });
      }

      lines.set(assembly, seen);
    }
  }
}

const pct = (value) => `${(value * 100).toFixed(2)}%`;
let failed = false;

console.log("Coverage by assembly (generated sources excluded)\n");

for (const [assembly, threshold] of thresholds) {
  if (!lines.has(assembly)) {
    console.log(`  FAIL ${assembly.padEnd(32)} absent from every report (threshold ${pct(threshold)})`);
    failed = true;
  }
}

for (const [assembly, seen] of [...lines].sort()) {
  const entries = [...seen.values()];
  const lineTotal = entries.length;
  const lineCovered = entries.filter((e) => e.hit).length;
  const lineRate = lineTotal === 0 ? 1 : lineCovered / lineTotal;
  const branchTotal = entries.reduce((sum, e) => sum + e.total, 0);
  const branchCovered = entries.reduce((sum, e) => sum + e.covered, 0);
  const branchRate = branchTotal === 0 ? 1 : branchCovered / branchTotal;

  const threshold = thresholds.get(assembly);
  const gated = threshold !== undefined;
  const pass = lineRate >= (threshold ?? 0) && branchRate >= (threshold ?? 0);
  const status = !gated ? " " : pass ? "PASS" : "FAIL";

  console.log(
    `  ${status.padEnd(5)}${assembly.padEnd(32)} lines ${pct(lineRate).padStart(8)} (${lineCovered}/${lineTotal})` +
      `  branches ${pct(branchRate).padStart(8)} (${branchCovered}/${branchTotal})` +
      (gated ? `  threshold ${pct(threshold)}` : ""),
  );

  if (gated && !pass) {
    failed = true;

    const missed = new Map();
    for (const [key, entry] of seen) {
      if (entry.hit && entry.covered === entry.total) continue;
      const file = key.slice(0, key.lastIndexOf(":"));
      const line = Number(key.slice(key.lastIndexOf(":") + 1));
      const short = file.split(/[\\/]/).slice(-2).join("/");
      const label = entry.hit ? `${line}(b)` : `${line}`;
      missed.set(short, (missed.get(short) ?? []).concat(label));
    }

    for (const [file, missedLines] of [...missed].sort()) {
      console.log(`         ${file}: ${missedLines.join(", ")}`);
    }
  }
}

process.exitCode = failed ? 1 : 0;
