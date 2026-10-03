<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, nextTick } from "vue";

import { screenshots } from "~/data/screenshots";

const featureShots = ["addons", "backups", "worlds"] as const;

const mobileMenuOpen = ref(false);
const closeMobileMenu = () => { mobileMenuOpen.value = false; };
const isMobileDevice = ref(false);
const pcHandoffOpen = ref(false);
const openPcHandoff = () => { if (isMobileDevice.value) pcHandoffOpen.value = true; };
const closeMenuOnEscape = (event: KeyboardEvent) => {
  if (event.key === "Escape") closeMobileMenu();
};

let revealObserver: IntersectionObserver | null = null;

const defaultDownloadPath = "https://stmaipilot.blob.core.windows.net/public/downloads/MaiPilotSetup.exe";
const docsUrl = "/docs";
const docsLanUrl = "/docs/lan";
const docsHostingUrl = "/docs/24-7-hosting";
const docsPortUrl = "/docs/port-forwarding";
const docsTroubleUrl = "/docs/troubleshooting";
const docsPrivacyUrl = "/docs/privacy-and-network";
const docsJavaUrl = "/docs/java-setup/";
const boothUrl = "https://maipilot.booth.pm/items/8118402";

const downloadOptionEvents: Record<string, string> = {
  local: "download_click",
  hosting: "docs_24_7_hosting_click",
};

const faqLinks: Record<string, string> = {
  hosting: docsHostingUrl,
  port: docsPortUrl,
  trouble: docsTroubleUrl,
  java: docsJavaUrl,
};

const translations = {
  en: {
    metaTitle: "MaiPilot | All-in-one Minecraft server manager for Windows",
    metaDescription: "Manage Minecraft Java and Bedrock servers on Windows. Set up Java, manage compatible mods and plugins, and keep worlds backed up. Free for personal, non-commercial use.",
    siteName: "MaiPilot",
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
    eyebrow: "All-in-one Minecraft server management for Windows",
    heroTitle: "From your first server to everyday play.",
    heroTitleParts: ["From your first server ", "to everyday play."],
    heroSub: "Set up and manage Minecraft Java and Bedrock servers from one Windows app.",
    heroNotes: [
      "Choose from six Java server types or the official Bedrock Dedicated Server.",
      "Manage local worlds and backups. Add mods and plugins on supported Java servers."
    ],
    heroAffiliateLabel: "Need always-on hosting?",
    heroAffiliateCta: "See hosting options (PR)",
    heroCtaPrimary: "Download free for Windows",
    heroMobileCta: "Use on a Windows PC",
    freeNote: "Official installer · Free for personal, non-commercial use",
    supportNote: "Optional ¥100 support edition on BOOTH. Same features.",
    heroCtaSecondary: "Read the setup guide",
    heroBoothCta: "Support development (BOOTH)",
    heroMetaVersion: "Version",
    heroMetaWindows: "Windows 10/11",
    heroMetaRuntime: ".NET runtime included",
    featuresTitle: "Build your world. Keep it running.",
    featuresSub: "Choose the right edition, then manage the tasks that matter to your server.",
    features: [
      {
        "title": "Java + Bedrock",
        "body": "Vanilla, Paper, Purpur, Fabric, Forge and Spigot for Java Edition, plus the official Bedrock Dedicated Server (BDS)."
      },
      {
        "title": "Automatic Java setup",
        "body": "Download and configure a compatible Eclipse Temurin JRE from the app. Bedrock servers do not require Java."
      },
      {
        "title": "Mods and plugins for Java",
        "body": "Add, enable and disable compatible mods or plugins; search Modrinth in the app. Available for supported Java server types, not Vanilla or BDS."
      },
      {
        "title": "Backups and worlds",
        "body": "Create ZIP backups, restore worlds and schedule backups. Manage your worlds from the sidebar."
      },
      {
        "title": "Server settings",
        "body": "Edit settings through a GUI. The available settings and management tools depend on the server edition."
      },
      {
        "title": "Connection and sharing",
        "body": "Check connection information and manage firewall or UPnP settings. External access depends on your router, ISP and server protocol."
      },
      {
        "title": "Monitoring and recovery",
        "body": "View server status and logs, inspect crashes, and configure automatic restart."
      },
      {
        "title": "Changes under your control",
        "body": "Save configuration changes, then restart the server when required to apply them."
      },
      {
        "title": "Resource packs for Java",
        "body": "Host Java resource packs using an HTTP server and Cloudflare Quick Tunnel. Starting distribution publishes the selected pack through an external service. This feature is not available for BDS."
      }
    ],
    featuresAffiliateLabel: "Always-on hosting is an option if your PC cannot stay on.",
    featuresAffiliateCta: "Compare hosting options (PR)",
    stepsTitle: "Your first server, in three stages",
    steps: [
      {
        "title": "Choose an edition and server type.",
        "body": "Select Java or Bedrock, a version and a save location. Review and accept the applicable Minecraft terms."
      },
      {
        "title": "Prepare the server.",
        "body": "Download the server files and configure its settings. For Java, the app can set up a compatible Temurin JRE."
      },
      {
        "title": "Check, then start.",
        "body": "Review requirements and connection settings. Start locally, and check router and ISP conditions before sharing outside your LAN."
      }
    ],
    helpTitle: "Guided setup that prevents surprises",
    helpSub: "Clear checks and next steps before you press Start.",
    helpItems: [
      {
        "title": "Java setup, guided",
        "body": "Use the app to download and configure Java. Existing installations can also be detected; requirements vary with the server version."
      },
      {
        "title": "Connection guidance",
        "body": "Use Connection & Sharing to review addresses and required ports. Bedrock connection behaviour can vary by server version and protocol."
      },
      {
        "title": "Crash recovery",
        "body": "Inspect logs and crash reports, and configure automatic restart for unexpected stops."
      }
    ],
    helpCta: "Open setup notes",
    compatTitle: "Java requirements, with setup built in",
    compatSub: "MaiPilot checks the required Java version and can download a suitable Temurin JRE. The table is a guide for Java Edition; Bedrock does not need Java.",
    compat: [
      {
        "title": "26.1 and later",
        "badge": "Java 25+"
      },
      {
        "title": "1.20.5 – 1.21.x",
        "badge": "Java 21+"
      },
      {
        "title": "1.18 – 1.20.4",
        "badge": "Java 17+"
      },
      {
        "title": "1.17",
        "badge": "Java 16+"
      },
      {
        "title": "1.16 and earlier",
        "badge": "Java 8+"
      }
    ],
    compatNotes: [
      "Server implementations and mods may have additional requirements. Check the selected version before starting.",
      "Java setup needs an internet connection and downloads from Adoptium/GitHub.",
      ".NET is included with MaiPilot; the Java server runtime is a separate download."
    ],
    securityTitle: "Security & Privacy",
    securitySub: "Local storage, with clear reasons for external connections.",
    securityItems: [
      {
        "title": "Local server data",
        "body": "Worlds, settings and backups are stored on your PC. They are not automatically uploaded to cloud storage."
      },
      {
        "title": "Downloads and checks",
        "body": "The app contacts external services for updates, server files, Java setup, Modrinth and public IP lookup."
      },
      {
        "title": "Optional resource pack sharing",
        "body": "Java resource pack distribution uses Cloudflare Quick Tunnel to expose the selected pack. Start it only when you intend to distribute that file."
      },
      {
        "title": "Network changes",
        "body": "Firewall changes may require Windows administrator permission. UPnP depends on router support; it does not guarantee external access."
      },
      {
        "title": "Local logs",
        "body": "Logs and crash reports stay local. Review addresses, player names and paths before sharing them."
      }
    ],
    securityCta: "Open privacy & network notes",
    downloadTitle: "Start with the free version",
    downloadSub: "Windows 10/11 (x64). Free for personal, non-commercial use. The .NET runtime is included; server files and Java, when needed, are downloaded separately.",
    downloadOptionsTitle: "Choose the right path",
    downloadOptionsSub: "We show options based on your situation.",
    downloadOptions: [
      {
        "id": "local",
        "title": "Run locally on this PC",
        "body": "Manage Java and Bedrock servers on this PC. Features depend on the edition and server type.",
        "cta": "Get the Windows .exe installer"
      },
      {
        "id": "lan",
        "title": "LAN only on the same Wi-Fi",
        "body": "Keep it local for friends on your home network.",
        "cta": "LAN setup guide"
      },
      {
        "id": "hosting",
        "title": "24/7 hosting or public access (PR)",
        "body": "If your PC cannot stay on, hosted servers can help.",
        "cta": "See options (PR)"
      }
    ],
    downloadPrimary: "Download free for Windows",
    downloadSecondary: "Setup notes",
    downloadBoothCta: "Support development (BOOTH)",
    prSectionTitle: "Sponsored options (PR)",
    prSectionSub: "Sponsored banners with text links below each banner.",
    prTextLinkLabel: "Text Link (PR)",
    prItems: [
      {
        kind: "banner",
        title: "ConoHa VPS (PR)",
        description: "GMO Internet ConoHa VPS. Good for always-on public server hosting.",
        cta: "Open ConoHa VPS (PR)",
        href: "https://px.a8.net/svt/ejp?a8mat=4AXJKD+2YKMU2+50+4YQJIQ",
        imageSrc: "https://www28.a8.net/svt/bgt?aid=260225869179&wid=002&eno=01&mid=s00000000018030129000&mc=1",
        imageAlt: "ConoHa VPS sponsored banner",
        eventName: "pr_conoha_click",
      },
      {
        kind: "banner",
        title: "XServer VPS for Game (PR)",
        description: "Beginner-friendly game VPS with easy templates and low starting cost.",
        cta: "Open XServer VPS for Game (PR)",
        href: "https://px.a8.net/svt/ejp?a8mat=4AXKC8+D3JCNU+CO4+2NAN35",
        imageSrc: "https://www20.a8.net/svt/bgt?aid=260226872792&wid=002&eno=01&mid=s00000001642016006000&mc=1",
        imageAlt: "XServer VPS for Game sponsored banner",
        eventName: "pr_xserver_click",
      },
      {
        kind: "banner",
        title: "Shin VPS (PR)",
        description: "High-memory-cost-performance VPS for players who need more resources.",
        cta: "Open Shin VPS (PR)",
        href: "https://px.a8.net/svt/ejp?a8mat=4AXJKD+2WSC0Q+5GDG+NVP2P",
        textHref: "https://px.a8.net/svt/ejp?a8mat=4AXJKD+2WSC0Q+5GDG+NTJWY",
        imageSrc: "https://www29.a8.net/svt/bgt?aid=260225869176&wid=002&eno=01&mid=s00000025450004011000&mc=1",
        imageAlt: "Shin VPS sponsored banner",
        eventName: "pr_shinvps_click",
      },
    ],
    guidesTitle: "Guides & Articles",
    guidesSub: "Step-by-step docs for common tasks.",
    guides: [
      { title: "How to set up a Minecraft server (Windows)", href: "/docs/growth/windows-minecraft-server-2026/" },
      { title: "How to install MODs (Forge / Fabric)", href: "/docs/growth/forge-fabric-mod-installation/" },
      { title: "How to install Plugins (Paper / Spigot)", href: "/docs/growth/paper-spigot-plugin-installation/" },
      { title: "How to install a distribution map", href: "/docs/growth/haichi-map-installation/" },
      { title: "How to install datapacks", href: "/docs/growth/minecraft-server-datapack-install/" },
      { title: "Gamerule settings guide", href: "/docs/growth/minecraft-server-gamerule-settings/" },
      { title: "Player management (BAN / OP / Whitelist)", href: "/docs/growth/minecraft-server-player-management/" },
      { title: "Resource pack distribution guide", href: "/docs/growth/resource-pack-distribution/" },
      { title: "Port-forwarding checklist", href: "/docs/port-forwarding/" },
      { title: "Java setup guide", href: "/docs/java-setup/" },
      { title: "Troubleshooting", href: "/docs/troubleshooting/" },
    ],
    guidesMore: "See all guides",
    faqTitle: "FAQ",
    faqSub: "Quick answers for a smooth start.",
    faq: [
      {
        title: "Can I run MaiPilot on my phone or Mac?",
        body: "MaiPilot runs on a Windows 10/11 PC, not on phones or macOS. If you are browsing on mobile, copy this page’s link and open it on your Windows PC. Player-device compatibility depends on the Minecraft edition and server; console access is not guaranteed."
      },
      {
        "title": "Which server types are supported?",
        "body": "Vanilla, Paper, Purpur, Fabric, Forge and Spigot for Java Edition, plus the official Bedrock Dedicated Server (BDS)."
      },
      {
        "title": "Does it work with mods and plugins?",
        "body": "Add, enable and disable compatible mods or plugins; search Modrinth in the app. Available for supported Java server types, not Vanilla or BDS."
      },
      {
        "title": "I cannot keep my PC on 24/7.",
        "body": "If you need always-on hosting, see the options.",
        "linkId": "hosting",
        "linkLabel": "View hosting options (PR)"
      },
      {
        "title": "External access is not working.",
        "body": "Shared networks, CGNAT and double NAT can prevent external access. Bedrock also depends on the version and transport, including RakNet or NetherNet. Connectivity is not guaranteed in every environment.",
        "linkId": "port",
        "linkLabel": "Open port-forwarding guide"
      },
      {
        "title": "It crashed and I do not know where logs are.",
        "body": "Inspect logs and crash reports, and configure automatic restart for unexpected stops.",
        "linkId": "trouble",
        "linkLabel": "Open troubleshooting"
      },
      {
        "title": "How do I install Java?",
        "body": "The app can download and configure a compatible Eclipse Temurin JRE, or detect an existing Java installation. Downloading requires internet access. Bedrock does not need Java.",
        "linkId": "java",
        "linkLabel": "Open Java setup guide"
      },
      {
        "title": "Where are server files stored?",
        "body": "By default in your AppData folder. Custom locations are supported."
      },
      {
        "title": "Is it free?",
        "body": "The official download and the ¥100 BOOTH support edition have the same features. BOOTH is an optional way to support development. Try the free version first. Both editions are licensed for personal, non-commercial use; purchasing does not grant commercial-use rights."
      },
      {
        "title": "How do I update?",
        "body": "Choose “Check for updates” in MaiPilot, then run the latest installer shown in the prompt."
      }
    ],
    boothTitle: "Support MaiPilot's development",
    boothSub: "The official download and the ¥100 BOOTH support edition have the same features.",
    boothNote: "BOOTH is an optional way to support development. Try the free version first. Both editions are licensed for personal, non-commercial use; purchasing does not grant commercial-use rights.",
    boothCta: "Support for ¥100 on BOOTH",
    footerDocs: "Docs",
    footerNotices: "Third-party notices",
    footerLicense: "License",
    footerPrivacy: "Privacy Policy",
    footerTerms: "Terms of Use",
    footerX: "Official X",
    footerRight: "Built for local worlds.",
    footerDisclosure: "This page includes affiliate links (PR).",
    galleryTitle: "A place for every everyday task",
    gallerySub: "Create a server, organise your worlds and manage compatible add-ons from the sidebar.",
    screenshotPending: "Screenshot in preparation",
    screenshotSource: "MaiPilot 2.2.0 UI rendered with demo data.",
    screenshotPendingNote: "A verified screenshot of the current app will appear here.",
    screenshots: {
      "overview": {
        "title": "Your server at a glance",
        "caption": "Status, configuration and everyday actions in one place.",
        "alt": "MaiPilot server overview in the current Windows app"
      },
      "create": {
        "title": "Choose Java or Bedrock",
        "caption": "Start with the edition and server type you want to play.",
        "alt": "MaiPilot new-server edition and type selection"
      },
      "addons": {
        "title": "Make a Java server your own",
        "caption": "Manage mods or plugins for a supported Java server type.",
        "alt": "MaiPilot add-on management on a supported Java server"
      },
      "backups": {
        "title": "Keep a restore point",
        "caption": "Create, schedule and manage local world backups.",
        "alt": "MaiPilot backup management"
      },
      "worlds": {
        "title": "Look after your worlds",
        "caption": "Manage saved worlds from a dedicated screen.",
        "alt": "MaiPilot world management"
      }
    },
  },
  ja: {
    metaTitle: "MaiPilot | Windows向けマイクラサーバー統合管理",
    metaDescription: "Java版・統合版のマイクラサーバーをWindowsで管理。Java自動セットアップ、対応MOD・プラグイン管理、ワールドのバックアップをひとつのアプリで。個人・非商用利用は無料。",
    siteName: "MaiPilot",
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
    eyebrow: "Windows向け Minecraft サーバー統合管理",
    heroTitle: "マイクラサーバーの準備も、日々の管理も。",
    heroTitleParts: ["マイクラサーバーの", "準備も、", "日々の管理も。"],
    heroSub: "Java版・統合版のサーバーを、Windowsのひとつの画面で。",
    heroNotes: [
      "Java版6種と、公式の統合版専用サーバー（BDS）に対応。",
      "ワールドとバックアップを手元で管理。対応するJavaサーバーではMOD・プラグインも。"
    ],
    heroAffiliateLabel: "24時間運用の選択肢もあります",
    heroAffiliateCta: "VPSの比較を見る（PR）",
    heroCtaPrimary: "Windows版を無料ダウンロード",
    heroMobileCta: "Windows PCで使う準備をする",
    freeNote: "公式インストーラー・個人／非商用利用は無料",
    supportNote: "BOOTHの100円支援版は任意購入です。機能は同じ。",
    heroCtaSecondary: "セットアップガイド",
    heroBoothCta: "開発を応援する（BOOTH）",
    heroMetaVersion: "バージョン",
    heroMetaWindows: "Windows 10/11 対応",
    heroMetaRuntime: ".NETランタイム同梱",
    featuresTitle: "遊びたい世界をつくる。その後の管理まで。",
    featuresSub: "エディションを選んで、サーバーに合った機能を使えます。",
    features: [
      {
        "title": "Java版＋統合版に対応",
        "body": "Java版はVanilla・Paper・Purpur・Fabric・Forge・Spigotの6種。統合版は公式のBedrock Dedicated Server（BDS）に対応。"
      },
      {
        "title": "Javaをアプリから自動セットアップ",
        "body": "必要なバージョンに合わせてEclipse Temurin JREをダウンロードし、設定できます。統合版にはJavaは不要です。"
      },
      {
        "title": "Java版のMOD・プラグイン管理",
        "body": "対応するサーバー種別で追加・有効化・無効化、Modrinth検索が可能。Vanilla・BDSはこの機能の対象外です。"
      },
      {
        "title": "バックアップとワールド管理",
        "body": "ZIPバックアップ、復元、定期バックアップに対応。サイドバーからワールドを管理できます。"
      },
      {
        "title": "サーバー設定をGUIで",
        "body": "設定ファイルを直接編集せず、画面から変更。利用できる設定項目や管理機能はエディションによって異なります。"
      },
      {
        "title": "接続・公開をサポート",
        "body": "接続情報の確認、ファイアウォールやUPnPの操作を集約。外部接続の可否はルーター・回線・通信方式に左右されます。"
      },
      {
        "title": "監視とクラッシュ復旧",
        "body": "稼働状況やログを確認し、クラッシュ時の調査へ。予期しない停止に備えた自動再起動も設定できます。"
      },
      {
        "title": "変更は確認して反映",
        "body": "設定を保存し、反映に再起動が必要な項目はサーバーを再起動して適用します。"
      },
      {
        "title": "Java版のリソースパック配布",
        "body": "HTTPサーバーとCloudflare Quick Tunnelで配信。配信を開始すると、選んだパックを外部サービス経由で公開します。BDSは対象外です。"
      }
    ],
    featuresAffiliateLabel: "PCをつけっぱなしにできない場合は、VPSという選択肢もあります。",
    featuresAffiliateCta: "24時間運用の比較を見る（PR）",
    stepsTitle: "はじめてのサーバー、3つの段階",
    steps: [
      {
        "title": "エディションと種別を選ぶ",
        "body": "Java版・統合版、バージョン、保存先を選択。Minecraftの適用される利用条件を確認し、同意します。"
      },
      {
        "title": "サーバーを準備する",
        "body": "必要なファイルを取得して設定。Java版では、対応するTemurin JREをアプリから自動セットアップできます。"
      },
      {
        "title": "確認して起動する",
        "body": "動作条件と接続設定を確認してローカルで起動。家の外へ公開する前に、ルーターや回線の条件も確認します。"
      }
    ],
    helpTitle: "ガイド付きでスムーズに起動",
    helpSub: "起動前のチェックと次の一手を見える化。",
    helpItems: [
      {
        "title": "Javaの準備を案内",
        "body": "アプリからJavaをダウンロードして設定。既存のJavaも検出でき、選んだサーバーバージョンに応じた要件を確認します。"
      },
      {
        "title": "接続条件を確認",
        "body": "「接続・公開」でアドレスと必要なポートを確認。統合版はサーバーバージョンや通信方式によって接続条件が異なります。"
      },
      {
        "title": "クラッシュからの復旧",
        "body": "ログとクラッシュレポートを確認。予期しない停止に備えて、自動再起動も設定できます。"
      }
    ],
    helpCta: "セットアップノートを見る",
    compatTitle: "Javaの要件確認から、自動セットアップまで",
    compatSub: "必要なJavaを判定し、対応するTemurin JREを取得できます。表はJava版の目安です。統合版にはJavaは不要です。",
    compat: [
      {
        "title": "26.1以降",
        "badge": "Java 25+"
      },
      {
        "title": "1.20.5〜1.21.x",
        "badge": "Java 21+"
      },
      {
        "title": "1.18〜1.20.4",
        "badge": "Java 17+"
      },
      {
        "title": "1.17",
        "badge": "Java 16+"
      },
      {
        "title": "1.16以前",
        "badge": "Java 8+"
      }
    ],
    compatNotes: [
      "サーバー実装やMODによって追加の要件があります。起動前に対象バージョンをご確認ください。",
      "Javaの自動導入にはインターネット接続が必要です。Adoptium/GitHubからダウンロードします。",
      "MaiPilot用の.NETは同梱。Javaサーバー用の実行環境は別途ダウンロードします。"
    ],
    securityTitle: "セキュリティ/プライバシー",
    securitySub: "手元に保存し、必要な目的で外部サービスにつなぎます。",
    securityItems: [
      {
        "title": "サーバーデータはローカル保存",
        "body": "ワールド・設定・バックアップはPC内に保存。クラウドストレージへ自動アップロードしません。"
      },
      {
        "title": "取得・確認のための外部通信",
        "body": "更新確認、サーバーファイルやJavaの取得、Modrinth、公開IPの取得などで外部サービスと通信します。"
      },
      {
        "title": "必要なときだけパックを配信",
        "body": "Java版リソースパックの配信にはCloudflare Quick Tunnelを使用。選んだファイルを公開する場合に配信を開始してください。"
      },
      {
        "title": "ネットワーク設定の変更",
        "body": "ファイアウォール変更にはWindowsの管理者権限が必要になる場合があります。UPnPはルーターの対応状況に依存し、外部接続を保証しません。"
      },
      {
        "title": "ログも手元で管理",
        "body": "ログとクラッシュレポートはローカル保存。共有する際はIP・プレイヤー名・保存パスなどをご確認ください。"
      }
    ],
    securityCta: "通信と権限の詳細を見る",
    downloadTitle: "まずは無料版から",
    downloadSub: "Windows 10/11（x64）向け。個人・非商用利用は無料です。.NETは同梱。サーバーファイルと、必要な場合のJavaは別途ダウンロードします。",
    downloadOptionsTitle: "状況別の選び方",
    downloadOptionsSub: "おすすめではなく、選択肢として提示します。",
    downloadOptions: [
      {
        "id": "local",
        "title": "このPCでローカル運用",
        "body": "Java版・統合版のサーバーをこのPCで管理。機能はエディションや種別によって異なります。",
        "cta": "Windows用 .exe を保存"
      },
      {
        "id": "lan",
        "title": "同じWi-FiでLAN参加",
        "body": "家の中だけで遊びたい人向け。",
        "cta": "LAN手順を見る"
      },
      {
        "id": "hosting",
        "title": "24時間稼働・外部公開したい（PR）",
        "body": "PCをつけっぱなしにできない場合の選択肢。",
        "cta": "比較を見る（PR）"
      }
    ],
    downloadPrimary: "Windows版を無料ダウンロード",
    downloadSecondary: "セットアップノート",
    downloadBoothCta: "開発を応援する（BOOTH）",
    prSectionTitle: "スポンサーリンク（PR）",
    prSectionSub: "各バナーの下にテキストリンクを配置しています。",
    prTextLinkLabel: "テキストリンク（PR）",
    prItems: [
      {
        kind: "banner",
        title: "ConoHa VPS（PR）",
        description: "GMOインターネットのVPS。24時間運用や外部公開向け。",
        cta: "ConoHa VPSを見る（PR）",
        href: "https://px.a8.net/svt/ejp?a8mat=4AXJKD+2YKMU2+50+4YQJIQ",
        imageSrc: "https://www28.a8.net/svt/bgt?aid=260225869179&wid=002&eno=01&mid=s00000000018030129000&mc=1",
        imageAlt: "ConoHa VPS スポンサードバナー",
        eventName: "pr_conoha_click",
      },
      {
        kind: "banner",
        title: "XServer VPS for Game（PR）",
        description: "ゲーム向けテンプレートがあり、初めてでも始めやすいVPSです。",
        cta: "XServer VPS for Gameを見る（PR）",
        href: "https://px.a8.net/svt/ejp?a8mat=4AXKC8+D3JCNU+CO4+2NAN35",
        imageSrc: "https://www20.a8.net/svt/bgt?aid=260226872792&wid=002&eno=01&mid=s00000001642016006000&mc=1",
        imageAlt: "XServer VPS for Game スポンサードバナー",
        eventName: "pr_xserver_click",
      },
      {
        kind: "banner",
        title: "シンVPS（PR）",
        description: "より高いスペックを選びたいときの候補です。",
        cta: "シンVPSを見る（PR）",
        href: "https://px.a8.net/svt/ejp?a8mat=4AXJKD+2WSC0Q+5GDG+NVP2P",
        textHref: "https://px.a8.net/svt/ejp?a8mat=4AXJKD+2WSC0Q+5GDG+NTJWY",
        imageSrc: "https://www29.a8.net/svt/bgt?aid=260225869176&wid=002&eno=01&mid=s00000025450004011000&mc=1",
        imageAlt: "シンVPS スポンサードバナー",
        eventName: "pr_shinvps_click",
      },
    ],
    guidesTitle: "ガイド・攻略記事",
    guidesSub: "よくある作業をステップごとに解説。",
    guides: [
      { title: "マイクラサーバーの立て方（Windows 2026年版）", href: "/docs/growth/windows-minecraft-server-2026/" },
      { title: "MOD の入れ方（Forge / Fabric）", href: "/docs/growth/forge-fabric-mod-installation/" },
      { title: "プラグインの入れ方（Paper / Spigot）", href: "/docs/growth/paper-spigot-plugin-installation/" },
      { title: "配布マップの入れ方・遊び方", href: "/docs/growth/haichi-map-installation/" },
      { title: "データパックの入れ方", href: "/docs/growth/minecraft-server-datapack-install/" },
      { title: "gamerule（ゲームルール）設定ガイド", href: "/docs/growth/minecraft-server-gamerule-settings/" },
      { title: "プレイヤー管理（BAN・OP・ホワイトリスト）", href: "/docs/growth/minecraft-server-player-management/" },
      { title: "リソースパック配布ガイド", href: "/docs/growth/resource-pack-distribution/" },
      { title: "ポート開放チェックリスト", href: "/docs/port-forwarding/" },
      { title: "Java 導入ガイド（初心者向け）", href: "/docs/java-setup/" },
      { title: "トラブルシュート", href: "/docs/troubleshooting/" },
    ],
    guidesMore: "記事をすべて見る",
    faqTitle: "よくある質問",
    faqSub: "スムーズに始めるためのヒント。",
    faq: [
      {
        title: "スマホやMacでも使えますか？",
        body: "MaiPilotを動かすにはWindows 10/11のPCが必要です。スマホで見ている方は、このページのリンクをコピーしてWindows PCで開いてください。参加できる端末はMinecraftのエディションやサーバーの条件によって異なり、家庭用ゲーム機からの接続を保証するものではありません。"
      },
      {
        "title": "対応サーバー種別は？",
        "body": "Java版はVanilla・Paper・Purpur・Fabric・Forge・Spigotの6種。統合版は公式のBedrock Dedicated Server（BDS）に対応。"
      },
      {
        "title": "MOD/プラグインは使えますか？",
        "body": "対応するサーバー種別で追加・有効化・無効化、Modrinth検索が可能。Vanilla・BDSはこの機能の対象外です。"
      },
      {
        "title": "PCをつけっぱなしにできません",
        "body": "常時稼働が必要な場合はVPSの選択肢があります。",
        "linkId": "hosting",
        "linkLabel": "24時間運用の選択肢を見る（PR）"
      },
      {
        "title": "外部公開ができません",
        "body": "共有回線やCGNAT、二重ルーター等では外部公開できない場合があります。統合版はバージョンやRakNet／NetherNetなどの通信方式にも依存します。すべての環境で接続できることは保証できません。",
        "linkId": "port",
        "linkLabel": "ポート開放ガイドを見る"
      },
      {
        "title": "クラッシュした/ログが分からない",
        "body": "ログとクラッシュレポートを確認。予期しない停止に備えて、自動再起動も設定できます。",
        "linkId": "trouble",
        "linkLabel": "トラブルシュートを見る"
      },
      {
        "title": "Javaの入れ方がわかりません",
        "body": "アプリから対応するEclipse Temurin JREをダウンロードして設定できます。既存のJavaの検出にも対応。初回取得にはインターネットが必要で、統合版にはJavaは不要です。",
        "linkId": "java",
        "linkLabel": "Java導入ガイドを見る"
      },
      {
        "title": "サーバーファイルはどこ？",
        "body": "既定では AppData 配下。任意の場所にも変更できます。"
      },
      {
        "title": "料金は？",
        "body": "公式サイトの無料版と、BOOTHの100円支援版は同じ機能です。 BOOTHは開発を応援してくださる方向けの任意購入です。まずは無料版でお試しください。どちらも個人・非商用利用向けで、購入によって商用利用が許可されるものではありません。"
      },
      {
        "title": "アップデート方法は？",
        "body": "MaiPilotの「更新を確認」から確認し、表示された最新版インストーラーを実行してください。"
      }
    ],
    boothTitle: "MaiPilotの開発を応援する",
    boothSub: "公式サイトの無料版と、BOOTHの100円支援版は同じ機能です。",
    boothNote: "BOOTHは開発を応援してくださる方向けの任意購入です。まずは無料版でお試しください。どちらも個人・非商用利用向けで、購入によって商用利用が許可されるものではありません。",
    boothCta: "100円で開発を応援する",
    footerDocs: "ドキュメント",
    footerNotices: "サードパーティ通知",
    footerLicense: "ライセンス",
    footerPrivacy: "プライバシーポリシー",
    footerTerms: "利用規約",
    footerX: "公式X",
    footerRight: "ローカル世界のために。",
    footerDisclosure: "本ページにはアフィリエイトリンク（PR）が含まれます。",
    galleryTitle: "日々の操作に、専用の画面を。",
    gallerySub: "サーバー作成、ワールドの管理、対応アドオンの整理をサイドバーから。",
    screenshotPending: "実画面の画像を準備中",
    screenshotSource: "デモデータで描画したMaiPilot 2.2.0の画面例。",
    screenshotPendingNote: "確認済みの現行アプリ画像をここに掲載します。",
    screenshots: {
      "overview": {
        "title": "サーバーの状態をひと目で",
        "caption": "稼働状況、設定、よく使う操作をまとめて確認。",
        "alt": "現行Windows版MaiPilotのサーバー概要画面"
      },
      "create": {
        "title": "Java版か、統合版か。ここから選ぶ",
        "caption": "遊びたいエディションとサーバー種別から準備を始めます。",
        "alt": "MaiPilotの新しいサーバー作成・エディション選択画面"
      },
      "addons": {
        "title": "Javaサーバーを、自分たち好みに",
        "caption": "対応する種別のMODやプラグインを画面から管理。",
        "alt": "対応するJavaサーバーでのMaiPilotアドオン管理画面"
      },
      "backups": {
        "title": "戻せる備えを、手元に",
        "caption": "ワールドのバックアップ作成・定期実行・一覧管理。",
        "alt": "MaiPilotのバックアップ管理画面"
      },
      "worlds": {
        "title": "ワールドを整理する",
        "caption": "保存したワールドを専用画面から管理します。",
        "alt": "MaiPilotのワールド管理画面"
      }
    },
  },
} as const;

type Locale = keyof typeof translations;

const currentLang = ref<Locale>("ja");
const t = computed(() => translations[currentLang.value]);
const runtimeConfig = useRuntimeConfig();
const version = runtimeConfig.public.appVersion as string;

type PrItem = {
  kind: "banner" | "link";
  title: string;
  description?: string;
  cta: string;
  href: string;
  textHref?: string;
  imageSrc?: string;
  imageAlt?: string;
  eventName?: string;
  textEventName?: string;
};

const siteUrl = computed(() => {
  const raw = runtimeConfig.public.siteUrl as string | undefined;
  if (!raw) {
    return "";
  }
  return raw.endsWith("/") ? raw.slice(0, -1) : raw;
});
const downloadUrl = computed(() => {
  const raw = runtimeConfig.public.downloadUrl as string | undefined;
  return raw && raw.trim().length > 0 ? raw : defaultDownloadPath;
});
const gaMeasurementId = computed(() => {
  const raw = runtimeConfig.public.gaMeasurementId as string | undefined;
  return raw?.trim() ?? "";
});
const downloadOptionLinks = computed<Record<string, string>>(() => ({
  local: downloadUrl.value,
  lan: docsLanUrl,
  hosting: docsHostingUrl,
}));
const defaultPrItems = computed<PrItem[]>(() => t.value.prItems as unknown as PrItem[]);
const normalizePrItem = (value: unknown): PrItem | null => {
  if (typeof value !== "object" || value === null) {
    return null;
  }
  const obj = value as Record<string, unknown>;
  const href = typeof obj.href === "string" ? obj.href.trim() : "";
  const title = typeof obj.title === "string" ? obj.title.trim() : "";
  const cta = typeof obj.cta === "string" ? obj.cta.trim() : "";
  if (!href || !title || !cta) {
    return null;
  }
  const kind = obj.kind === "banner" ? "banner" : "link";
  return {
    kind,
    title,
    cta,
    href,
    textHref: typeof obj.textHref === "string" ? obj.textHref.trim() : undefined,
    description: typeof obj.description === "string" ? obj.description.trim() : undefined,
    imageSrc: typeof obj.imageSrc === "string" ? obj.imageSrc.trim() : undefined,
    imageAlt: typeof obj.imageAlt === "string" ? obj.imageAlt.trim() : undefined,
    eventName: typeof obj.eventName === "string" ? obj.eventName.trim() : undefined,
    textEventName: typeof obj.textEventName === "string" ? obj.textEventName.trim() : undefined,
  };
};
const prItems = computed<PrItem[]>(() => {
  const raw = runtimeConfig.public.prItemsJson as string | undefined;
  if (!raw || raw.trim().length === 0) {
    return defaultPrItems.value;
  }
  try {
    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) {
      return defaultPrItems.value;
    }
    const normalized = parsed
      .map((item) => normalizePrItem(item))
      .filter((item): item is PrItem => item !== null);
    return normalized.length > 0 ? normalized : defaultPrItems.value;
  } catch {
    return defaultPrItems.value;
  }
});
const isExternalUrl = (href: string) => /^https?:\/\//i.test(href);

const setLang = (lang: Locale) => {
  currentLang.value = lang;
  if (process.client) {
    try { localStorage.setItem("mcsm-lang", lang); } catch { /* Storage may be disabled. */ }
  }
};

onMounted(() => {
  if (!process.client) {
    return;
  }
  document.addEventListener("keydown", closeMenuOnEscape);
  isMobileDevice.value = /Android|iPhone|iPad|iPod/i.test(navigator.userAgent) ||
    (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  pcHandoffOpen.value = isMobileDevice.value || window.location.hash === "#pc-handoff";

  // Fallback for environments without IntersectionObserver.
  nextTick(() => {
    const revealTargets = Array.from(document.querySelectorAll<HTMLElement>(".reveal"));
    if (!("IntersectionObserver" in window)) {
      revealTargets.forEach((el) => {
        el.classList.add("visible");
      });
      return;
    }
    revealObserver = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add("visible");
            revealObserver?.unobserve(entry.target);
          }
        });
      },
      { threshold: 0.12 }
    );
    revealTargets.forEach((el) => {
      revealObserver?.observe(el);
    });
  });

  let saved: Locale | null = null;
  try { saved = localStorage.getItem("mcsm-lang") as Locale | null; } catch { /* Storage may be disabled. */ }
  if (saved && translations[saved]) {
    currentLang.value = saved;
    return;
  }
  const browserLang = navigator.language?.toLowerCase() ?? "";
  currentLang.value = browserLang.startsWith("ja") ? "ja" : "en";
});

onBeforeUnmount(() => {
  if (!process.client) {
    return;
  }
  document.removeEventListener("keydown", closeMenuOnEscape);
  revealObserver?.disconnect();
  revealObserver = null;
});

useHead(() => ({
  title: t.value.metaTitle,
  htmlAttrs: { lang: currentLang.value },
  meta: [
    { name: "description", content: t.value.metaDescription },
    { name: "keywords", content: "Minecraft server manager, Minecraft server GUI, Minecraft server Windows, マイクラ サーバー 管理, マイクラ サーバー 立て方, マイクラ サーバー 無料, マイクラ サーバー ポート開放, Paper サーバー, Forge サーバー, マイクラ MOD 導入, MaiPilot" },
    { name: "author", content: "MaiPilot" },
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
    ...(gaMeasurementId.value
      ? [
          {
            key: "maipilot-analytics",
            src: "/analytics.js",
            defer: true,
            "data-measurement-id": gaMeasurementId.value,
          },
        ]
      : []),
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
          "@type": "Organization",
          name: "MaiPilot",
          url: siteUrl.value ? `${siteUrl.value}/` : undefined,
          logo: siteUrl.value ? `${siteUrl.value}/icon.png` : undefined,
          sameAs: ["https://x.com/MaipilotOffical", boothUrl],
          contactPoint: {
            "@type": "ContactPoint",
            contactType: "customer support",
            availableLanguage: "Japanese",
          },
        },
        {
          "@context": "https://schema.org",
          "@type": "SoftwareApplication",
          name: t.value.siteName,
          description: t.value.metaDescription,
          operatingSystem: "Windows 10, Windows 11",
          applicationCategory: "GameApplication",
          softwareVersion: version,
          url: siteUrl.value ? `${siteUrl.value}/` : undefined,
          downloadUrl: siteUrl.value ? `${siteUrl.value}/` : undefined,
          inLanguage: currentLang.value,
          featureList: t.value.features.map((item) => `${item.title}: ${item.body}`),
          license: siteUrl.value ? `${siteUrl.value}/LICENSE.txt` : undefined,
          offers: [
            {
              "@type": "Offer",
              price: "0",
              priceCurrency: "JPY",
              description: t.value.downloadSub,
              url: siteUrl.value ? `${siteUrl.value}/` : undefined,
            },
            {
              "@type": "Offer",
              price: "100",
              priceCurrency: "JPY",
              description: t.value.boothSub,
              url: boothUrl,
            },
          ],
          author: {
            "@type": "Organization",
            name: "MaiPilot",
            url: siteUrl.value ? `${siteUrl.value}/` : undefined,
            sameAs: ["https://x.com/MaipilotOffical", boothUrl],
          },
        },
        {
          "@context": "https://schema.org",
          "@type": "FAQPage",
          mainEntity: t.value.faq.map((item) => ({
            "@type": "Question",
            name: item.title,
            acceptedAnswer: {
              "@type": "Answer",
              text: item.body,
            },
          })),
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
        <img src="/icon.png" alt="MaiPilot" class="brand-icon" />
        <span class="brand-name">MaiPilot</span>
      </div>
      <button
        class="hamburger"
        type="button"
        :class="{ open: mobileMenuOpen }"
        :aria-label="currentLang === 'ja' ? 'メニュー' : 'Menu'"
        :aria-expanded="mobileMenuOpen"
        aria-controls="main-navigation"
        @click="mobileMenuOpen = !mobileMenuOpen"
      >
        <span /><span /><span />
      </button>
      <nav id="main-navigation" class="nav-links" :class="{ 'mobile-open': mobileMenuOpen }">
        <a href="#features" @click="closeMobileMenu">{{ t.nav.features }}</a>
        <a href="#help" @click="closeMobileMenu">{{ t.nav.help }}</a>
        <a href="#how" @click="closeMobileMenu">{{ t.nav.how }}</a>
        <a href="#compat" @click="closeMobileMenu">{{ t.nav.compat }}</a>
        <a href="#security" @click="closeMobileMenu">{{ t.nav.security }}</a>
        <a href="#download" class="nav-cta" data-event="download_section_click" data-cta-location="nav" @click="closeMobileMenu">{{ t.nav.download }}</a>
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
      <div class="mobile-overlay" :class="{ visible: mobileMenuOpen }" @click="closeMobileMenu" />
    </header>

    <main>
      <section class="hero">
        <div class="hero-bg" aria-hidden="true"></div>
        <div class="hero-content">
          <p class="eyebrow">{{ t.eyebrow }}</p>
          <h1 class="hero-title"><template v-if="currentLang === 'ja'"><span v-for="part in t.heroTitleParts" :key="part">{{ part }}</span></template><template v-else>{{ t.heroTitle }}</template></h1>
          <p class="hero-sub">{{ t.heroSub }}</p>
          <ul class="hero-notes">
            <li v-for="note in t.heroNotes" :key="note">{{ note }}</li>
          </ul>
          <div class="hero-actions">
            <a :href="isMobileDevice ? '#pc-handoff' : downloadUrl" class="btn primary"
              :data-event="isMobileDevice ? 'pc_handoff_open' : 'download_click'" data-cta-location="hero"
              @click="openPcHandoff">{{ isMobileDevice ? t.heroMobileCta : t.heroCtaPrimary }}</a>
            <a :href="docsUrl" class="btn ghost" data-event="setup_guide_click" data-cta-location="hero">{{ t.heroCtaSecondary }}</a>
          </div>
          <p class="download-reassurance">{{ t.freeNote }}</p>
          <p class="support-note"><a href="#support">{{ t.supportNote }}</a></p>
          <PcHandoff id="pc-handoff" :locale="currentLang" :open="pcHandoffOpen" @toggle="pcHandoffOpen = $event" />
          <div class="hero-meta">
            <span class="chip">{{ t.heroMetaVersion }} {{ version }}</span>
            <span class="chip">{{ t.heroMetaWindows }}</span>
            <span class="chip">{{ t.heroMetaRuntime }}</span>
          </div>
        </div>
        <ProductScreenshot class="hero-screenshot" :src="screenshots.overview"
          :title="t.screenshots.overview.title" :caption="t.screenshots.overview.caption"
          :alt="t.screenshots.overview.alt" :pending-label="t.screenshotPending"
          :pending-note="t.screenshotPendingNote" :source-note="t.screenshotSource" eager />
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
          <a :href="docsHostingUrl" class="features-affiliate-link" data-event="docs_24_7_hosting_click" data-cta-location="features">
            {{ t.featuresAffiliateCta }}
          </a>
        </div>
      </section>

      <section class="section reveal" aria-labelledby="gallery-title">
        <div class="section-head">
          <h2 id="gallery-title">{{ t.galleryTitle }}</h2>
          <p>{{ t.gallerySub }}</p>
        </div>
        <div class="product-gallery">
          <ProductScreenshot v-for="key in featureShots" :key="key" :src="screenshots[key]"
            :title="t.screenshots[key].title" :caption="t.screenshots[key].caption"
            :alt="t.screenshots[key].alt" :pending-label="t.screenshotPending"
            :pending-note="t.screenshotPendingNote" :source-note="t.screenshotSource" />
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
              <span class="step-body">{{ step.body }}</span>
            </li>
          </ol>
        </div>
        <ProductScreenshot :src="screenshots.create"
          :title="t.screenshots.create.title" :caption="t.screenshots.create.caption"
          :alt="t.screenshots.create.alt" :pending-label="t.screenshotPending"
          :pending-note="t.screenshotPendingNote" :source-note="t.screenshotSource" />
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
            <p class="download-reassurance">{{ t.freeNote }}</p>
          </div>
          <div class="callout-actions">
            <a :href="isMobileDevice ? '#pc-handoff' : downloadUrl" class="btn primary"
              :data-event="isMobileDevice ? 'pc_handoff_open' : 'download_click'" data-cta-location="download"
              @click="openPcHandoff">{{ isMobileDevice ? t.heroMobileCta : t.downloadPrimary }}</a>
            <a :href="docsUrl" class="btn ghost" data-event="setup_guide_click" data-cta-location="download">{{ t.downloadSecondary }}</a>
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
                :href="isMobileDevice && option.id === 'local' ? '#pc-handoff' : downloadOptionLinks[option.id]"
                class="btn ghost"
                :data-event="isMobileDevice && option.id === 'local' ? 'pc_handoff_open' : downloadOptionEvents[option.id]"
                data-cta-location="options"
                @click="option.id === 'local' && openPcHandoff()"
              >
                {{ isMobileDevice && option.id === 'local' ? t.heroMobileCta : option.cta }}
              </a>
            </div>
          </div>
        </div>
      </section>

      <section id="support" class="section reveal">
        <div class="callout booth-callout">
          <div class="booth-text">
            <h2>{{ t.boothTitle }}</h2>
            <p>{{ t.boothSub }}</p>
            <p class="booth-note">{{ t.boothNote }}</p>
          </div>
          <div class="callout-actions">
            <a
              :href="boothUrl"
              class="btn ghost"
              target="_blank"
              rel="noopener noreferrer"
              data-event="booth_click"
              data-cta-location="booth"
            >{{ t.boothCta }}</a>
          </div>
        </div>
      </section>

      <section class="section reveal">
        <div class="section-head">
          <h2>{{ t.prSectionTitle }}</h2>
          <p>{{ t.prSectionSub }}</p>
        </div>
        <div class="grid pr-grid">
          <article
            v-for="item in prItems"
            :key="`${item.kind}-${item.href}-${item.title}`"
            class="panel pr-card"
            :class="item.kind === 'banner' ? 'is-banner' : 'is-link'"
          >
            <div class="pr-link-card">
              <a
                :href="item.href"
                class="pr-media-link"
                data-affiliate="true"
                data-cta-location="sponsors"
                :data-event="item.eventName || 'outbound_affiliate_click'"
                :target="isExternalUrl(item.href) ? '_blank' : null"
                :rel="isExternalUrl(item.href) ? 'sponsored nofollow noopener noreferrer' : 'sponsored nofollow'"
              >
                <img
                  v-if="item.kind === 'banner' && item.imageSrc"
                  :src="item.imageSrc"
                  :alt="item.imageAlt || item.title"
                  class="pr-banner-image"
                  loading="lazy"
                />
                <div v-else class="pr-link-media" aria-hidden="true">
                  <span>{{ t.prTextLinkLabel }}</span>
                </div>
              </a>
              <h3>{{ item.title }}</h3>
              <p v-if="item.description">{{ item.description }}</p>
              <a
                :href="item.textHref || item.href"
                class="pr-cta"
                data-affiliate="true"
                data-cta-location="sponsors"
                :data-event="item.textEventName || item.eventName || 'outbound_affiliate_click'"
                :target="isExternalUrl(item.textHref || item.href) ? '_blank' : null"
                :rel="isExternalUrl(item.textHref || item.href) ? 'sponsored nofollow noopener noreferrer' : 'sponsored nofollow'"
              >
                {{ item.cta }}
              </a>
            </div>
          </article>
        </div>
      </section>

      <section id="guides" class="section reveal">
        <div class="section-head">
          <h2>{{ t.guidesTitle }}</h2>
          <p>{{ t.guidesSub }}</p>
        </div>
        <div class="guides-grid">
          <a
            v-for="guide in t.guides"
            :key="guide.href"
            :href="guide.href"
            class="guide-card"
          >
            {{ guide.title }}
          </a>
        </div>
        <div class="guides-more-wrap">
          <a :href="docsUrl" class="guides-more-link">{{ t.guidesMore }} →</a>
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
              :data-event="item.linkId === 'hosting' ? 'docs_24_7_hosting_click' : 'setup_guide_click'"
              data-cta-location="faq"
            >
              {{ item.linkLabel }}
            </a>
          </div>
        </div>
      </section>
    </main>

    <footer class="footer">
      <div class="footer-top">
        <div class="footer-left">
          <img src="/icon.png" alt="MaiPilot" class="brand-icon small" />
          <span>MaiPilot</span>
        </div>
        <div class="footer-right">{{ t.footerRight }}</div>
      </div>
      <div class="footer-links">
        <a :href="docsUrl">{{ t.footerDocs }}</a>
        <a href="/docs/privacy-policy/">{{ t.footerPrivacy }}</a>
        <a href="/docs/terms/">{{ t.footerTerms }}</a>
        <a href="/THIRD_PARTY_NOTICES.txt">{{ t.footerNotices }}</a>
        <a href="/LICENSE.txt">{{ t.footerLicense }}</a>
        <a href="https://x.com/MaipilotOffical" target="_blank" rel="noopener noreferrer">{{ t.footerX }}</a>
      </div>
      <div class="footer-disclosure">{{ t.footerDisclosure }}</div>
    </footer>
  </div>
</template>
