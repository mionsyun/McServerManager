export default defineNuxtConfig({
  devtools: { enabled: true },
  css: ["~/assets/css/main.css"],
  runtimeConfig: {
    public: {
      siteUrl: process.env.NUXT_PUBLIC_SITE_URL || "https://lp.maipilot.jp"
    }
  },
  app: {
    head: {
      title: "BlockPilot",
      meta: [
        { name: "description", content: "Build and run Minecraft servers fast with a gamer-first WPF tool." },
        { name: "viewport", content: "width=device-width, initial-scale=1" },
        { name: "theme-color", content: "#0b0f17" }
      ],
      link: [
        { rel: "icon", type: "image/png", href: "/icon.png" }
      ]
    }
  }
});

