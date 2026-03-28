export default defineNuxtConfig({
  devtools: { enabled: process.env.NODE_ENV === "development" },
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
        { name: "description", content: "Build and run Minecraft servers fast with a gamer-first WPF tool." },
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
