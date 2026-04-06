export default defineNuxtConfig({
  devtools: { enabled: false },
  css: ["~/assets/css/main.css"],

  runtimeConfig: {
    public: {
      siteUrl: process.env.NUXT_PUBLIC_SITE_URL || "https://www.maipilot.jp",
      downloadUrl:
        process.env.NUXT_PUBLIC_DOWNLOAD_URL ||
        "https://stmailpilotje.blob.core.windows.net/public/downloads/MaiPilotSetup.exe",
      gaMeasurementId: process.env.NUXT_PUBLIC_GA_MEASUREMENT_ID || "G-MM106D2B2Z",
      prItemsJson: process.env.NUXT_PUBLIC_PR_ITEMS_JSON || ""
    }
  },

  app: {
    head: {
      title: "MaiPilot",
      meta: [
        { name: "description", content: "MaiPilotは日本語対応のWindowsマイクラサーバー管理ツール。ポート開放・MOD管理・自動バックアップをGUIで簡単操作。Vanilla・Paper・Forge対応。無料ダウンロード。" },
        { name: "viewport", content: "width=device-width, initial-scale=1" },
        { name: "theme-color", content: "#0b0f17" }
      ],
      link: [
        { rel: "icon", type: "image/png", href: "/icon.png" }
      ]
    }
  },

  compatibilityDate: "2026-02-18"
});
