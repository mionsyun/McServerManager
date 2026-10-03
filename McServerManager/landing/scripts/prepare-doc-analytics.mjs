import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { resolve, relative } from "node:path";
import { fileURLToPath } from "node:url";

export function walkHtmlFiles(root) {
  return readdirSync(root, { withFileTypes: true }).flatMap((entry) => {
    const path = resolve(root, entry.name);
    return entry.isDirectory() ? walkHtmlFiles(path) : entry.name.endsWith(".html") ? [path] : [];
  }).sort();
}

export function transformDoc(html, measurementId = "G-MM106D2B2Z") {
  if (measurementId && !/^G-[A-Z0-9]+$/.test(measurementId)) throw new Error("Invalid GA measurement ID");
  let result = html.replace(/\s*<!-- Google tag \(gtag\.js\)[\s\S]*?-->/g, "");
  result = result.replace(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi, (full, attrs, body) => {
    if (/\bsrc=["']\/analytics\.js["']/.test(attrs)) return "";
    if (/application\/ld\+json/i.test(attrs)) return full;
    if (body.includes("googletagmanager.com/gtag/js") && body.includes("window.gtag")) return "";
    if (!body.includes("trackEvent")) return full;
    if (body.includes('const buttons = document.querySelectorAll("[data-lang]")')) {
      const start = body.indexOf("      const trackEvent =");
      const end = body.indexOf("      const saved =", start);
      if (start < 0 || end < 0) throw new Error("Unrecognized mixed docs analytics script; refusing to remove UI code");
      const preserved = (body.slice(0, start) + body.slice(end))
        .replace(/\s*document\.addEventListener\("click", handleTrackedClick, \{ passive: true \}\);/, "");
      return `<script${attrs}>${preserved}</script>`;
    }
    // Known dedicated tracking scripts contain no other page UI behavior.
    if (/^\s*const trackEvent =/.test(body)) return "";
    throw new Error("Unrecognized docs analytics script; inspect before migrating");
  });
  // Keep the document's declared UI language aligned with its visible blocks.
  result = result.replace(/const applyLang = \(lang\) => \{\n(?!\s*document\.documentElement\.lang)/g,
    'const applyLang = (lang) => {\n        document.documentElement.lang = lang === "en" ? "en" : "ja";\n');
  result = result.replace(/function apply\(lang\) \{\n(?!\s*document\.documentElement\.lang)/g,
    'function apply(lang) {\n          document.documentElement.lang = lang === "en" ? "en" : "ja";\n');
  // Resolve fragments and relative URLs against this document, never the homepage.
  const canonical = result.match(/<link\b[^>]*rel=["']canonical["'][^>]*href=["']([^"']+)["']/i)?.[1];
  const documentUrl = canonical || "https://www.maipilot.jp/docs/";
  result = result.replace(/<a\b[^>]*>/gi, (tag) => {
    const href = tag.match(/\bhref=["']([^"']*)["']/i)?.[1];
    let destination;
    if (!href) return tag;
    try { destination = new URL(href, documentUrl); } catch { return tag; }
    const home = ["www.maipilot.jp", "maipilot.jp"].includes(destination.hostname) && ["/", "/index.html"].includes(destination.pathname);
    const explicit = /\bdata-event=["'](?:download_click|nav_download_click|growth_download_click|docs_to_download)["']/.test(tag);
    const ctaClass = /\bclass=["'][^"']*\b(?:cta-btn|nav-cta)\b/.test(tag);
    if (!home) {
      // Repair an earlier migration's incorrectly labelled in-article CTA.
      return tag.replace(/\s+data-event=["']docs_to_download["']/, "");
    }
    if (!(explicit || ctaClass || destination.hash === "#download")) return tag;
    if (/\bdata-event=/.test(tag)) return tag.replace(/\bdata-event=["'][^"']*["']/, 'data-event="docs_to_download"');
    return tag.replace(/>$/, ' data-event="docs_to_download">');
  });
  if (!/<\/head>/i.test(result)) throw new Error("Docs page has no head");
  // Remove only the old shared tag line, making repeated builds byte-idempotent.
  result = result.replace(/\n[ \t]*\n(?=[ \t]*<\/head>)/, "\n");
  const tag = `    <script defer src="/analytics.js" data-measurement-id="${measurementId}"></script>`;
  return result.replace(/^[ \t]*<\/head>/m, `${tag}\n  </head>`).replace(/^[ \t]+$/gm, "");
}

export function prepareDocAnalytics(root, measurementId = "G-MM106D2B2Z") {
  const docsRoot = resolve(root, "public/docs");
  const files = walkHtmlFiles(docsRoot);
  const paths = files.map((file) => `/docs/${relative(docsRoot, file).replace(/\\/g, "/").replace(/index\.html$/, "")}`);
  const titles = Object.fromEntries(files.map((file, index) => {
    const title = readFileSync(file, "utf8").match(/<title>([\s\S]*?)<\/title>/i)?.[1] || "MaiPilot";
    return [paths[index], title.replace(/<[^>]*>/g, "").replace(/&amp;/g, "&").replace(/&quot;/g, '\"').replace(/&#39;/g, "'").replace(/\s+/g, " ").trim()];
  }));
  const analyticsPath = resolve(root, "public/analytics.js");
  const originalAnalytics = readFileSync(analyticsPath, "utf8");
  const analytics = originalAnalytics.replace(/(  \/\/ BEGIN DOCUMENT PATHS\n)[\s\S]*?(  \/\/ END DOCUMENT PATHS)/,
    `$1  var DOCUMENT_TITLES = ${JSON.stringify(titles, null, 2).replace(/\n/g, "\n  ")};\n  var DOCUMENT_PATHS = new Set(Object.keys(DOCUMENT_TITLES));\n$2`);
  if (analytics !== originalAnalytics) writeFileSync(analyticsPath, analytics);
  let changed = 0;
  for (const file of files) {
    const original = readFileSync(file, "utf8");
    const next = transformDoc(original, measurementId);
    if (next !== original) { writeFileSync(file, next); changed++; }
  }
  return { total: files.length, changed };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = fileURLToPath(new URL("../", import.meta.url));
  const id = process.env.NUXT_PUBLIC_GA_MEASUREMENT_ID ?? "G-MM106D2B2Z";
  const result = prepareDocAnalytics(root, id.trim());
  console.log(`Prepared shared analytics: ${result.changed}/${result.total} docs updated`);
}
