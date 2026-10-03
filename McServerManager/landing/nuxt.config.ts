import updateManifest from "./public/updates/win-x64/update.json";

export default defineNuxtConfig({
  devtools: { enabled: false },
  css: ["~/assets/css/main.css", "~/assets/css/product-shots.css"],

  runtimeConfig: {
    public: {
      siteUrl: process.env.NUXT_PUBLIC_SITE_URL || "https://www.maipilot.jp",
      downloadUrl:
        process.env.NUXT_PUBLIC_DOWNLOAD_URL ||
        "https://stmaipilot.blob.core.windows.net/public/downloads/MaiPilotSetup.exe",
      appVersion: process.env.NUXT_PUBLIC_APP_VERSION || updateManifest.version,
      gaMeasurementId: process.env.NUXT_PUBLIC_GA_MEASUREMENT_ID || "G-MM106D2B2Z",
      prItemsJson: process.env.NUXT_PUBLIC_PR_ITEMS_JSON || ""
    }
  },

  app: {
    head: {
      title: "MaiPilot",
      meta: [
        { name: "description", content: "Java版・統合版のマイクラサーバーをWindowsで管理。Java自動セットアップ、対応MOD・プラグイン管理、ワールドのバックアップをひとつのアプリで。個人・非商用利用は無料。" },
        { name: "viewport", content: "width=device-width, initial-scale=1" },
        { name: "theme-color", content: "#0b0f17" }
      ],
      link: [
        { rel: "icon", type: "image/png", href: "/icon.png" },
        { rel: "preconnect", href: "https://fonts.googleapis.com" },
        { rel: "preconnect", href: "https://fonts.gstatic.com", crossorigin: "" }
      ]
    }
  },

  compatibilityDate: "2026-02-18"
});
