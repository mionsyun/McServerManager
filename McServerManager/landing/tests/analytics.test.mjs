import test from "node:test";
import assert from "node:assert/strict";
import vm from "node:vm";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { resolve, relative } from "node:path";
import { transformDoc, walkHtmlFiles } from "../scripts/prepare-doc-analytics.mjs";

const root = fileURLToPath(new URL("../", import.meta.url));
const source = readFileSync(resolve(root, "public/analytics.js"), "utf8");
const ID = "G-MM106D2B2Z";
const INSTALLER = "https://stmaipilot.blob.core.windows.net/public/downloads/MaiPilotSetup.exe";

// A tiny DOM double exercises the real classic script with no browser/network.
class Element {
  constructor(tag = "a", attributes = {}, parent = null) {
    this.tagName = tag.toUpperCase(); this.attributes = attributes; this.parentElement = parent; this.nodeType = 1;
    this.id = attributes.id || "";
  }
  getAttribute(key) { return this.attributes[key] ?? null; }
  matches(selector) {
    return selector.split(",").some((part) => {
      part = part.trim();
      const match = part.match(/^(\w+)?(?:\[([^\]]+)\])?$/);
      if (match) return (!match[1] || this.tagName === match[1].toUpperCase()) && (!match[2] || this.getAttribute(match[2]) !== null);
      if (part.startsWith(".")) return (this.attributes.class || "").split(/\s+/).includes(part.slice(1));
      return false;
    });
  }
  closest(selector) { return this.matches(selector) ? this : this.parentElement?.closest(selector) || null; }
}

function fixture({ url = "https://www.maipilot.jp/", lang = "ja", referrer = "", navigator = { userAgent: "Windows NT 10.0", platform: "Win32" }, injected = false, configured = false, auto = true } = {}) {
  const listeners = {};
  const scripts = [];
  const calls = [];
  const document = {
    documentElement: { lang }, referrer, scripts,
    currentScript: auto ? new Element("script", { "data-measurement-id": ID }) : null,
    createElement: (tag) => new Element(tag),
    addEventListener(type, fn) { (listeners[type] ||= []).push(fn); },
    head: { appendChild: (script) => scripts.push(script) }
  };
  const window = { location: new URL(url), navigator, dataLayer: [] };
  if (configured) window.dataLayer.push(["js", new Date()], ["config", ID]);
  if (injected) { window.gtag = (...args) => calls.push(args); scripts.push({ src: `https://www.googletagmanager.com/gtag/js?id=${ID}` }); }
  const context = vm.createContext({ window, document, URL, Date, Set, WeakSet });
  vm.runInContext(source, context);
  const api = window.MaiPilotAnalytics;
  function dispatch(anchor, type = "click", button = 0, extra = {}) {
    const event = { target: anchor, type, button, defaultPrevented: false, ...extra };
    for (const listener of listeners[type] || []) listener(event);
    return event;
  }
  function commands() { return injected ? calls : window.dataLayer.map((entry) => Array.from(entry)); }
  function events() { return commands().filter((entry) => entry[0] === "event"); }
  return { api, window, document, listeners, scripts, context, dispatch, commands, events };
}

const anchor = (attributes = {}, parent = null) => new Element("a", { href: INSTALLER, "data-event": "download_click", ...attributes }, parent);
const eventParams = (f) => JSON.parse(JSON.stringify(f.events().at(-1)[2]));

test("strict production allowlist blocks loopbacks, previews, LAN, file and host spoofing", () => {
  const denied = ["http://localhost:3000/", "http://127.0.0.1/", "http://127.6.7.8/", "http://127.255.255.254/", "http://[::1]/", "https://app.localhost/", "https://localhost/", "file:///tmp/index.html", "https://preview.pages.dev/", "https://maipilot.jp.evil.example/", "http://www.maipilot.jp/", "https://www.maipilot.jp:3000/", "http://192.168.1.2/", "https://maipilot-jp.azurestaticapps.net/"];
  for (const url of denied) {
    for (const injected of [false, true]) {
      const f = fixture({ url, injected });
      assert.equal(f.api.track("download_click"), false, url);
      assert.equal(f.window[`ga-disable-${ID}`], true, url);
      assert.equal(f.events().length, 0, url);
      assert.equal(f.commands().length, 0, url);
      assert.equal(f.scripts.length, injected ? 1 : 0, url);
      assert.equal(Object.keys(f.listeners).length, 0, url);
    }
  }
  assert.equal(fixture({ url: "https://maipilot.jp/" }).scripts.length, 1);
});

test("production bootstrap, repeated init and reexecuted script create only one loader/listener/config", () => {
  const f = fixture();
  assert.equal(f.scripts.length, 1);
  assert.equal(f.scripts[0].src, `https://www.googletagmanager.com/gtag/js?id=${ID}`);
  assert.equal(f.api.init({ measurementId: ID }), true);
  assert.equal(f.api.init({ measurementId: "G-ANOTHER" }), false);
  vm.runInContext(source, f.context);
  assert.equal(f.scripts.length, 1);
  assert.equal(f.commands().filter((c) => c[0] === "config").length, 1);
  assert.equal(f.listeners.click.length, 1);
  assert.equal(f.listeners.auxclick.length, 1);
});

test("separately injected gtag is reused without duplicate loader or automatic page_view", () => {
  const f = fixture({ injected: true, configured: true });
  const existing = f.window.gtag;
  assert.equal(f.scripts.length, 1);
  assert.equal(f.api.init({ measurementId: ID }), true);
  assert.equal(f.window.gtag, existing);
  assert.equal(f.commands().find((c) => c[0] === "config")[2].send_page_view, false);
  f.dispatch(anchor());
  assert.equal(f.events().length, 1);
});

test("no invalid ID, opt-out override, production escape hatch or unsafe dataLayer", () => {
  const f = fixture({ auto: false });
  for (const id of ["", "G-ABC&x=1", "G-foo", "not-google", null]) assert.equal(f.api.init({ measurementId: id, debug: true, allowLocalhost: true }), false);
  f.window[`ga-disable-${ID}`] = true;
  assert.equal(f.api.init({ measurementId: ID }), false);
  assert.equal(f.scripts.length, 0);
  delete f.window[`ga-disable-${ID}`];
  f.window.dataLayer = {};
  assert.equal(f.api.init({ measurementId: ID }), false);
});

test("mouse, nested target, Enter-style and middle-click each emit exactly one download", () => {
  const f = fixture();
  const a = anchor({ "data-cta-location": "hero" });
  f.dispatch(new Element("span", {}, a));
  f.dispatch(a, "click", 0, { detail: 0 }); // Native Enter activation.
  f.dispatch(a, "click", 1); // Old middle-button click must not count twice.
  f.dispatch(a, "auxclick", 1);
  assert.equal(f.events().length, 3);
  assert.ok(f.events().every((c) => c[1] === "download_click"));
  assert.equal(eventParams(f).cta_location, "hero");
  assert.equal(eventParams(f).download_kind, "official_free");
});

test("right clicks, prevented activations, disabled links and non-links do not emit", () => {
  const f = fixture();
  f.dispatch(anchor(), "auxclick", 2);
  f.dispatch(anchor(), "click", 2);
  f.dispatch(anchor(), "click", 0, { defaultPrevented: true });
  f.dispatch(anchor({ "aria-disabled": "true" }));
  f.dispatch(new Element("button", { "data-event": "download_click" }));
  f.dispatch(anchor({ href: "mailto:private@example.test" }));
  f.dispatch(anchor({ href: "javascript:void(0)" }));
  assert.equal(f.events().length, 0);
});

test("deduplication concerns the same DOM event, not legitimate repeated activations", () => {
  const f = fixture();
  const a = anchor();
  const event = f.dispatch(a);
  f.listeners.click[0](event);
  assert.equal(f.events().length, 1);
  f.dispatch(a);
  assert.equal(f.events().length, 2);
});

test("BOOTH and affiliate metrics preserve established names and bounded categories", () => {
  const f = fixture();
  f.dispatch(anchor({ href: "https://maipilot.booth.pm", "data-event": "booth_click", "data-cta-location": "booth" }));
  assert.equal(f.events()[0][1], "booth_click");
  assert.equal(eventParams(f).download_kind, "booth_support");
  f.dispatch(anchor({ href: "https://affiliate.example/path?private=secret", "data-event": "pr_conoha_click", rel: "noopener sponsored", "data-affiliate": "true" }));
  assert.deepEqual(f.events().map((c) => c[1]), ["booth_click", "pr_conoha_click", "outbound_affiliate_click"]);
  assert.equal(eventParams(f).affiliate_partner, "conoha");
  f.dispatch(anchor({ href: "https://affiliate.example/", "data-event": "outbound_affiliate_click", rel: "sponsored" }));
  assert.equal(f.events().length, 4);
});

test("docs homepage CTA is a journey event, never an actual download", () => {
  const f = fixture({ url: "https://www.maipilot.jp/docs/growth/forge-fabric-mod-installation/?from=private#heading" });
  f.dispatch(anchor({ href: "/?from=docs#download", "data-event": "download_click", class: "cta-btn" }));
  assert.equal(f.events()[0][1], "docs_to_download");
  assert.equal(eventParams(f).journey_stage, "docs_to_download");
  assert.equal(eventParams(f).cta_location, "docs_cta");
  f.dispatch(anchor({ href: "/?from=docs#download", "data-event": "nav_download_click", class: "nav-cta" }));
  assert.equal(eventParams(f).cta_location, "docs_nav");
  f.dispatch(anchor({ href: "/", "data-event": "", class: "cta-btn" }));
  assert.equal(f.events().at(-1)[1], "docs_to_download");
  f.dispatch(anchor());
  assert.equal(f.events().at(-1)[1], "download_click");
  assert.equal(eventParams(f).journey_origin, "docs");
});

test("landing download preserves a docs origin without carrying a raw referrer", () => {
  const f = fixture({ referrer: "https://maipilot.jp/docs/java-setup/?email=secret@example.com#private" });
  f.dispatch(anchor());
  assert.equal(eventParams(f).journey_origin, "docs");
  assert.equal(eventParams(f).page_referrer, "https://maipilot.jp/docs/java-setup/");
});

test("event and pageview payloads exclude arbitrary DOM text, URLs, email, queries and hashes", () => {
  const f = fixture({ url: "https://www.maipilot.jp/?email=secret@example.test#secret", referrer: "https://search.example/?q=secret#secret" });
  const a = anchor({ href: `${INSTALLER}?secret`, "data-cta-location": "private@example.test", "data-source": "secret", "data-label": "secret" });
  a.textContent = "secret@example.test";
  f.dispatch(a);
  f.api.track("download_click", { cta_location: "email@example.test", link_url: "secret", link_text: "secret", email: "secret", page_path: "/secret/", ui_language: "secret", platform_class: "secret", download_kind: "secret", affiliate_partner: "secret" });
  const serialized = JSON.stringify(f.commands());
  assert.ok(!serialized.includes("secret"));
  assert.ok(!serialized.includes("email@"));
  assert.ok(!serialized.includes("link_url"));
  assert.equal(eventParams(f).page_path, "/");
  assert.equal(eventParams(f).platform_class, "windows");
  assert.equal(eventParams(f).ui_language, "ja");
  assert.equal(eventParams(f).cta_location, "other");
  assert.equal(eventParams(f).send_to, ID);
  assert.equal(f.api.track("arbitrary_private_event", { email: "secret" }), false);
});

test("safe route list canonicalizes docs aliases and rejects unknown identifying paths", () => {
  const f = fixture();
  assert.equal(f.api.pagePath("/LPdocs/java-setup/index.html?secret#x"), "/docs/java-setup/");
  assert.equal(f.api.pagePath("/lpdocs"), "/docs/");
  assert.equal(f.api.pagePath("/docs/java-setup"), "/docs/java-setup/");
  for (const path of ["/user/private@example.com", "/docs/private-id", "//malicious.example/path", "/%70rivate"]) assert.equal(f.api.pagePath(path), "/other/");
  for (const file of walkHtmlFiles(resolve(root, "public/docs"))) {
    const path = `/docs/${relative(resolve(root, "public/docs"), file).replace(/index\.html$/, "")}`;
    assert.equal(f.api.pagePath(path), path);
  }
});

test("platform and displayed-language dimensions have a closed vocabulary", () => {
  const f = fixture();
  const cases = [
    [{ userAgent: "Windows NT 10", platform: "Win32" }, "windows"],
    [{ userAgent: "Mozilla iPhone" }, "mobile"], [{ userAgent: "Android" }, "mobile"],
    [{ userAgent: "Windows Phone" }, "mobile"], [{ platform: "MacIntel", maxTouchPoints: 5 }, "mobile"],
    [{ userAgentData: { mobile: true, platform: "Windows" } }, "mobile"],
    [{ userAgentData: { mobile: false, platform: "Windows" } }, "windows"],
    [{ userAgent: "Mac OS X" }, "other"], [{ userAgent: "Linux" }, "other"], [{}, "unknown"], [null, "unknown"]
  ];
  for (const [input, expected] of cases) assert.equal(f.api.platformClass(input), expected);
  f.document.documentElement.lang = "en-US";
  f.dispatch(anchor());
  assert.equal(eventParams(f).ui_language, "en");
  f.document.documentElement.lang = "private@example.test";
  f.dispatch(anchor());
  assert.equal(eventParams(f).ui_language, "unknown");
});

test("retired handoff events and legacy query markers are not collected", () => {
  const f = fixture({ url: "https://www.maipilot.jp/?from=mobile_handoff&email=secret@example.test#download" });
  for (const event of ["pc_handoff_open", "pc_link_copy", "pc_link_share"]) assert.equal(f.api.track(event, { cta_location: "hero" }), false);
  assert.equal(f.events().length, 0);
  f.dispatch(anchor());
  assert.equal(eventParams(f).download_kind, "official_free");
  assert.equal(eventParams(f).entry_point, undefined);
  assert.ok(!JSON.stringify(f.commands()).includes("mobile_handoff"));
  assert.ok(!JSON.stringify(f.commands()).includes("secret"));
});

test("tracking fails closed after opt-out, host changes, or gtag errors", () => {
  const f = fixture();
  f.window[`ga-disable-${ID}`] = true;
  assert.equal(f.api.track("download_click"), false);
  delete f.window[`ga-disable-${ID}`];
  f.window.location = new URL("https://preview.example/");
  assert.equal(f.api.track("download_click"), false);
  f.window.location = new URL("https://www.maipilot.jp/");
  f.window.gtag = () => { throw new Error("blocked"); };
  assert.equal(f.api.track("download_click"), false);
});

test("docs migration is idempotent, preserves JSON-LD and language UI, removes all legacy tracking", () => {
  for (const file of walkHtmlFiles(resolve(root, "public/docs"))) {
    const html = readFileSync(file, "utf8");
    assert.equal(transformDoc(html), html, file);
    assert.equal((html.match(/src="\/analytics\.js"/g) || []).length, 1, file);
    assert.ok(!/gtag|trackEvent|handleTrackedClick/.test(html), file);
    assert.ok(!/data-event="(?:nav_download_click|growth_download_click)"/.test(html), file);
    for (const match of html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)) {
      if (!match[1].includes("application/ld+json") && !match[1].includes("src=")) new vm.Script(match[2]);
    }
    if (html.includes("const applyLang")) {
      assert.ok(html.includes('document.documentElement.lang = lang === "en" ? "en" : "ja";'), file);
      assert.ok(html.includes('button.addEventListener("click", () => applyLang'), file);
    }
  }
});

test("migration refuses unknown mixed UI/tracking scripts and invalid measurement IDs", () => {
  assert.throws(() => transformDoc('<head></head><script>doOtherWork(); trackEvent();</script>'), /Unrecognized/);
  assert.throws(() => transformDoc('<head></head>', 'bad"id'), /Invalid/);
});


test("migration preserves even pre-existing malformed JSON-LD verbatim for a separate SEO audit", () => {
  const structuredData = '<script type="application/ld+json">{"name":"trackEvent", "preexisting": invalid}</script>';
  const html = `<html>\n  <head>\n${structuredData}\n  </head>\n</html>`;
  assert.ok(transformDoc(html).includes(structuredData));
});

test("known route titles are retained without trusting arbitrary live DOM titles", () => {
  const docs = fixture({ url: "https://www.maipilot.jp/docs/java-setup/" });
  const config = docs.commands().find((c) => c[0] === "config")[2];
  assert.notEqual(config.page_title, "MaiPilot");
  assert.ok(config.page_title.includes("Java"));
  docs.document.title = "private@example.test";
  docs.dispatch(anchor());
  assert.equal(eventParams(docs).page_title, config.page_title);
  const home = fixture({ lang: "en" });
  assert.equal(home.commands().find((c) => c[0] === "config")[2].page_title, "MaiPilot | All-in-one Minecraft server manager for Windows");
  assert.equal("ui_language" in home.commands().find((c) => c[0] === "config")[2], false);
  home.dispatch(anchor());
  assert.equal(eventParams(home).ui_language, "en");
});

test("known UTM source and medium are preserved explicitly; freeform campaign data is dropped", () => {
  const f = fixture({ url: "https://www.maipilot.jp/?utm_source=google&utm_medium=cpc&utm_campaign=secret&utm_term=secret&utm_content=secret&gclid=secret" });
  const config = f.commands().find((c) => c[0] === "config")[2];
  assert.equal(config.campaign_source, "google");
  assert.equal(config.campaign_medium, "cpc");
  f.dispatch(anchor());
  assert.equal(eventParams(f).campaign_source, "google");
  assert.equal(eventParams(f).campaign_medium, "cpc");
  assert.ok(!JSON.stringify(f.commands()).includes("secret"));
  const unknown = fixture({ url: "https://www.maipilot.jp/?utm_source=secret&utm_medium=secret" });
  const unknownConfig = unknown.commands().find((c) => c[0] === "config")[2];
  assert.equal("campaign_source" in unknownConfig, false);
  assert.equal("campaign_medium" in unknownConfig, false);
  assert.ok(!JSON.stringify(unknown.commands()).includes("secret"));
});


test("in-article fragments and relative docs links are never download journeys", () => {
  const f = fixture({ url: "https://www.maipilot.jp/docs/growth/haichi-map-installation/" });
  f.dispatch(anchor({ href: "#maipilot-map", "data-event": "docs_to_download", class: "cta-btn" }));
  assert.equal(f.events().length, 0);
  const html = `<html>
  <head>
<link rel="canonical" href="https://www.maipilot.jp/docs/growth/haichi-map-installation/">
  </head>
<body><a class="cta-btn" href="#maipilot-map" data-event="docs_to_download">Steps</a><a class="cta-btn" href="../backup-restore-guide/">Backups</a><a class="cta-btn" href="/#download">Download</a></body></html>`;
  const next = transformDoc(html);
  assert.match(next, /href="#maipilot-map">Steps/);
  assert.match(next, /href="\.\.\/backup-restore-guide\/">Backups/);
  assert.match(next, /href="\/#download" data-event="docs_to_download">Download/);
  assert.equal(transformDoc(next), next);
});
