import { existsSync, readdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { relative, resolve } from "node:path";

const siteUrl = (process.env.NUXT_PUBLIC_SITE_URL || "https://www.maipilot.jp").replace(/\/+$/, "");
const docsRoot = resolve(process.cwd(), "public", "docs");
const sitemapPath = resolve(process.cwd(), "public", "sitemap.xml");
const appVuePath = resolve(process.cwd(), "app.vue");

if (!existsSync(docsRoot)) {
  throw new Error(`docs directory not found: ${docsRoot}`);
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

const escapeAttr = (value) =>
  String(value)
    .replace(/&/g, "&amp;")
    .replace(/"/g, "&quot;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");

const stripHtml = (value) =>
  value
    .replace(/<script[\s\S]*?<\/script>/gi, " ")
    .replace(/<style[\s\S]*?<\/style>/gi, " ")
    .replace(/<[^>]+>/g, " ")
    .replace(/\s+/g, " ")
    .trim();

const getDocPath = (filePath) => {
  const rel = relative(docsRoot, filePath).replace(/\\/g, "/");
  if (rel === "index.html") {
    return "/docs/";
  }
  if (rel.endsWith("/index.html")) {
    return `/docs/${rel.slice(0, -"/index.html".length)}/`;
  }
  return `/docs/${rel}`;
};

const upsertInHead = (html, regex, tag) => {
  if (regex.test(html)) {
    return html.replace(regex, tag);
  }
  return html.replace("</head>", `  ${tag}\n  </head>`);
};

const toDate = (date) => {
  const y = date.getUTCFullYear();
  const m = String(date.getUTCMonth() + 1).padStart(2, "0");
  const d = String(date.getUTCDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
};

const resolveSeoDefaults = (html, docPath) => {
  const titleMatch = html.match(/<title>([\s\S]*?)<\/title>/i);
  const title = stripHtml(titleMatch?.[1] || `MaiPilot | ${docPath}`);

  const descMatch = html.match(/<meta\s+name=["']description["']\s+content=["']([\s\S]*?)["']\s*\/?>/i);
  let description = stripHtml(descMatch?.[1] || "");
  if (!description) {
    const firstParagraph = html.match(/<p[^>]*>([\s\S]*?)<\/p>/i);
    description = stripHtml(firstParagraph?.[1] || "");
  }
  if (!description) {
    description = `${title} - MaiPilot docs page.`;
  }
  if (description.length > 160) {
    description = `${description.slice(0, 157)}...`;
  }

  return { title, description };
};

const getSitemapMeta = (docPath) => {
  if (docPath === "/docs/") {
    return { changefreq: "monthly", priority: "0.8" };
  }
  if (docPath === "/docs/growth/") {
    return { changefreq: "weekly", priority: "0.8" };
  }
  if (docPath === "/docs/java-setup/" || docPath === "/docs/privacy-and-network/") {
    return { changefreq: "monthly", priority: "0.8" };
  }
  return { changefreq: "monthly", priority: "0.7" };
};

const files = walkHtmlFiles(docsRoot);
let updatedCount = 0;
const sitemapDocs = [];

for (const filePath of files) {
  const original = readFileSync(filePath, "utf8");
  const docPath = getDocPath(filePath);
  const canonicalUrl = `${siteUrl}${docPath}`;
  const { title, description } = resolveSeoDefaults(original, docPath);

  let html = original;
  html = upsertInHead(html, /<meta\s+name=["']description["'][^>]*>/i, `<meta name="description" content="${escapeAttr(description)}" />`);
  html = upsertInHead(html, /<meta\s+name=["']robots["'][^>]*>/i, `<meta name="robots" content="index,follow" />`);
  html = upsertInHead(html, /<link\s+rel=["']canonical["'][^>]*>/i, `<link rel="canonical" href="${escapeAttr(canonicalUrl)}" />`);
  html = upsertInHead(html, /<meta\s+property=["']og:title["'][^>]*>/i, `<meta property="og:title" content="${escapeAttr(title)}" />`);
  html = upsertInHead(html, /<meta\s+property=["']og:description["'][^>]*>/i, `<meta property="og:description" content="${escapeAttr(description)}" />`);
  html = upsertInHead(html, /<meta\s+property=["']og:type["'][^>]*>/i, `<meta property="og:type" content="article" />`);
  html = upsertInHead(html, /<meta\s+property=["']og:url["'][^>]*>/i, `<meta property="og:url" content="${escapeAttr(canonicalUrl)}" />`);
  html = upsertInHead(html, /<meta\s+name=["']twitter:card["'][^>]*>/i, `<meta name="twitter:card" content="summary" />`);
  html = upsertInHead(html, /<meta\s+name=["']twitter:title["'][^>]*>/i, `<meta name="twitter:title" content="${escapeAttr(title)}" />`);
  html = upsertInHead(html, /<meta\s+name=["']twitter:description["'][^>]*>/i, `<meta name="twitter:description" content="${escapeAttr(description)}" />`);

  if (html !== original) {
    writeFileSync(filePath, html, "utf8");
    updatedCount += 1;
  }

  const fileStat = statSync(filePath);
  sitemapDocs.push({
    path: docPath,
    lastmod: toDate(fileStat.mtime),
    ...getSitemapMeta(docPath),
  });
}

const appLastMod = existsSync(appVuePath) ? toDate(statSync(appVuePath).mtime) : toDate(new Date());
const sitemapEntries = [
  { path: "/", changefreq: "weekly", priority: "1.0", lastmod: appLastMod },
  ...sitemapDocs.sort((a, b) => a.path.localeCompare(b.path)),
];

const sitemapXml = [
  '<?xml version="1.0" encoding="UTF-8"?>',
  '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">',
  ...sitemapEntries.map(
    (entry) => [
      "  <url>",
      `    <loc>${siteUrl}${entry.path}</loc>`,
      `    <changefreq>${entry.changefreq}</changefreq>`,
      `    <priority>${entry.priority}</priority>`,
      `    <lastmod>${entry.lastmod}</lastmod>`,
      "  </url>",
    ].join("\n")
  ),
  "</urlset>",
  "",
].join("\n");

writeFileSync(sitemapPath, sitemapXml, "utf8");
console.log(`Prepared SEO metadata for docs files: ${updatedCount}/${files.length} updated`);
console.log(`Generated sitemap: ${sitemapEntries.length} URLs`);
