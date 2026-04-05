<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch, nextTick } from "vue";

const mobileMenuOpen = ref(false);
const closeMobileMenu = () => { mobileMenuOpen.value = false; };

let revealObserver: IntersectionObserver | null = null;

const defaultDownloadPath = "https://stmailpilotje.blob.core.windows.net/public/downloads/MaiPilotSetup.exe";
const docsUrl = "/docs";
const docsLanUrl = "/docs/lan";
const docsHostingUrl = "/docs/24-7-hosting";
const docsPortUrl = "/docs/port-forwarding";
const docsTroubleUrl = "/docs/troubleshooting";
const docsPrivacyUrl = "/docs/privacy-and-network";
const docsJavaUrl = "/docs/java-setup/";
const boothUrl = "https://maipilot.booth.pm";
const version = "1.0.6";

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
    metaDescription:
      "A Windows GUI for Vanilla, Paper, Forge, Fabric, Spigot & Purpur servers. Manage mods, backups, permissions, and network — all from one interface.",
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
    heroTitle: "Every server type. One interface.",
    heroSub:
      "Vanilla, Paper, Forge, Fabric, Spigot, Purpur — set up, mod, back up, and monitor from a single window.",
    heroNotes: [
      "Create, run, and manage six server types on Windows with a full-featured GUI.",
      "Data stays on your PC. Minimal network calls. Admin actions are clearly shown.",
    ],
    heroAffiliateLabel: "Need always-on hosting?",
    heroAffiliateCta: "See hosting options (PR)",
    heroCtaPrimary: "Download",
    heroCtaSecondary: "Read the setup guide",
    heroBoothCta: "Support development (BOOTH)",
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
    cardMods: "Mods",
    cardFirewall: "Firewall",
    featuresTitle: "Everything you need, already built in.",
    featuresSub: "Six server types, mod management, backups, and more — out of the box.",
    features: [
      {
        title: "Six server types",
        body: "Vanilla, Paper, Purpur, Fabric, Forge, and Spigot. Pick one and go.",
      },
      {
        title: "MOD & Plugin manager",
        body: "Add, enable, disable mods/plugins. Search Modrinth directly in the app.",
      },
      {
        title: "Backup, restore & worlds",
        body: "ZIP backups, one-click restore, world switching, and map archive import.",
      },
      {
        title: "Settings GUI",
        body: "Edit server.properties, manage OPs and whitelist — no file editing needed.",
      },
      {
        title: "Network all-in-one",
        body: "Firewall rules, UPnP, public IP, port check, and a guided checklist.",
      },
      {
        title: "Crash recovery & auto-restart",
        body: "One-click access to logs and crash reports. Optional auto-restart when unattended.",
      },
      {
        title: "Live monitoring",
        body: "CPU, RAM, and player count updated every second in real time.",
      },
      {
        title: "Safe apply flow",
        body: "Settings are saved instantly but applied after restart to prevent accidents.",
      },
      {
        title: "Resource pack hosting",
        body: "Serve your resource pack over HTTPS with a built-in HTTP server and Cloudflare Quick Tunnel — no account needed. SHA-1 and server.properties applied automatically.",
      },
    ],
    featuresAffiliateLabel: "Always-on hosting is an option if your PC cannot stay on.",
    featuresAffiliateCta: "Compare hosting options (PR)",
    stepsTitle: "3 steps to your first server",
    steps: [
      {
        title: "Create a server profile (EULA required).",
        body: "Choose name, location, type (Vanilla/Paper/Forge…), and version.",
      },
      {
        title: "Configure with a GUI.",
        body: "Memory, port, Java path, and server.properties — all with clear labels.",
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
        body: "One-click access to logs and crash reports. Enable auto-restart for unattended recovery.",
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
        body: "Full server management with local worlds, mods, and fast backups.",
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
    faqTitle: "FAQ",
    faqSub: "Quick answers for a smooth start.",
    faq: [
      {
        title: "Which server types are supported?",
        body: "Vanilla, Paper, Purpur, Fabric, Forge, and Spigot. Choose when creating a server.",
      },
      {
        title: "Does it work with mods and plugins?",
        body: "Yes. Select a modded server type (Forge, Fabric, Paper, etc.) and use the MOD/Plugin tab to add, manage, or search Modrinth.",
      },
      {
        title: "I cannot keep my PC on 24/7.",
        body: "If you need always-on hosting, see the options.",
        linkId: "hosting",
        linkLabel: "View hosting options (PR)",
      },
      {
        title: "External access is not working.",
        body: "Follow the checklist for ports and routers. On shared networks (dorm/school/apartment internet), upstream restrictions may block port forwarding.",
        linkId: "port",
        linkLabel: "Open port-forwarding guide",
      },
      {
        title: "It crashed and I do not know where logs are.",
        body: "Open logs/crash reports from the Console tab. You can also enable auto-restart for unattended recovery.",
        linkId: "trouble",
        linkLabel: "Open troubleshooting",
      },
      {
        title: "How do I install Java?",
        body: "Download Eclipse Temurin (LTS) from adoptium.net and run the installer. MaiPilot auto-detects your Java path.",
        linkId: "java",
        linkLabel: "Open Java setup guide",
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
        body: "Use \"Check for app updates\" in MaiPilot, then run the latest installer shown in the prompt.",
      },
    ],
    boothTitle: "Support MaiPilot's development",
    boothSub: "MaiPilot is free to download from the official site. There is no difference in features.",
    boothNote: "The BOOTH listing is for those who want to support ongoing development. Proceeds go toward server maintenance and new features. Try the free version first — support only if you find it useful.",
    boothCta: "View on BOOTH",
    footerDocs: "Docs",
    footerNotices: "Third-party notices",
    footerLicense: "License",
    footerPrivacy: "Privacy Policy",
    footerTerms: "Terms of Use",
    footerX: "Official X",
    footerRight: "Built for local worlds.",
    footerDisclosure: "This page includes affiliate links (PR).",
  },
  ja: {
    metaTitle: "MaiPilot | Windows向けマイクラサーバー統合管理",
    metaDescription:
      "Vanilla・Paper・Forge・Fabric・Spigot・Purpurに対応。MOD管理・バックアップ・監視を一画面で。Windows用GUI。",
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
    heroTitle: "6種のサーバーを、ひとつの画面で。",
    heroSub:
      "Vanilla・Paper・Forge・Fabric・Spigot・Purpur — セットアップからMOD管理・監視まで。",
    heroNotes: [
      "6種のサーバーを作成・運用・管理できるWindows用フルGUI",
      "データはPC内。必要最小限の通信のみ。権限が必要な操作は明示します",
    ],
    heroAffiliateLabel: "24時間運用の選択肢もあります",
    heroAffiliateCta: "VPSの比較を見る（PR）",
    heroCtaPrimary: "ダウンロード",
    heroCtaSecondary: "セットアップガイド",
    heroBoothCta: "開発を応援する（BOOTH）",
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
    cardMods: "MOD",
    cardFirewall: "ファイアウォール",
    featuresTitle: "必要な機能、すべて標準搭載。",
    featuresSub: "6種のサーバー対応、MOD管理、バックアップ — はじめから揃っています。",
    features: [
      {
        title: "6種のサーバー対応",
        body: "Vanilla・Paper・Purpur・Fabric・Forge・Spigotから選んですぐ開始。",
      },
      {
        title: "MOD/プラグイン管理",
        body: "追加・有効化・無効化をGUIで。Modrinth検索も内蔵。",
      },
      {
        title: "バックアップ/復元/ワールド管理",
        body: "ZIPバックアップ・ワンクリック復元・ワールド切替・配布マップ導入。",
      },
      {
        title: "設定GUI",
        body: "server.propertiesの編集、OP/ホワイトリスト管理をGUIで完結。",
      },
      {
        title: "ネットワーク一括管理",
        body: "Firewall・UPnP・公開IP・ポートチェック・チェックリストを一画面に。",
      },
      {
        title: "クラッシュ復旧/自動再起動",
        body: "ログ/クラッシュレポートへワンクリック。自動再起動も設定可能。",
      },
      {
        title: "リアルタイム監視",
        body: "CPU・RAM・プレイヤー数を毎秒リアルタイム更新。",
      },
      {
        title: "安全な反映フロー",
        body: "変更は保存できるが反映は再起動後。事故を防ぎます。",
      },
      {
        title: "リソースパック配布",
        body: "内蔵HTTPサーバーとCloudflare Quick TunnelでHTTPS配信。アカウント不要・無料。SHA-1計算とserver.properties書き込みも自動。",
      },
    ],
    featuresAffiliateLabel: "PCをつけっぱなしにできない場合は、VPSという選択肢もあります。",
    featuresAffiliateCta: "24時間運用の比較を見る（PR）",
    stepsTitle: "起動まで、たった 3 ステップ",
    steps: [
      {
        title: "サーバープロファイル作成（EULA同意必須）",
        body: "名前・保存先・種別（Vanilla/Paper/Forge…）・バージョンを指定。",
      },
      {
        title: "GUIで設定",
        body: "メモリ・ポート・Javaパス・server.propertiesを分かりやすく設定。",
      },
      {
        title: "起動前チェック",
        body: "Java互換・公開チェックで不安を減らす。",
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
        body: "ログ/クラッシュレポートへ即アクセス。自動再起動も設定可能です。",
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
        body: "6種のサーバーをまるごと管理。MOD・バックアップ・監視まで。",
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
    faqTitle: "よくある質問",
    faqSub: "スムーズに始めるためのヒント。",
    faq: [
      {
        title: "対応サーバー種別は？",
        body: "Vanilla・Paper・Purpur・Fabric・Forge・Spigotの6種です。作成時に選べます。",
      },
      {
        title: "MOD/プラグインは使えますか？",
        body: "はい。Forge・Fabric・Paper等を選んで、MOD/プラグインタブで追加・管理できます。Modrinth検索も内蔵しています。",
      },
      {
        title: "PCをつけっぱなしにできません",
        body: "常時稼働が必要な場合はVPSの選択肢があります。",
        linkId: "hosting",
        linkLabel: "24時間運用の選択肢を見る（PR）",
      },
      {
        title: "外部公開ができません",
        body: "ポート開放チェックリストを確認してください。共有回線（寮・学校・マンション一括回線など）は上位側の制限で失敗する場合があります。",
        linkId: "port",
        linkLabel: "ポート開放ガイドを見る",
      },
      {
        title: "クラッシュした/ログが分からない",
        body: "コンソールからログ/クラッシュレポートを開けます。自動再起動を有効にすると無人復旧も可能です。",
        linkId: "trouble",
        linkLabel: "トラブルシュートを見る",
      },
      {
        title: "Javaの入れ方がわかりません",
        body: "adoptium.net から Eclipse Temurin (LTS) をインストールするだけ。MaiPilotが自動検出します。",
        linkId: "java",
        linkLabel: "Java導入ガイドを見る",
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
        body: "MaiPilotの「アプリ更新を確認」から確認し、表示された最新版インストーラーを実行してください。",
      },
    ],
    boothTitle: "MaiPilotの開発を応援する",
    boothSub: "MaiPilotは公式サイトから無料でダウンロードできます。機能の違いはありません。",
    boothNote: "BOOTH版は、開発を続けていくための応援をしてくださる方向けです。いただいた売上はサーバー維持費や新機能の開発に充てます。まずは無料版を試してみて、「便利だな」と感じたら応援いただけると嬉しいです。",
    boothCta: "BOOTHで応援する",
    footerDocs: "ドキュメント",
    footerNotices: "サードパーティ通知",
    footerLicense: "ライセンス",
    footerPrivacy: "プライバシーポリシー",
    footerTerms: "利用規約",
    footerX: "公式X",
    footerRight: "ローカル世界のために。",
    footerDisclosure: "本ページにはアフィリエイトリンク（PR）が含まれます。",
  },
} as const;

type Locale = keyof typeof translations;

const currentLang = ref<Locale>("en");
const t = computed(() => translations[currentLang.value]);
const runtimeConfig = useRuntimeConfig();

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
const gaInitScript = computed(() => {
  if (!gaMeasurementId.value) {
    return "";
  }
  return [
    "window.dataLayer = window.dataLayer || [];",
    "function gtag(){dataLayer.push(arguments);}",
    "gtag('js', new Date());",
    `gtag('config', ${JSON.stringify(gaMeasurementId.value)});`,
  ].join("\n");
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
  revealObserver?.disconnect();
  revealObserver = null;
});

useHead(() => ({
  title: t.value.metaTitle,
  htmlAttrs: { lang: currentLang.value },
  meta: [
    { name: "description", content: t.value.metaDescription },
    { name: "keywords", content: "Minecraft server manager, Minecraft server GUI, Minecraft server Windows, マイクラ サーバー 管理, マイクラ サーバー 立て方" },
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
            src: `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(gaMeasurementId.value)}`,
            async: true,
          },
          {
            key: "ga4-init",
            children: gaInitScript.value,
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
          "@type": "SoftwareApplication",
          name: t.value.siteName,
          description: t.value.metaDescription,
          operatingSystem: "Windows 10, Windows 11",
          applicationCategory: "GameApplication",
          softwareVersion: version,
          url: siteUrl.value ? `${siteUrl.value}/` : undefined,
          downloadUrl: siteUrl.value ? `${siteUrl.value}/` : undefined,
          inLanguage: "ja",
          featureList: [
            "完全日本語対応",
            "サーバーの起動・停止をGUIで操作",
            "複数サーバーの同時管理",
            "自動バックアップ・スケジューラ",
            "ポート開放機能",
            "バニラ・Spigot/Paper・Forge/Fabric対応",
          ],
          offers: [
            {
              "@type": "Offer",
              price: "0",
              priceCurrency: "JPY",
              description: "公式サイトから無料ダウンロード",
              url: siteUrl.value ? `${siteUrl.value}/` : undefined,
            },
            {
              "@type": "Offer",
              price: "980",
              priceCurrency: "JPY",
              description: "BOOTH 応援購入版（内容は無料版と同じ）",
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
        aria-label="Menu"
        @click="mobileMenuOpen = !mobileMenuOpen"
      >
        <span /><span /><span />
      </button>
      <nav class="nav-links" :class="{ 'mobile-open': mobileMenuOpen }">
        <a href="#features" @click="closeMobileMenu">{{ t.nav.features }}</a>
        <a href="#help" @click="closeMobileMenu">{{ t.nav.help }}</a>
        <a href="#how" @click="closeMobileMenu">{{ t.nav.how }}</a>
        <a href="#compat" @click="closeMobileMenu">{{ t.nav.compat }}</a>
        <a href="#security" @click="closeMobileMenu">{{ t.nav.security }}</a>
        <a href="#download" class="nav-cta" @click="closeMobileMenu">{{ t.nav.download }}</a>
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
            <a :href="boothUrl" target="_blank" rel="noopener noreferrer" class="btn booth" data-event="booth_click">{{ t.heroBoothCta }}</a>
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
              <button class="btn tiny ghost" type="button">{{ t.cardMods }}</button>
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
              <span class="step-body">{{ step.body }}</span>
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
            <a :href="boothUrl" target="_blank" rel="noopener noreferrer" class="btn booth" data-event="booth_click">{{ t.downloadBoothCta }}</a>
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
        <div class="callout booth-callout">
          <div class="booth-text">
            <h2>{{ t.boothTitle }}</h2>
            <p>{{ t.boothSub }}</p>
            <p class="booth-note">{{ t.boothNote }}</p>
          </div>
          <div class="callout-actions">
            <a
              href="https://booth.pm/ja/items/8118402"
              class="btn ghost"
              target="_blank"
              rel="noopener noreferrer"
              data-event="booth_click"
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
