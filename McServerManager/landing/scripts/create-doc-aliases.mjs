import { cpSync, existsSync, rmSync } from "node:fs";
import { resolve } from "node:path";

const publicRoot = resolve(process.cwd(), "public");
const sourceDocs = resolve(publicRoot, "docs");
const aliases = ["LPdocs", "lpdocs"];

if (!existsSync(sourceDocs)) {
  throw new Error(`Source docs directory not found: ${sourceDocs}`);
}

for (const alias of aliases) {
  const targetDir = resolve(publicRoot, alias);
  rmSync(targetDir, { recursive: true, force: true });
  cpSync(sourceDocs, targetDir, { recursive: true, force: true });
  console.log(`Created docs alias: ${targetDir}`);
}
