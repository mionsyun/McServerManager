import { cpSync, existsSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { relative, resolve } from "node:path";

const publicRoot = resolve(process.cwd(), "public");
const sourceDocs = resolve(publicRoot, "docs");
const aliases = ["LPdocs", "lpdocs"];
const siteUrl = (process.env.NUXT_PUBLIC_SITE_URL || "https://www.maipilot.jp").replace(/\/+$/, "");

if (!existsSync(sourceDocs)) {
  throw new Error(`Source docs directory not found: ${sourceDocs}`);
}

const walkHtmlFiles = (dir) => {
  const result = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const fullPath = resolve(dir, entry.name);
    if (entry.isDirectory()) {
      result.push(...walkHtmlFiles(fullPath));
      continue;
    }
    if (entry.isFile() && entry.name.toLowerCase().endsWith(".html")) {
      result.push(fullPath);
    }
  }
  return result;
};

const getCanonicalFromAlias = (aliasRoot, filePath) => {
  const rel = relative(aliasRoot, filePath).replace(/\\/g, "/");
  if (rel === "index.html") {
    return `${siteUrl}/docs/`;
  }
  if (rel.endsWith("/index.html")) {
    return `${siteUrl}/docs/${rel.slice(0, -"/index.html".length)}/`;
  }
  return `${siteUrl}/docs/${rel}`;
};

const upsertInHead = (html, regex, tag) => {
  if (regex.test(html)) {
    return html.replace(regex, tag);
  }
  return html.replace("</head>", `  ${tag}\n  </head>`);
};

for (const alias of aliases) {
  const targetDir = resolve(publicRoot, alias);
  rmSync(targetDir, { recursive: true, force: true });
  cpSync(sourceDocs, targetDir, { recursive: true, force: true });
  const aliasFiles = walkHtmlFiles(targetDir);
  for (const filePath of aliasFiles) {
    const canonical = getCanonicalFromAlias(targetDir, filePath);
    const original = readFileSync(filePath, "utf8");
    let html = original;
    html = upsertInHead(html, /<meta\s+name=["']robots["'][^>]*>/i, `<meta name="robots" content="noindex,follow" />`);
    html = upsertInHead(html, /<link\s+rel=["']canonical["'][^>]*>/i, `<link rel="canonical" href="${canonical}" />`);
    html = upsertInHead(html, /<meta\s+property=["']og:url["'][^>]*>/i, `<meta property="og:url" content="${canonical}" />`);
    if (html !== original) {
      writeFileSync(filePath, html, "utf8");
    }
  }
  console.log(`Created docs alias: ${targetDir}`);
}
