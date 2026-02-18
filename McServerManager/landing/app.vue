<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from "vue";

const downloadUrl = "/downloads/BlockPilotSetup.exe";
const docsUrl = "/docs";
const docsLanUrl = "/docs/lan";
const docsHostingUrl = "/docs/24-7-hosting";
const docsPortUrl = "/docs/port-forwarding";
const docsTroubleUrl = "/docs/troubleshooting";
const docsPrivacyUrl = "/docs/privacy-and-network";
const version = "1.0.0";

const downloadOptionLinks: Record<string, string> = {
  local: downloadUrl,
  lan: docsLanUrl,
  hosting: docsHostingUrl,
};

const downloadOptionEvents: Record<string, string> = {
  local: "download_click",
  hosting: "docs_24_7_hosting_click",
};

const faqLinks: Record<string, string> = {
  hosting: docsHostingUrl,
  port: docsPortUrl,
  trouble: docsTroubleUrl,
};

const translations = {
  en: {
    metaTitle: "BlockPilot | Gamer-first Minecraft server control",
    metaDescription:
      "A Windows GUI to create, run, and recover Minecraft servers with clear setup, local-only data, and transparent network checks.",
    siteName: "BlockPilot",
    nav: {
      features: "Features",
      help: "Guided Setup",
      how: "How It Works",
      compat: "Java Compatibility",
      security: "Security",
      download: "Download",
    },
    langLabel: "Language",
    langJa: "日本語",
    langEn: "English",
    eyebrow: "Guided hosting for first-time and returning players",
    heroTitle: "Launch with confidence, even on day one.",
    heroSub:
      "BlockPilot puts setup, checks, and recovery in one place so you can focus on your world.",
    heroNotes: [
      "A GUI to create, run, and recover Minecraft servers on Windows.",
      "Data stays on your PC. Only minimal required network calls. Admin actions are clearly shown.",
    ],
    heroAffiliateLabel: "Need always-on hosting?",
    heroAffiliateCta: "See hosting options (PR)",
    heroCtaPrimary: "Download",
    heroCtaSecondary: "Read the setup guide",
    heroMetaVersion: "Version",
    heroMetaWindows: "Windows 10/11",
    heroMetaRuntime: "Self-contained runtime",
    cardTitle: "Server Control Deck",
    cardStatusLabel: "Status",
    cardStatusValue: "Online",
    cardPlayersLabel: "Players",
    cardJavaLabel: "Java",
    cardCpu: "CPU",
    cardMemory: "Memory",
    cardStart: "Start",
    cardBackup: "Backup",
    cardFirewall: "Firewall",
    featuresTitle: "Fast start. Clear control.",
    featuresSub: "The safety-critical parts are shown first.",
    features: [
      {
        title: "Crash recovery path",
        body: "Open logs and crash reports with one click when things go wrong.",
      },
      {
        title: "Safe apply flow",
        body: "Changes are saved, but applied after restart to prevent mistakes.",
      },
      {
        title: "Backup & restore built-in",
        body: "World backups and restores are standard, not add-ons.",
      },
      {
        title: "Network guidance",
        body: "Checklist, IP lookup, firewall, and UPnP help in one tab.",
      },
    ],
    featuresAffiliateLabel: "Always-on hosting is an option if your PC cannot stay on.",
    featuresAffiliateCta: "Compare hosting options (PR)",
    stepsTitle: "3 steps, no noise",
    steps: [
      {
        title: "Create a server profile (EULA required).",
        body: "Name, location, server type, and version.",
      },
      {
        title: "Set only what matters.",
        body: "Memory, port, and Java path with clear explanations.",
      },
      {
        title: "Start with checks.",
        body: "Java compatibility and network checks show what to fix first.",
      },
    ],
    previewTitle: "Live Log",
    previewStatus: "Running",
    previewLines: [
      "[12:40:01] Starting server...",
      "[12:40:05] Preparing spawn area: 64%",
      '[12:40:07] Done (5.8s)! For help, type "help"',
    ],
    helpTitle: "Guided setup that prevents surprises",
    helpSub: "Clear checks and next steps before you press Start.",
    helpItems: [
      {
        title: "Java mismatch",
        body: "Warns you before launch and tells you the required Java version.",
      },
      {
        title: "Port/Network confusion",
        body: "Guided checklist, public IP lookup, and port test in one tab.",
      },
      {
        title: "Crash recovery",
        body: "One-click access to logs and crash reports with tips to fix fast.",
      },
    ],
    helpCta: "Open setup notes",
    compatTitle: "Java compatibility",
    compatSub: "Required Java is checked before launch.",
    compat: [
      { title: "1.20.5 and newer", badge: "Java 21+" },
      { title: "1.18 - 1.20.4", badge: "Java 17+" },
      { title: "1.17", badge: "Java 16+" },
      { title: "1.16 and older", badge: "Java 8+" },
    ],
    compatNotes: [
      "We warn before launch if Java is too old for the selected version.",
      "Recommended Java depends on the Minecraft version you choose.",
      "If a start fails, open logs and crash reports for quick recovery.",
    ],
    securityTitle: "Security & Privacy",
    securitySub: "Concrete, transparent, and local-first.",
    securityItems: [
      {
        title: "Local-only storage",
        body: "Server data is stored on your PC and never auto-uploaded to the cloud.",
      },
      {
        title: "Minimal network calls",
        body: "External communication is limited to version checks and public IP lookup.",
      },
      {
        title: "Published endpoints",
        body: "We publish the network endpoints and their purposes in Docs.",
      },
      {
        title: "Explicit admin prompts",
        body: "Firewall/UPnP actions are announced and use UAC for elevation.",
      },
      {
        title: "Local logs only",
        body: "Logs and crash reports are stored and viewed locally.",
      },
    ],
    securityCta: "Open privacy & network notes",
    downloadTitle: "Download and play tonight",
    downloadSub: "Installer for Windows. No runtime setup required.",
    downloadOptionsTitle: "Choose the right path",
    downloadOptionsSub: "We show options based on your situation.",
    downloadOptions: [
      {
        id: "local",
        title: "Run locally on this PC",
        body: "Use the desktop app with local worlds and fast backups.",
        cta: "Download",
      },
      {
        id: "lan",
        title: "LAN only on the same Wi-Fi",
        body: "Keep it local for friends on your home network.",
        cta: "LAN setup guide",
      },
      {
        id: "hosting",
        title: "24/7 hosting or public access (PR)",
        body: "If your PC cannot stay on, hosted servers can help.",
        cta: "See options (PR)",
      },
    ],
    downloadPrimary: "Download for Windows",
    downloadSecondary: "Setup notes",
    faqTitle: "FAQ",
    faqSub: "Quick answers for a smooth start.",
    faq: [
      {
        title: "I cannot keep my PC on 24/7.",
        body: "If you need always-on hosting, see the options.",
        linkId: "hosting",
        linkLabel: "View hosting options (PR)",
      },
      {
        title: "External access is not working.",
        body: "Follow the checklist for ports and routers.",
        linkId: "port",
        linkLabel: "Open port-forwarding guide",
      },
      {
        title: "It crashed and I do not know where logs are.",
        body: "Open logs/crash reports from the Console tab or follow the guide.",
        linkId: "trouble",
        linkLabel: "Open troubleshooting",
      },
      {
        title: "Does it work with mods?",
        body: "Vanilla is the main focus. Modded servers may need extra steps.",
      },
      {
        title: "Where are server files stored?",
        body: "By default in your AppData folder. Custom locations are supported.",
      },
      {
        title: "Is it free?",
        body: "Free for personal, non-commercial use. See license details.",
      },
      {
        title: "How do I update?",
        body: "Download and run the latest installer.",
      },
    ],
    footerDocs: "Docs",
    footerNotices: "Third-party notices",
    footerLicense: "License",
    footerRight: "Built for local worlds.",
    footerDisclosure: "This page includes affiliate links (PR).",
  },
  ja: {
    metaTitle: "BlockPilot | ゲーマー向けマイクラサーバー管理",
    metaDescription:
      "WindowsでMinecraftサーバーを作成・起動・復旧するGUI。データはローカル保存、通信は最小限、権限は明示します。",
    siteName: "BlockPilot",
    nav: {
      features: "特長",
      help: "ガイド付きセットアップ",
      how: "使い方",
      compat: "Java互換",
      security: "安心設計",
      download: "ダウンロード",
    },
    langLabel: "言語",
    langJa: "日本語",
    langEn: "English",
    eyebrow: "ゲーマー向け Minecraft サーバー管理",
    heroTitle: "サクッと建てて、安定稼働。",
    heroSub:
      "Java・ポート・クラッシュのチェックまで、一画面でスマートに。",
    heroNotes: [
      "WindowsでMinecraftサーバーを、迷わず作って・動かして・戻せるGUI",
      "データはPC内。必要最小限の通信のみ。権限が必要な操作は明示します",
    ],
    heroAffiliateLabel: "24時間運用の選択肢もあります",
    heroAffiliateCta: "VPSの比較を見る（PR）",
    heroCtaPrimary: "ダウンロード",
    heroCtaSecondary: "セットアップガイド",
    heroMetaVersion: "バージョン",
    heroMetaWindows: "Windows 10/11 対応",
    heroMetaRuntime: "ランタイム同梱",
    cardTitle: "サーバー操作デッキ",
    cardStatusLabel: "状態",
    cardStatusValue: "稼働中",
    cardPlayersLabel: "プレイヤー",
    cardJavaLabel: "Java",
    cardCpu: "CPU",
    cardMemory: "メモリ",
    cardStart: "開始",
    cardBackup: "バックアップ",
    cardFirewall: "ファイアウォール",
    featuresTitle: "起動が速い。操作が速い。",
    featuresSub: "安心に効く体験を先に見せます。",
    features: [
      {
        title: "復旧導線が明確",
        body: "クラッシュ時はログ/クラッシュレポートへワンクリック。",
      },
      {
        title: "安全な反映フロー",
        body: "変更は保存できるが反映は再起動後。事故を防ぎます。",
      },
      {
        title: "バックアップ/復元が標準",
        body: "ワールドのバックアップ/復元を標準で搭載。",
      },
      {
        title: "ネットワーク支援",
        body: "公開チェック/IP表示/Firewall/UPnPのガイド。",
      },
    ],
    featuresAffiliateLabel: "PCをつけっぱなしにできない場合は、VPSという選択肢もあります。",
    featuresAffiliateCta: "24時間運用の比較を見る（PR）",
    stepsTitle: "起動まで、たった 3 ステップ",
    steps: [
      {
        title: "サーバープロファイル作成（EULA同意必須）",
        body: "名前/保存先/種別/バージョンを指定。",
      },
      {
        title: "必要な設定だけ",
        body: "メモリ/ポート/Javaパスを説明しながら設定。",
      },
      {
        title: "起動前チェック",
        body: "Java互換/公開チェックで不安を減らす。",
      },
    ],
    previewTitle: "ライブログ",
    previewStatus: "稼働中",
    previewLines: [
      "[12:40:01] サーバー起動中...",
      "[12:40:05] スポーン準備中: 64%",
      "[12:40:07] 完了 (5.8秒)! help と入力でヘルプ表示",
    ],
    helpTitle: "ガイド付きでスムーズに起動",
    helpSub: "起動前のチェックと次の一手を見える化。",
    helpItems: [
      {
        title: "Java のバージョン違い",
        body: "起動前に必要バージョンを警告します。",
      },
      {
        title: "ポート/ネットワークの迷子",
        body: "公開チェック・IP表示・ガイドを一画面に集約。",
      },
      {
        title: "クラッシュ復旧",
        body: "ログ/クラッシュレポートへ即アクセスできます。",
      },
    ],
    helpCta: "セットアップノートを見る",
    compatTitle: "Java 互換表",
    compatSub: "起動前に必要 Java を判定します。",
    compat: [
      { title: "1.20.5 以降", badge: "Java 21+" },
      { title: "1.18 - 1.20.4", badge: "Java 17+" },
      { title: "1.17", badge: "Java 16+" },
      { title: "1.16 以下", badge: "Java 8+" },
    ],
    compatNotes: [
      "必要Javaに足りない場合は起動前に警告します。",
      "推奨Javaはバージョンによって異なります。",
      "失敗時はログ/クラッシュレポートで復旧します。",
    ],
    securityTitle: "セキュリティ/プライバシー",
    securitySub: "曖昧にせず、具体的に。",
    securityItems: [
      {
        title: "ローカル保存",
        body: "サーバーデータは端末内に保存され、クラウドへ自動送信しません。",
      },
      {
        title: "必要最小限の通信",
        body: "外部通信はバージョン取得・公開IP取得など必要最小限です。",
      },
      {
        title: "通信先一覧を公開",
        body: "通信先一覧をDocsで公開しています。",
      },
      {
        title: "権限の明示",
        body: "Firewall/UPnPは実行前に明示し、UACで昇格します。",
      },
      {
        title: "ログはローカル",
        body: "ログ/クラッシュレポートはローカル表示・ローカル保存です。",
      },
    ],
    securityCta: "通信と権限の詳細を見る",
    downloadTitle: "今夜からプレイ可能",
    downloadSub: "Windows 用インストーラー。ランタイム設定不要。",
    downloadOptionsTitle: "状況別の選び方",
    downloadOptionsSub: "おすすめではなく、選択肢として提示します。",
    downloadOptions: [
      {
        id: "local",
        title: "このPCでローカル運用",
        body: "ローカルの世界をそのまま管理したい人向け。",
        cta: "ダウンロード",
      },
      {
        id: "lan",
        title: "同じWi-FiでLAN参加",
        body: "家の中だけで遊びたい人向け。",
        cta: "LAN手順を見る",
      },
      {
        id: "hosting",
        title: "24時間稼働・外部公開したい（PR）",
        body: "PCをつけっぱなしにできない場合の選択肢。",
        cta: "比較を見る（PR）",
      },
    ],
    downloadPrimary: "Windows 用を入手",
    downloadSecondary: "セットアップノート",
    faqTitle: "よくある質問",
    faqSub: "スムーズに始めるためのヒント。",
    faq: [
      {
        title: "PCをつけっぱなしにできません",
        body: "常時稼働が必要な場合はVPSの選択肢があります。",
        linkId: "hosting",
        linkLabel: "24時間運用の選択肢を見る（PR）",
      },
      {
        title: "外部公開ができません",
        body: "ポート開放チェックリストを確認してください。",
        linkId: "port",
        linkLabel: "ポート開放ガイドを見る",
      },
      {
        title: "クラッシュした/ログが分からない",
        body: "コンソールからログ/クラッシュレポートを開くか、ガイドを参照してください。",
        linkId: "trouble",
        linkLabel: "トラブルシュートを見る",
      },
      {
        title: "MOD は使えますか？",
        body: "基本はバニラ向けです。MOD は追加の手順が必要な場合があります。",
      },
      {
        title: "サーバーファイルはどこ？",
        body: "既定では AppData 配下。任意の場所にも変更できます。",
      },
      {
        title: "料金は？",
        body: "個人・非商用は無料です。詳細はライセンスをご確認ください。",
      },
      {
        title: "アップデート方法は？",
        body: "最新のインストーラーを入手して実行してください。",
      },
    ],
    footerDocs: "ドキュメント",
    footerNotices: "サードパーティ通知",
    footerLicense: "ライセンス",
    footerRight: "ローカル世界のために。",
    footerDisclosure: "本ページにはアフィリエイトリンク（PR）が含まれます。",
  },
} as const;

type Locale = keyof typeof translations;

const currentLang = ref<Locale>("en");
const t = computed(() => translations[currentLang.value]);
const runtimeConfig = useRuntimeConfig();
const siteUrl = computed(() => {
  const raw = runtimeConfig.public.siteUrl as string | undefined;
  if (!raw) {
    return "";
  }
  return raw.endsWith("/") ? raw.slice(0, -1) : raw;
});

type TrackPayload = {
  event_name: string;
  link_url?: string;
  link_text?: string;
  source?: string;
};

const trackEvent = (payload: TrackPayload) => {
  if (!process.client) {
    return;
  }
  const win = window as typeof window & {
    gtag?: (...args: unknown[]) => void;
    dataLayer?: unknown[];
  };
  if (typeof win.gtag === "function") {
    win.gtag("event", payload.event_name, {
      event_category: payload.source ?? "cta",
      event_label: payload.link_text ?? payload.link_url ?? "",
      link_url: payload.link_url,
    });
    return;
  }
  if (Array.isArray(win.dataLayer)) {
    win.dataLayer.push({
      event: payload.event_name,
      link_url: payload.link_url,
      link_text: payload.link_text,
      source: payload.source ?? "cta",
    });
  }
};

const handleTrackedClick = (event: MouseEvent) => {
  const target = event.target as HTMLElement | null;
  if (!target) {
    return;
  }
  const tracked = target.closest<HTMLElement>("[data-event]");
  const anchor = target.closest<HTMLAnchorElement>("a");
  const link = tracked ?? anchor;
  if (!link) {
    return;
  }

  const eventName = tracked?.getAttribute("data-event") ?? null;
  const href = anchor?.getAttribute("href") ?? link.getAttribute("href") ?? undefined;
  const text = link.textContent?.trim() ?? undefined;

  if (eventName) {
    trackEvent({
      event_name: eventName,
      link_url: href,
      link_text: text,
      source: "cta",
    });
  }

  if (!eventName && href?.includes("/docs/24-7-hosting")) {
    trackEvent({
      event_name: "docs_24_7_hosting_click",
      link_url: href,
      link_text: text,
      source: "cta",
    });
  }

  if (anchor) {
    const rel = (anchor.getAttribute("rel") ?? "").toLowerCase();
    const isSponsored = rel.includes("sponsored");
    const isAffiliate = isSponsored || anchor.dataset.affiliate === "true";
    if (isAffiliate && eventName !== "outbound_affiliate_click") {
      trackEvent({
        event_name: "outbound_affiliate_click",
        link_url: href,
        link_text: text,
        source: "affiliate",
      });
    }
  }
};

const setLang = (lang: Locale) => {
  currentLang.value = lang;
  if (process.client) {
    localStorage.setItem("mcsm-lang", lang);
  }
};

onMounted(() => {
  if (!process.client) {
    return;
  }
  document.addEventListener("click", handleTrackedClick, { passive: true });
  const saved = localStorage.getItem("mcsm-lang") as Locale | null;
  if (saved && translations[saved]) {
    currentLang.value = saved;
    return;
  }
  const browserLang = navigator.language?.toLowerCase() ?? "";
  if (browserLang.startsWith("ja")) {
    currentLang.value = "ja";
  }
});

onBeforeUnmount(() => {
  if (!process.client) {
    return;
  }
  document.removeEventListener("click", handleTrackedClick);
});

useHead(() => ({
  title: t.value.metaTitle,
  htmlAttrs: { lang: currentLang.value },
  meta: [
    { name: "description", content: t.value.metaDescription },
    { name: "keywords", content: "Minecraft server manager, Minecraft server GUI, Minecraft server Windows, マイクラ サーバー 管理, マイクラ サーバー 立て方" },
    { name: "author", content: "BlockPilot" },
    { name: "robots", content: "index,follow" },
    { name: "theme-color", content: "#0b0f17" },
    { name: "format-detection", content: "telephone=no" },
    { property: "og:title", content: t.value.metaTitle },
    { property: "og:description", content: t.value.metaDescription },
    { property: "og:type", content: "website" },
    { property: "og:site_name", content: t.value.siteName },
    { property: "og:locale", content: currentLang.value === "ja" ? "ja_JP" : "en_US" },
    ...(siteUrl.value ? [{ property: "og:image", content: `${siteUrl.value}/icon.png` }] : []),
    ...(siteUrl.value ? [{ property: "og:url", content: `${siteUrl.value}/` }] : []),
    { name: "twitter:card", content: "summary_large_image" },
    { name: "twitter:title", content: t.value.metaTitle },
    { name: "twitter:description", content: t.value.metaDescription },
    ...(siteUrl.value ? [{ name: "twitter:image", content: `${siteUrl.value}/icon.png` }] : []),
  ],
  link: siteUrl.value ? [{ rel: "canonical", href: `${siteUrl.value}/` }] : [],
  script: [
    {
      type: "application/ld+json",
      key: "ld-json",
      children: JSON.stringify([
        {
          "@context": "https://schema.org",
          "@type": "WebSite",
          name: t.value.siteName,
          url: siteUrl.value ? `${siteUrl.value}/` : undefined,
          inLanguage: currentLang.value,
        },
        {
          "@context": "https://schema.org",
          "@type": "SoftwareApplication",
          name: t.value.siteName,
          description: t.value.metaDescription,
          operatingSystem: "Windows 10/11",
          applicationCategory: "UtilitiesApplication",
          softwareVersion: version,
          url: siteUrl.value ? `${siteUrl.value}/` : undefined,
        },
      ]),
    },
  ],
}));
</script>

<template>
  <div class="page">
    <header class="nav">
      <div class="brand">
        <img src="/icon.png" alt="BlockPilot" class="brand-icon" />
        <span class="brand-name">BlockPilot</span>
      </div>
      <nav class="nav-links">
        <a href="#features">{{ t.nav.features }}</a>
        <a href="#help">{{ t.nav.help }}</a>
        <a href="#how">{{ t.nav.how }}</a>
        <a href="#compat">{{ t.nav.compat }}</a>
        <a href="#security">{{ t.nav.security }}</a>
        <a href="#download" class="nav-cta">{{ t.nav.download }}</a>
        <div class="lang-toggle" aria-label="Language toggle">
          <span class="lang-label">{{ t.langLabel }}</span>
          <button
            class="lang-btn"
            :class="{ active: currentLang === 'ja' }"
            type="button"
            @click="setLang('ja')"
          >
            {{ t.langJa }}
          </button>
          <button
            class="lang-btn"
            :class="{ active: currentLang === 'en' }"
            type="button"
            @click="setLang('en')"
          >
            {{ t.langEn }}
          </button>
        </div>
      </nav>
    </header>

    <main>
      <section class="hero">
        <div class="hero-bg" aria-hidden="true"></div>
        <div class="hero-content">
          <p class="eyebrow">{{ t.eyebrow }}</p>
          <h1 class="hero-title">{{ t.heroTitle }}</h1>
          <p class="hero-sub">{{ t.heroSub }}</p>
          <ul class="hero-notes">
            <li v-for="note in t.heroNotes" :key="note">{{ note }}</li>
          </ul>
          <div class="hero-affiliate">
            <span>{{ t.heroAffiliateLabel }}</span>
            <a :href="docsHostingUrl" class="hero-affiliate-link" data-event="docs_24_7_hosting_click">
              {{ t.heroAffiliateCta }}
            </a>
          </div>
          <div class="hero-actions">
            <a :href="downloadUrl" class="btn primary" data-event="download_click">{{ t.heroCtaPrimary }}</a>
            <a :href="docsUrl" class="btn ghost">{{ t.heroCtaSecondary }}</a>
          </div>
          <div class="hero-meta">
            <span class="chip">{{ t.heroMetaVersion }} {{ version }}</span>
            <span class="chip">{{ t.heroMetaWindows }}</span>
            <span class="chip">{{ t.heroMetaRuntime }}</span>
          </div>
        </div>
        <div class="hero-card">
          <div class="hero-card-header">
            <span>{{ t.cardTitle }}</span>
            <span class="pulse"></span>
          </div>
          <div class="hero-card-body">
            <div class="stat">
              <span class="stat-label">{{ t.cardStatusLabel }}</span>
              <span class="stat-value">{{ t.cardStatusValue }}</span>
            </div>
            <div class="stat">
              <span class="stat-label">{{ t.cardPlayersLabel }}</span>
              <span class="stat-value">6 / 20</span>
            </div>
            <div class="stat">
              <span class="stat-label">{{ t.cardJavaLabel }}</span>
              <span class="stat-value">17.0.x</span>
            </div>
            <div class="hero-bars">
              <div class="bar">
                <span>{{ t.cardCpu }}</span>
                <div class="bar-track"><div class="bar-fill" style="width: 48%"></div></div>
              </div>
              <div class="bar">
                <span>{{ t.cardMemory }}</span>
                <div class="bar-track"><div class="bar-fill alt" style="width: 64%"></div></div>
              </div>
            </div>
            <div class="hero-cta">
              <button class="btn tiny" type="button">{{ t.cardStart }}</button>
              <button class="btn tiny ghost" type="button">{{ t.cardBackup }}</button>
              <button class="btn tiny ghost" type="button">{{ t.cardFirewall }}</button>
            </div>
          </div>
        </div>
      </section>

      <section id="features" class="section reveal">
        <div class="section-head">
          <h2>{{ t.featuresTitle }}</h2>
          <p>{{ t.featuresSub }}</p>
        </div>
        <div class="grid features">
          <div v-for="item in t.features" :key="item.title" class="panel">
            <h3>{{ item.title }}</h3>
            <p>{{ item.body }}</p>
          </div>
        </div>
        <div class="features-affiliate">
          <span>{{ t.featuresAffiliateLabel }}</span>
          <a :href="docsHostingUrl" class="features-affiliate-link" data-event="docs_24_7_hosting_click">
            {{ t.featuresAffiliateCta }}
          </a>
        </div>
      </section>

      <section id="help" class="section reveal">
        <div class="section-head">
          <h2>{{ t.helpTitle }}</h2>
          <p>{{ t.helpSub }}</p>
        </div>
        <div class="grid support">
          <div v-for="item in t.helpItems" :key="item.title" class="panel">
            <h3>{{ item.title }}</h3>
            <p>{{ item.body }}</p>
          </div>
        </div>
        <div class="support-cta">
          <a :href="docsUrl" class="btn ghost">{{ t.helpCta }}</a>
        </div>
      </section>

      <section id="how" class="section split reveal">
        <div>
          <h2>{{ t.stepsTitle }}</h2>
          <ol class="steps">
            <li v-for="step in t.steps" :key="step.title">
              <strong>{{ step.title }}</strong>
              {{ step.body }}
            </li>
          </ol>
        </div>
        <div class="panel preview">
          <div class="preview-header">
            <span>{{ t.previewTitle }}</span>
            <span class="pill">{{ t.previewStatus }}</span>
          </div>
          <div class="preview-body">
            <p v-for="line in t.previewLines" :key="line">{{ line }}</p>
          </div>
        </div>
      </section>

      <section id="compat" class="section reveal">
        <div class="section-head">
          <h2>{{ t.compatTitle }}</h2>
          <p>{{ t.compatSub }}</p>
        </div>
        <div class="compat-grid">
          <div v-for="item in t.compat" :key="item.title" class="compat-card">
            <h4>{{ item.title }}</h4>
            <span class="badge">{{ item.badge }}</span>
          </div>
        </div>
        <ul class="compat-notes">
          <li v-for="note in t.compatNotes" :key="note">{{ note }}</li>
        </ul>
      </section>

      <section id="security" class="section reveal">
        <div class="section-head">
          <h2>{{ t.securityTitle }}</h2>
          <p>{{ t.securitySub }}</p>
        </div>
        <div class="grid security">
          <div v-for="item in t.securityItems" :key="item.title" class="panel">
            <h3>{{ item.title }}</h3>
            <p>{{ item.body }}</p>
          </div>
        </div>
        <div class="security-cta">
          <a :href="docsPrivacyUrl" class="btn ghost">{{ t.securityCta }}</a>
        </div>
      </section>

      <section id="download" class="section reveal">
        <div class="callout">
          <div>
            <h2>{{ t.downloadTitle }}</h2>
            <p>{{ t.downloadSub }}</p>
          </div>
          <div class="callout-actions">
            <a :href="downloadUrl" class="btn primary" data-event="download_click">{{ t.downloadPrimary }}</a>
            <a :href="docsUrl" class="btn ghost">{{ t.downloadSecondary }}</a>
          </div>
        </div>
        <div class="download-options-block">
          <div class="section-head">
            <h2>{{ t.downloadOptionsTitle }}</h2>
            <p>{{ t.downloadOptionsSub }}</p>
          </div>
          <div class="grid download-options">
            <div v-for="option in t.downloadOptions" :key="option.title" class="panel">
              <h3>{{ option.title }}</h3>
              <p>{{ option.body }}</p>
              <a
                :href="downloadOptionLinks[option.id]"
                class="btn ghost"
                :data-event="downloadOptionEvents[option.id]"
              >
                {{ option.cta }}
              </a>
            </div>
          </div>
        </div>
      </section>

      <section class="section reveal">
        <div class="section-head">
          <h2>{{ t.faqTitle }}</h2>
          <p>{{ t.faqSub }}</p>
        </div>
        <div class="grid faq">
          <div v-for="item in t.faq" :key="item.title" class="panel">
            <h3>{{ item.title }}</h3>
            <p>{{ item.body }}</p>
            <a
              v-if="item.linkId"
              :href="faqLinks[item.linkId]"
              class="faq-link"
              :data-event="item.linkId === 'hosting' ? 'docs_24_7_hosting_click' : null"
            >
              {{ item.linkLabel }}
            </a>
          </div>
        </div>
      </section>
    </main>

    <footer class="footer">
      <div class="footer-left">
        <img src="/icon.png" alt="BlockPilot" class="brand-icon small" />
        <span>BlockPilot</span>
      </div>
      <div class="footer-links">
        <a :href="docsUrl">{{ t.footerDocs }}</a>
        <a href="/THIRD_PARTY_NOTICES.txt">{{ t.footerNotices }}</a>
        <a href="/LICENSE.txt">{{ t.footerLicense }}</a>
      </div>
      <div class="footer-right">{{ t.footerRight }}</div>
      <div class="footer-disclosure">{{ t.footerDisclosure }}</div>
    </footer>
  </div>
</template>

