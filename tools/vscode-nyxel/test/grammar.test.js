"use strict";
// Highlighting snapshots: every sample and every file in test/cases is tokenized with VS Code's own TextMate engine
// (vscode-textmate and vscode-oniguruma), and the scopes are compared with test/snapshots. After an intended change
// to the grammar or a sample, run `NYXEL_UPDATE_SNAPSHOTS=1 npm test` and review the diff before committing.
const assert = require("node:assert");
const fs = require("node:fs");
const path = require("node:path");
const { test } = require("node:test");
const vsctm = require("vscode-textmate");
const oniguruma = require("vscode-oniguruma");

const extensionRoot = path.join(__dirname, "..");
const samplesRoot = path.join(extensionRoot, "..", "..", "samples");
const casesRoot = path.join(__dirname, "cases");
const snapshotsRoot = path.join(__dirname, "snapshots");
const update = process.env.NYXEL_UPDATE_SNAPSHOTS === "1";

async function loadGrammar() {
  const wasm = fs.readFileSync(path.join(path.dirname(require.resolve("vscode-oniguruma")), "onig.wasm"));
  await oniguruma.loadWASM(wasm.buffer.slice(wasm.byteOffset, wasm.byteOffset + wasm.byteLength));
  const grammarPath = path.join(extensionRoot, "syntaxes", "nyxel.tmLanguage.json");
  const registry = new vsctm.Registry({
    onigLib: Promise.resolve({
      createOnigScanner: (patterns) => new oniguruma.OnigScanner(patterns),
      createOnigString: (text) => new oniguruma.OnigString(text),
    }),
    loadGrammar: async () => vsctm.parseRawGrammar(fs.readFileSync(grammarPath, "utf8"), grammarPath),
  });
  return registry.loadGrammar("source.nyxel");
}

/**
 * For each line with highlighted tokens: the line after "> ", then every token that has a scope besides
 * source.nyxel, as its text and its scopes from the outside in ("text"=scope scope). Plain identifiers, operators
 * and whitespace have no scope and are left out.
 */
function describeScopes(grammar, text) {
  const out = [];
  let state = vsctm.INITIAL;
  for (const line of text.split(/\r?\n/)) {
    const result = grammar.tokenizeLine(line, state);
    state = result.ruleStack;
    const tokens = result.tokens
      .map((token) => [
        line.substring(token.startIndex, token.endIndex),
        token.scopes.slice(1).map((scope) => scope.replace(/\.nyxel$/, "")).join(" "),
      ])
      .filter(([token, scopes]) => token.trim() !== "" && scopes !== "");
    if (tokens.length > 0) {
      out.push(`> ${line.trim()}`);
      out.push(`  ${tokens.map(([token, scopes]) => `${JSON.stringify(token)}=${scopes}`).join("  |  ")}`);
    }
  }
  return out.join("\n") + "\n";
}

/** Snapshot name and path of every .nyxel file under a folder: Combat/Health.nyxel is Combat.Health.nyxel.scopes. */
function sources(root, prefix) {
  return fs
    .readdirSync(root, { recursive: true })
    .filter((file) => file.endsWith(".nyxel"))
    .map((file) => [prefix + file.split(path.sep).join(".") + ".scopes", path.join(root, file)])
    .sort(([a], [b]) => a.localeCompare(b));
}

test("highlighting matches the snapshots", async (t) => {
  const grammar = await loadGrammar();
  const files = [...sources(samplesRoot, ""), ...sources(casesRoot, "cases.")];
  assert.ok(files.length > 16, "samples/ and test/cases/ were not found");
  fs.mkdirSync(snapshotsRoot, { recursive: true });

  for (const [name, file] of files) {
    await t.test(name, () => {
      const actual = describeScopes(grammar, fs.readFileSync(file, "utf8"));
      const snapshot = path.join(snapshotsRoot, name);
      if (update) {
        fs.writeFileSync(snapshot, actual);
        return;
      }
      assert.ok(fs.existsSync(snapshot), `${name} has no snapshot; run NYXEL_UPDATE_SNAPSHOTS=1 npm test`);
      assert.strictEqual(actual, fs.readFileSync(snapshot, "utf8").replace(/\r\n/g, "\n"));
    });
  }

  await t.test("every snapshot has a source file", () => {
    const names = new Set(files.map(([name]) => name));
    const stale = fs.readdirSync(snapshotsRoot).filter((name) => !names.has(name));
    if (update) {
      stale.forEach((name) => fs.rmSync(path.join(snapshotsRoot, name)));
      return;
    }
    assert.deepStrictEqual(stale, [], "snapshots without a source; run NYXEL_UPDATE_SNAPSHOTS=1 npm test");
  });
});
