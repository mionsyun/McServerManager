/* Shared landing/docs analytics. No network or event commands outside production.
 * Embed once: <script defer src="/analytics.js" data-measurement-id="G-..."></script>
 * Browser tests must mock production URLs + the Google script, never add a bypass.
 */
(function (win, doc) {
  "use strict";
  if (win.MaiPilotAnalytics) return;

  var EVENTS = new Set([
    "download_click", "booth_click", "docs_to_download", "docs_click",
    "download_section_click", "setup_guide_click",
    "docs_24_7_hosting_click", "nav_download_click", "growth_download_click",
    "growth_home_banner_click", "growth_lp_banner_click", "growth_docs_click",
    "growth_article_click", "docs_home_banner_click", "docs_lp_banner_click",
    "pr_conoha_click", "pr_xserver_click", "pr_shinvps_click",
    "outbound_affiliate_click"
  ]);
  var PLACEMENTS = new Set([
    "nav", "header", "hero", "features", "help", "how", "compat", "security",
    "download", "options", "booth", "support", "sponsors", "resources", "faq", "footer",
    "docs_nav", "docs_cta", "docs_body", "other"
  ]);
  // A closed route list prevents accidental user identifiers in page paths.
  // Keep in sync with docs; the generator updates this marked block.
  // BEGIN DOCUMENT PATHS
  var DOCUMENT_TITLES = {
    "/docs/24-7-hosting/": "MaiPilot | マイクラ サーバー 24時間稼働 VPS（PR）",
    "/docs/growth/backup-restore-guide/": "マイクラサーバーのバックアップと復元手順（事故防止） | MaiPilot",
    "/docs/growth/forge-fabric-mod-installation/": "マイクラ MOD 入れ方・導入ガイド（Forge / Fabric）Windows版 | MaiPilot",
    "/docs/growth/forge-fabric-server-setup/": "Forge/Fabricサーバーの立て方（初心者向け） | MaiPilot",
    "/docs/growth/friends-cant-join-error-guide/": "友だちが入れない時のエラー別対処（timeout/refused） | MaiPilot",
    "/docs/growth/haichi-map-installation/": "Planet Minecraftの配布マップの入れ方｜ダウンロード・サーバー導入 | MaiPilot",
    "/docs/growth/": "MaiPilot | マイクラサーバー立て方・ポート開放ガイド",
    "/docs/growth/minecraft-server-datapack-install/": "マイクラサーバーにデータパックを入れる方法【2026年版】 | MaiPilot",
    "/docs/growth/minecraft-server-gamerule-settings/": "マイクラサーバーのgamerule（ゲームルール）設定完全ガイド【2026年版】 | MaiPilot",
    "/docs/growth/minecraft-server-lag-fix/": "マイクラサーバーが重い・ラグの原因と対処法 | MaiPilot",
    "/docs/growth/minecraft-server-memory-settings/": "マイクラサーバーのメモリ設定（Xms/Xmx目安） | MaiPilot",
    "/docs/growth/minecraft-server-player-management/": "マイクラサーバーのプレイヤー管理完全ガイド｜BAN・キック・OP権限【2026年版】 | MaiPilot",
    "/docs/growth/minecraft-server-recommended-specs/": "マイクラサーバーの必要スペック｜メモリは何GB？ CPU・容量の確認 | MaiPilot",
    "/docs/growth/minecraft-server-setup-maipilot/": "【2026年最新】マイクラサーバーの立て方｜日本語ツールで簡単3ステップ | MaiPilot",
    "/docs/growth/minecraft-server-tatekkata-2026/": "【2026年最新】マイクラサーバーの立て方｜自宅PCで無料・簡単3ステップ | MaiPilot",
    "/docs/growth/paper-plugin-quickstart/": "Paperサーバーにプラグインを入れる最短手順 | MaiPilot",
    "/docs/growth/paper-spigot-plugin-installation/": "マイクラ プラグイン入れ方ガイド（Paper / Spigot）Windows版 | MaiPilot",
    "/docs/growth/port-kaihou-dekinai/": "マイクラのポート開放できない時の対処法【2026年版・安全チェックリスト】 | MaiPilot",
    "/docs/growth/port-open-checklist/": "マイクラポート開放できない時のチェックリスト | MaiPilot",
    "/docs/growth/rental-vs-jitaku/": "マイクラサーバーはレンタルと自宅PCどっちがいい？コスト・手間・自由度を徹底比較 | MaiPilot",
    "/docs/growth/resource-pack-distribution/": "Minecraftサーバーにリソースパックを自動配布する方法 | MaiPilot",
    "/docs/growth/server-properties-recommended-settings/": "server.propertiesおすすめ設定（公開向け） | MaiPilot",
    "/docs/growth/server-type-comparison/": "Paper / Purpur / Vanilla の選び方比較 | MaiPilot",
    "/docs/growth/whitelist-op-guide/": "マイクラサーバーのホワイトリスト・OP設定ガイド | MaiPilot",
    "/docs/growth/windows-minecraft-server-2026/": "Windowsでマイクラサーバーの立て方（2026年版） | MaiPilot",
    "/docs/": "MaiPilot | マイクラサーバー立て方・セットアップノート",
    "/docs/java-setup/": "MaiPilot | Java導入ガイド - マイクラサーバーに必要なJavaの入れ方",
    "/docs/lan/": "MaiPilot | LAN手順",
    "/docs/port-forwarding/": "MaiPilot | マイクラポート開放チェックリスト",
    "/docs/privacy-and-network/": "MaiPilot | 通信と権限",
    "/docs/privacy-policy/": "MaiPilot | プライバシーポリシー",
    "/docs/terms/": "MaiPilot | 利用規約",
    "/docs/troubleshooting/": "MaiPilot | トラブルシュート"
  };
  var DOCUMENT_PATHS = new Set(Object.keys(DOCUMENT_TITLES));
  // END DOCUMENT PATHS
  var CAMPAIGN_SOURCES = new Set(["google", "bing", "yahoo", "duckduckgo", "youtube", "x", "twitter", "instagram", "facebook", "reddit", "discord", "booth", "newsletter", "maipilot"]);
  var CAMPAIGN_MEDIA = new Set(["organic", "cpc", "ppc", "paid_social", "social", "referral", "email", "display", "video", "banner", "affiliate"]);
  var measurementId = "";
  var started = false;
  var seen = new WeakSet();

  function isProductionLocation(location) {
    return Boolean(location && location.protocol === "https:" &&
      (location.hostname === "maipilot.jp" || location.hostname === "www.maipilot.jp") &&
      (!location.port || location.port === "443"));
  }

  function pagePath(pathname) {
    var path = String(pathname || "/").split(/[?#]/, 1)[0];
    if (path === "/" || path === "/index.html") return "/";
    path = path.replace(/^\/(?:LPdocs|lpdocs)(?=\/|$)/, "/docs")
      .replace(/\/index\.html$/, "/");
    if (!path.endsWith("/")) path += "/";
    return DOCUMENT_PATHS.has(path) ? path : "/other/";
  }

  function platformClass(navigator) {
    if (!navigator) return "unknown";
    var ua = typeof navigator.userAgent === "string" ? navigator.userAgent : "";
    var platform = navigator.userAgentData && navigator.userAgentData.platform || navigator.platform || "";
    if (navigator.userAgentData && navigator.userAgentData.mobile ||
        /Android|iPhone|iPad|iPod|Mobile|Windows Phone/i.test(ua) ||
        /Mac/i.test(platform) && navigator.maxTouchPoints > 1) return "mobile";
    if (/Windows/i.test(ua) || /^Win/i.test(platform)) return "windows";
    return ua || platform ? "other" : "unknown";
  }

  function language() {
    // Read the displayed UI, never a raw browser locale or DOM label.
    var value = String(doc.documentElement.lang || "").toLowerCase();
    return /^(ja|en)(-|$)/.test(value) ? value.slice(0, 2) : "unknown";
  }

  function safeReferrer() {
    try {
      var url = new URL(doc.referrer);
      if (!/^https?:$/.test(url.protocol) || url.username || url.password) return "";
      // Retain source origin for acquisition, but no search terms or fragments.
      if (url.hostname === "maipilot.jp" || url.hostname === "www.maipilot.jp") {
        return url.origin + pagePath(url.pathname);
      }
      return url.origin + "/";
    } catch (_) { return ""; }
  }

  function campaignParams() {
    var query = new URL(win.location.href).searchParams;
    var params = {};
    // Exact known tokens only. Never copy campaign names, IDs, terms or content.
    if (CAMPAIGN_SOURCES.has(query.get("utm_source"))) params.campaign_source = query.get("utm_source");
    if (CAMPAIGN_MEDIA.has(query.get("utm_medium"))) params.campaign_medium = query.get("utm_medium");
    return params;
  }

  function pageTitle(path) {
    if (path === "/") return language() === "en" ? "MaiPilot | All-in-one Minecraft server manager for Windows" : "MaiPilot | Windows向けマイクラサーバー統合管理";
    return DOCUMENT_TITLES[path] || "MaiPilot";
  }

  function baseParams() {
    var path = pagePath(win.location.pathname);
    var referralPath = "";
    try {
      var referrer = new URL(doc.referrer);
      if (referrer.hostname === "maipilot.jp" || referrer.hostname === "www.maipilot.jp") {
        referralPath = pagePath(referrer.pathname);
      }
    } catch (_) { /* An empty referrer is normal. */ }
    return Object.assign({
      page_path: path,
      page_title: pageTitle(path),
      page_location: win.location.origin + path,
      page_referrer: safeReferrer(),
      ui_language: language(),
      platform_class: platformClass(win.navigator),
      journey_origin: path.startsWith("/docs/") || referralPath.startsWith("/docs/") ? "docs" : "landing"
    }, campaignParams());
  }

  function safeParams(name, input) {
    var params = baseParams();
    input = input || {};
    params.cta_location = PLACEMENTS.has(input.cta_location) ? input.cta_location : "other";
    params.event_category = name === "outbound_affiliate_click" || name.startsWith("pr_") ? "affiliate" : "cta";
    params.journey_stage = name === "download_click" ? "download" : name === "booth_click" ? "support" :
      name === "docs_to_download" ? "docs_to_download" :
      params.event_category === "affiliate" ? "affiliate" : "navigation";
    if (name === "download_click" || name === "docs_to_download") params.download_kind = "official_free";
    if (name === "booth_click") params.download_kind = "booth_support";
    if (["conoha", "xserver", "shinvps"].includes(input.affiliate_partner)) {
      params.affiliate_partner = input.affiliate_partner;
    }
    // Explicit routing prevents an unrelated injected GA property receiving events.
    params.send_to = measurementId;
    return params;
  }

  function track(name, input) {
    if (!started || !isProductionLocation(win.location) || win["ga-disable-" + measurementId] === true || !EVENTS.has(name)) return false;
    try {
      win.gtag("event", name, safeParams(name, input));
      return true;
    } catch (_) { return false; } // Analytics must never break a CTA.
  }

  function placement(anchor) {
    var marked = anchor.closest("[data-cta-location]");
    var explicit = marked && marked.getAttribute("data-cta-location");
    if (PLACEMENTS.has(explicit)) return explicit;
    if (pagePath(win.location.pathname).startsWith("/docs/")) {
      if (anchor.closest("nav, .nav, .nav-cta")) return "docs_nav";
      if (anchor.closest(".cta, .cta-btn, .booth-cta")) return "docs_cta";
      return "docs_body";
    }
    if (anchor.closest("nav")) return "nav";
    if (anchor.closest("footer")) return "footer";
    if (anchor.closest(".hero")) return "hero";
    var section = anchor.closest("section[id]");
    return section && PLACEMENTS.has(section.id) ? section.id : "other";
  }

  function linkEvent(anchor) {
    var url;
    try { url = new URL(anchor.getAttribute("href"), win.location.href); } catch (_) { return ""; }
    if (!/^https?:$/.test(url.protocol)) return "";
    var declared = anchor.getAttribute("data-event") || "";
    var sameSite = url.origin === win.location.origin ||
      url.hostname === "maipilot.jp" || url.hostname === "www.maipilot.jp";
    var isHome = sameSite && pagePath(url.pathname) === "/";
    var fromDocs = pagePath(win.location.pathname).startsWith("/docs/");
    // A docs CTA leading to the landing page is not an installer download.
    if (fromDocs && isHome && (["download_click", "nav_download_click", "growth_download_click", "docs_to_download"].includes(declared) ||
        anchor.matches(".cta-btn, .nav-cta") || url.hash === "#download")) return "docs_to_download";
    if (declared === "docs_to_download") return fromDocs && isHome ? declared : "";
    if (declared === "download_click") {
      // Only a real download destination, never an in-page anchor or homepage.
      return !isHome && /\.(exe|msi|zip)$/i.test(url.pathname) ? declared : "";
    }
    if (declared === "booth_click") return url.hostname === "maipilot.booth.pm" ? declared : "";
    if (EVENTS.has(declared)) return declared;
    if (url.hostname === "maipilot.booth.pm") return "booth_click";
    if (url.hostname === "stmaipilot.blob.core.windows.net" && url.pathname === "/public/downloads/MaiPilotSetup.exe") return "download_click";
    if (sameSite && pagePath(url.pathname) === "/docs/24-7-hosting/") return "docs_24_7_hosting_click";
    if (sameSite && pagePath(url.pathname).startsWith("/docs/")) return "docs_click";
    return "";
  }

  function handleActivation(event) {
    if (event.defaultPrevented || seen.has(event)) return;
    if (event.type === "click" && event.button !== 0 || event.type === "auxclick" && event.button !== 1) return;
    var target = event.target && (event.target.nodeType === 3 ? event.target.parentElement : event.target);
    var anchor = target && typeof target.closest === "function" && target.closest("a[href]");
    if (!anchor || anchor.getAttribute("aria-disabled") === "true") return;
    seen.add(event);
    var name = linkEvent(anchor);
    var partner = (anchor.getAttribute("data-event") || "").match(/^pr_(conoha|xserver|shinvps)_click$/);
    var params = { cta_location: placement(anchor), affiliate_partner: partner ? partner[1] : undefined };
    if (name) track(name, params);
    var rel = (anchor.getAttribute("rel") || "").toLowerCase().split(/\s+/);
    if ((rel.includes("sponsored") || anchor.getAttribute("data-affiliate") === "true") && name !== "outbound_affiliate_click") {
      track("outbound_affiliate_click", params);
    }
  }

  function init(config) {
    config = config || {};
    var id = typeof config.measurementId === "string" ? config.measurementId.trim() : "";
    if (!/^G-[A-Z0-9]+$/.test(id)) return false;
    if (!isProductionLocation(win.location)) {
      // Also stop this property if a separate script already injected gtag.
      win["ga-disable-" + id] = true;
      return false;
    }
    if (started) return measurementId === id;
    if (win["ga-disable-" + id] === true) return false;
    measurementId = id;
    if (win.dataLayer && !Array.isArray(win.dataLayer)) return false;
    win.dataLayer = win.dataLayer || [];
    var configured = win.dataLayer.some(function (entry) { return entry && entry[0] === "config" && entry[1] === id; });
    var bootstrapped = win.dataLayer.some(function (entry) { return entry && entry[0] === "js"; });
    if (typeof win.gtag !== "function") win.gtag = function () { win.dataLayer.push(arguments); };
    var base = baseParams();
    if (!bootstrapped) win.gtag("js", new Date());
    var gaConfig = Object.assign({
      send_page_view: !configured,
      page_path: base.page_path,
      page_location: base.page_location,
      page_referrer: base.page_referrer,
      page_title: base.page_title,
      platform_class: base.platform_class,
      allow_google_signals: false,
      allow_ad_personalization_signals: false
    }, campaignParams());
    // Nuxt may still hydrate the landing UI; avoid claiming an initial language.
    if (base.page_path.startsWith("/docs/")) gaConfig.ui_language = base.ui_language;
    win.gtag("config", id, gaConfig);
    var existing = Array.from(doc.scripts).some(function (script) {
      try {
        var url = new URL(script.src, win.location.href);
        return url.hostname === "www.googletagmanager.com" && url.pathname === "/gtag/js";
      } catch (_) { return false; }
    });
    if (!existing) {
      var script = doc.createElement("script");
      script.async = true;
      script.src = "https://www.googletagmanager.com/gtag/js?id=" + encodeURIComponent(id);
      script.id = "maipilot-google-tag";
      doc.head.appendChild(script);
    }
    started = true;
    // Native anchors emit click for Enter. No keydown tracker, which would double count.
    doc.addEventListener("click", handleActivation, { passive: true });
    doc.addEventListener("auxclick", handleActivation, { passive: true });
    return true;
  }

  win.MaiPilotAnalytics = Object.freeze({
    init: init,
    track: track,
    // Pure, bounded helpers are public so tests need no production debug switch.
    isProductionLocation: isProductionLocation,
    pagePath: pagePath,
    platformClass: platformClass
  });
  var tag = doc.currentScript;
  if (tag && tag.getAttribute("data-measurement-id")) init({ measurementId: tag.getAttribute("data-measurement-id") });
})(window, document);
