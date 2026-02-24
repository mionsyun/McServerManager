import { cpSync, existsSync, rmSync } from "node:fs";
import { resolve } from "node:path";

const publicRoot = resolve(process.cwd(), "public");
const outputRoot = resolve(process.cwd(), ".output", "public");
const docDirs = ["docs", "LPdocs", "lpdocs"];

if (!existsSync(outputRoot)) {
  throw new Error(`Nuxt output directory not found: ${outputRoot}`);
}

for (const dir of docDirs) {
  const source = resolve(publicRoot, dir);
  const target = resolve(outputRoot, dir);
  if (!existsSync(source)) {
    continue;
  }

  rmSync(target, { recursive: true, force: true });
  cpSync(source, target, { recursive: true, force: true });
  console.log(`Synced static docs to output: ${target}`);
}
