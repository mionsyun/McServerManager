import { shallowReactive, reactive, effectScope, getCurrentScope, hasInjectionContext, getCurrentInstance, inject, toRef, computed, defineComponent, h, isReadonly, isRef, isShallow, isReactive, toRaw, ref, mergeProps, useSSRContext, defineAsyncComponent, unref, provide, onErrorCaptured, onServerPrefetch, createVNode, resolveDynamicComponent, createApp } from "vue";
import { $fetch } from "C:/Users/sumiy/Downloads/minecraft/McServerManager/landing/node_modules/ofetch/dist/node.mjs";
import { baseURL, publicAssetsURL } from "#internal/nuxt/paths";
import { createHooks } from "C:/Users/sumiy/Downloads/minecraft/McServerManager/landing/node_modules/hookable/dist/index.mjs";
import { getContext } from "C:/Users/sumiy/Downloads/minecraft/McServerManager/landing/node_modules/unctx/dist/index.mjs";
import { sanitizeStatusCode, createError as createError$1 } from "C:/Users/sumiy/Downloads/minecraft/McServerManager/landing/node_modules/h3/dist/index.mjs";
import { hasProtocol, joinURL, withQuery, isScriptProtocol, isEqual, stringifyParsedURL, stringifyQuery, parseQuery } from "C:/Users/sumiy/Downloads/minecraft/McServerManager/landing/node_modules/ufo/dist/index.mjs";
import { toRouteMatcher, createRouter } from "C:/Users/sumiy/Downloads/minecraft/McServerManager/landing/node_modules/radix3/dist/index.mjs";
import { defu } from "C:/Users/sumiy/Downloads/minecraft/McServerManager/landing/node_modules/defu/dist/defu.mjs";
import { ssrRenderAttrs, ssrRenderAttr, ssrInterpolate, ssrRenderClass, ssrRenderList, ssrRenderStyle, ssrRenderComponent, ssrRenderSuspense, ssrRenderVNode } from "vue/server-renderer";
import { useHead as useHead$1, headSymbol } from "C:/Users/sumiy/Downloads/minecraft/McServerManager/landing/node_modules/@unhead/vue/dist/index.mjs";
if (!globalThis.$fetch) {
  globalThis.$fetch = $fetch.create({
    baseURL: baseURL()
  });
}
if (!("global" in globalThis)) {
  globalThis.global = globalThis;
}
const nuxtLinkDefaults = { "componentName": "NuxtLink" };
const nuxtDefaultErrorValue = null;
const appId = "nuxt-app";
function getNuxtAppCtx(id = appId) {
  return getContext(id, {
    asyncContext: false
  });
}
const NuxtPluginIndicator = "__nuxt_plugin";
function createNuxtApp(options) {
  let hydratingCount = 0;
  const nuxtApp = {
    _id: options.id || appId || "nuxt-app",
    _scope: effectScope(),
    provide: void 0,
    globalName: "nuxt",
    versions: {
      get nuxt() {
        return "3.20.2";
      },
      get vue() {
        return nuxtApp.vueApp.version;
      }
    },
    payload: shallowReactive({
      ...options.ssrContext?.payload || {},
      data: shallowReactive({}),
      state: reactive({}),
      once: /* @__PURE__ */ new Set(),
      _errors: shallowReactive({})
    }),
    static: {
      data: {}
    },
    runWithContext(fn) {
      if (nuxtApp._scope.active && !getCurrentScope()) {
        return nuxtApp._scope.run(() => callWithNuxt(nuxtApp, fn));
      }
      return callWithNuxt(nuxtApp, fn);
    },
    isHydrating: false,
    deferHydration() {
      if (!nuxtApp.isHydrating) {
        return () => {
        };
      }
      hydratingCount++;
      let called = false;
      return () => {
        if (called) {
          return;
        }
        called = true;
        hydratingCount--;
        if (hydratingCount === 0) {
          nuxtApp.isHydrating = false;
          return nuxtApp.callHook("app:suspense:resolve");
        }
      };
    },
    _asyncDataPromises: {},
    _asyncData: shallowReactive({}),
    _payloadRevivers: {},
    ...options
  };
  {
    nuxtApp.payload.serverRendered = true;
  }
  if (nuxtApp.ssrContext) {
    nuxtApp.payload.path = nuxtApp.ssrContext.url;
    nuxtApp.ssrContext.nuxt = nuxtApp;
    nuxtApp.ssrContext.payload = nuxtApp.payload;
    nuxtApp.ssrContext.config = {
      public: nuxtApp.ssrContext.runtimeConfig.public,
      app: nuxtApp.ssrContext.runtimeConfig.app
    };
  }
  nuxtApp.hooks = createHooks();
  nuxtApp.hook = nuxtApp.hooks.hook;
  {
    const contextCaller = async function(hooks, args) {
      for (const hook of hooks) {
        await nuxtApp.runWithContext(() => hook(...args));
      }
    };
    nuxtApp.hooks.callHook = (name, ...args) => nuxtApp.hooks.callHookWith(contextCaller, name, ...args);
  }
  nuxtApp.callHook = nuxtApp.hooks.callHook;
  nuxtApp.provide = (name, value) => {
    const $name = "$" + name;
    defineGetter(nuxtApp, $name, value);
    defineGetter(nuxtApp.vueApp.config.globalProperties, $name, value);
  };
  defineGetter(nuxtApp.vueApp, "$nuxt", nuxtApp);
  defineGetter(nuxtApp.vueApp.config.globalProperties, "$nuxt", nuxtApp);
  const runtimeConfig = options.ssrContext.runtimeConfig;
  nuxtApp.provide("config", runtimeConfig);
  return nuxtApp;
}
function registerPluginHooks(nuxtApp, plugin) {
  if (plugin.hooks) {
    nuxtApp.hooks.addHooks(plugin.hooks);
  }
}
async function applyPlugin(nuxtApp, plugin) {
  if (typeof plugin === "function") {
    const { provide: provide2 } = await nuxtApp.runWithContext(() => plugin(nuxtApp)) || {};
    if (provide2 && typeof provide2 === "object") {
      for (const key in provide2) {
        nuxtApp.provide(key, provide2[key]);
      }
    }
  }
}
async function applyPlugins(nuxtApp, plugins2) {
  const resolvedPlugins = /* @__PURE__ */ new Set();
  const unresolvedPlugins = [];
  const parallels = [];
  let error = void 0;
  let promiseDepth = 0;
  async function executePlugin(plugin) {
    const unresolvedPluginsForThisPlugin = plugin.dependsOn?.filter((name) => plugins2.some((p) => p._name === name) && !resolvedPlugins.has(name)) ?? [];
    if (unresolvedPluginsForThisPlugin.length > 0) {
      unresolvedPlugins.push([new Set(unresolvedPluginsForThisPlugin), plugin]);
    } else {
      const promise = applyPlugin(nuxtApp, plugin).then(async () => {
        if (plugin._name) {
          resolvedPlugins.add(plugin._name);
          await Promise.all(unresolvedPlugins.map(async ([dependsOn, unexecutedPlugin]) => {
            if (dependsOn.has(plugin._name)) {
              dependsOn.delete(plugin._name);
              if (dependsOn.size === 0) {
                promiseDepth++;
                await executePlugin(unexecutedPlugin);
              }
            }
          }));
        }
      }).catch((e) => {
        if (!plugin.parallel && !nuxtApp.payload.error) {
          throw e;
        }
        error ||= e;
      });
      if (plugin.parallel) {
        parallels.push(promise);
      } else {
        await promise;
      }
    }
  }
  for (const plugin of plugins2) {
    if (nuxtApp.ssrContext?.islandContext && plugin.env?.islands === false) {
      continue;
    }
    registerPluginHooks(nuxtApp, plugin);
  }
  for (const plugin of plugins2) {
    if (nuxtApp.ssrContext?.islandContext && plugin.env?.islands === false) {
      continue;
    }
    await executePlugin(plugin);
  }
  await Promise.all(parallels);
  if (promiseDepth) {
    for (let i = 0; i < promiseDepth; i++) {
      await Promise.all(parallels);
    }
  }
  if (error) {
    throw nuxtApp.payload.error || error;
  }
}
// @__NO_SIDE_EFFECTS__
function defineNuxtPlugin(plugin) {
  if (typeof plugin === "function") {
    return plugin;
  }
  const _name = plugin._name || plugin.name;
  delete plugin.name;
  return Object.assign(plugin.setup || (() => {
  }), plugin, { [NuxtPluginIndicator]: true, _name });
}
function callWithNuxt(nuxt, setup, args) {
  const fn = () => setup();
  const nuxtAppCtx = getNuxtAppCtx(nuxt._id);
  {
    return nuxt.vueApp.runWithContext(() => nuxtAppCtx.callAsync(nuxt, fn));
  }
}
function tryUseNuxtApp(id) {
  let nuxtAppInstance;
  if (hasInjectionContext()) {
    nuxtAppInstance = getCurrentInstance()?.appContext.app.$nuxt;
  }
  nuxtAppInstance ||= getNuxtAppCtx(id).tryUse();
  return nuxtAppInstance || null;
}
function useNuxtApp(id) {
  const nuxtAppInstance = tryUseNuxtApp(id);
  if (!nuxtAppInstance) {
    {
      throw new Error("[nuxt] instance unavailable");
    }
  }
  return nuxtAppInstance;
}
// @__NO_SIDE_EFFECTS__
function useRuntimeConfig(_event) {
  return useNuxtApp().$config;
}
function defineGetter(obj, key, val) {
  Object.defineProperty(obj, key, { get: () => val });
}
const PageRouteSymbol = /* @__PURE__ */ Symbol("route");
import.meta.url.replace(/\/app\/.*$/, "/");
const useRouter = () => {
  return useNuxtApp()?.$router;
};
const useRoute = () => {
  if (hasInjectionContext()) {
    return inject(PageRouteSymbol, useNuxtApp()._route);
  }
  return useNuxtApp()._route;
};
// @__NO_SIDE_EFFECTS__
function defineNuxtRouteMiddleware(middleware) {
  return middleware;
}
const isProcessingMiddleware = () => {
  try {
    if (useNuxtApp()._processingMiddleware) {
      return true;
    }
  } catch {
    return false;
  }
  return false;
};
const URL_QUOTE_RE = /"/g;
const navigateTo = (to, options) => {
  to ||= "/";
  const toPath = typeof to === "string" ? to : "path" in to ? resolveRouteObject(to) : useRouter().resolve(to).href;
  const isExternalHost = hasProtocol(toPath, { acceptRelative: true });
  const isExternal = options?.external || isExternalHost;
  if (isExternal) {
    if (!options?.external) {
      throw new Error("Navigating to an external URL is not allowed by default. Use `navigateTo(url, { external: true })`.");
    }
    const { protocol } = new URL(toPath, "http://localhost");
    if (protocol && isScriptProtocol(protocol)) {
      throw new Error(`Cannot navigate to a URL with '${protocol}' protocol.`);
    }
  }
  const inMiddleware = isProcessingMiddleware();
  const router = useRouter();
  const nuxtApp = useNuxtApp();
  {
    if (nuxtApp.ssrContext) {
      const fullPath = typeof to === "string" || isExternal ? toPath : router.resolve(to).fullPath || "/";
      const location2 = isExternal ? toPath : joinURL((/* @__PURE__ */ useRuntimeConfig()).app.baseURL, fullPath);
      const redirect = async function(response) {
        await nuxtApp.callHook("app:redirected");
        const encodedLoc = location2.replace(URL_QUOTE_RE, "%22");
        const encodedHeader = encodeURL(location2, isExternalHost);
        nuxtApp.ssrContext._renderResponse = {
          statusCode: sanitizeStatusCode(options?.redirectCode || 302, 302),
          body: `<!DOCTYPE html><html><head><meta http-equiv="refresh" content="0; url=${encodedLoc}"></head></html>`,
          headers: { location: encodedHeader }
        };
        return response;
      };
      if (!isExternal && inMiddleware) {
        router.afterEach((final) => final.fullPath === fullPath ? redirect(false) : void 0);
        return to;
      }
      return redirect(!inMiddleware ? void 0 : (
        /* abort route navigation */
        false
      ));
    }
  }
  if (isExternal) {
    nuxtApp._scope.stop();
    if (options?.replace) {
      (void 0).replace(toPath);
    } else {
      (void 0).href = toPath;
    }
    if (inMiddleware) {
      if (!nuxtApp.isHydrating) {
        return false;
      }
      return new Promise(() => {
      });
    }
    return Promise.resolve();
  }
  return options?.replace ? router.replace(to) : router.push(to);
};
function resolveRouteObject(to) {
  return withQuery(to.path || "", to.query || {}) + (to.hash || "");
}
function encodeURL(location2, isExternalHost = false) {
  const url = new URL(location2, "http://localhost");
  if (!isExternalHost) {
    return url.pathname + url.search + url.hash;
  }
  if (location2.startsWith("//")) {
    return url.toString().replace(url.protocol, "");
  }
  return url.toString();
}
const NUXT_ERROR_SIGNATURE = "__nuxt_error";
const useError = /* @__NO_SIDE_EFFECTS__ */ () => toRef(useNuxtApp().payload, "error");
const showError = (error) => {
  const nuxtError = createError(error);
  try {
    const error2 = /* @__PURE__ */ useError();
    if (false) ;
    error2.value ||= nuxtError;
  } catch {
    throw nuxtError;
  }
  return nuxtError;
};
const clearError = async (options = {}) => {
  const nuxtApp = useNuxtApp();
  const error = /* @__PURE__ */ useError();
  nuxtApp.callHook("app:error:cleared", options);
  if (options.redirect) {
    await useRouter().replace(options.redirect);
  }
  error.value = nuxtDefaultErrorValue;
};
const isNuxtError = (error) => !!error && typeof error === "object" && NUXT_ERROR_SIGNATURE in error;
const createError = (error) => {
  const nuxtError = createError$1(error);
  Object.defineProperty(nuxtError, NUXT_ERROR_SIGNATURE, {
    value: true,
    configurable: false,
    writable: false
  });
  return nuxtError;
};
const unhead_k2P3m_ZDyjlr2mMYnoDPwavjsDN8hBlk9cFai0bbopU = /* @__PURE__ */ defineNuxtPlugin({
  name: "nuxt:head",
  enforce: "pre",
  setup(nuxtApp) {
    const head = nuxtApp.ssrContext.head;
    nuxtApp.vueApp.use(head);
  }
});
async function getRouteRules(arg) {
  const path = typeof arg === "string" ? arg : arg.path;
  {
    useNuxtApp().ssrContext._preloadManifest = true;
    const _routeRulesMatcher = toRouteMatcher(
      createRouter({ routes: (/* @__PURE__ */ useRuntimeConfig()).nitro.routeRules })
    );
    return defu({}, ..._routeRulesMatcher.matchAll(path).reverse());
  }
}
const manifest_45route_45rule = /* @__PURE__ */ defineNuxtRouteMiddleware(async (to) => {
  {
    return;
  }
});
const globalMiddleware = [
  manifest_45route_45rule
];
function getRouteFromPath(fullPath) {
  const route = fullPath && typeof fullPath === "object" ? fullPath : {};
  if (typeof fullPath === "object") {
    fullPath = stringifyParsedURL({
      pathname: fullPath.path || "",
      search: stringifyQuery(fullPath.query || {}),
      hash: fullPath.hash || ""
    });
  }
  const url = new URL(fullPath.toString(), "http://localhost");
  return {
    path: url.pathname,
    fullPath,
    query: parseQuery(url.search),
    hash: url.hash,
    // stub properties for compat with vue-router
    params: route.params || {},
    name: void 0,
    matched: route.matched || [],
    redirectedFrom: void 0,
    meta: route.meta || {},
    href: fullPath
  };
}
const router_DclsWNDeVV7SyG4lslgLnjbQUK1ws8wgf2FHaAbo7Cw = /* @__PURE__ */ defineNuxtPlugin({
  name: "nuxt:router",
  enforce: "pre",
  setup(nuxtApp) {
    const initialURL = nuxtApp.ssrContext.url;
    const routes = [];
    const hooks = {
      "navigate:before": [],
      "resolve:before": [],
      "navigate:after": [],
      "error": []
    };
    const registerHook = (hook, guard) => {
      hooks[hook].push(guard);
      return () => hooks[hook].splice(hooks[hook].indexOf(guard), 1);
    };
    const baseURL2 = (/* @__PURE__ */ useRuntimeConfig()).app.baseURL;
    const route = reactive(getRouteFromPath(initialURL));
    async function handleNavigation(url, replace) {
      try {
        const to = getRouteFromPath(url);
        for (const middleware of hooks["navigate:before"]) {
          const result = await middleware(to, route);
          if (result === false || result instanceof Error) {
            return;
          }
          if (typeof result === "string" && result.length) {
            return handleNavigation(result, true);
          }
        }
        for (const handler of hooks["resolve:before"]) {
          await handler(to, route);
        }
        Object.assign(route, to);
        if (false) ;
        for (const middleware of hooks["navigate:after"]) {
          await middleware(to, route);
        }
      } catch (err) {
        for (const handler of hooks.error) {
          await handler(err);
        }
      }
    }
    const currentRoute = computed(() => route);
    const router = {
      currentRoute,
      isReady: () => Promise.resolve(),
      // These options provide a similar API to vue-router but have no effect
      options: {},
      install: () => Promise.resolve(),
      // Navigation
      push: (url) => handleNavigation(url, false),
      replace: (url) => handleNavigation(url, true),
      back: () => (void 0).history.go(-1),
      go: (delta) => (void 0).history.go(delta),
      forward: () => (void 0).history.go(1),
      // Guards
      beforeResolve: (guard) => registerHook("resolve:before", guard),
      beforeEach: (guard) => registerHook("navigate:before", guard),
      afterEach: (guard) => registerHook("navigate:after", guard),
      onError: (handler) => registerHook("error", handler),
      // Routes
      resolve: getRouteFromPath,
      addRoute: (parentName, route2) => {
        routes.push(route2);
      },
      getRoutes: () => routes,
      hasRoute: (name) => routes.some((route2) => route2.name === name),
      removeRoute: (name) => {
        const index = routes.findIndex((route2) => route2.name === name);
        if (index !== -1) {
          routes.splice(index, 1);
        }
      }
    };
    nuxtApp.vueApp.component("RouterLink", defineComponent({
      functional: true,
      props: {
        to: {
          type: String,
          required: true
        },
        custom: Boolean,
        replace: Boolean,
        // Not implemented
        activeClass: String,
        exactActiveClass: String,
        ariaCurrentValue: String
      },
      setup: (props, { slots }) => {
        const navigate = () => handleNavigation(props.to, props.replace);
        return () => {
          const route2 = router.resolve(props.to);
          return props.custom ? slots.default?.({ href: props.to, navigate, route: route2 }) : h("a", { href: props.to, onClick: (e) => {
            e.preventDefault();
            return navigate();
          } }, slots);
        };
      }
    }));
    nuxtApp._route = route;
    nuxtApp._middleware ||= {
      global: [],
      named: {}
    };
    const initialLayout = nuxtApp.payload.state._layout;
    nuxtApp.hooks.hookOnce("app:created", async () => {
      router.beforeEach(async (to, from) => {
        to.meta = reactive(to.meta || {});
        if (nuxtApp.isHydrating && initialLayout && !isReadonly(to.meta.layout)) {
          to.meta.layout = initialLayout;
        }
        nuxtApp._processingMiddleware = true;
        if (!nuxtApp.ssrContext?.islandContext) {
          const middlewareEntries = /* @__PURE__ */ new Set([...globalMiddleware, ...nuxtApp._middleware.global]);
          {
            const routeRules = await nuxtApp.runWithContext(() => getRouteRules({ path: to.path }));
            if (routeRules.appMiddleware) {
              for (const key in routeRules.appMiddleware) {
                const guard = nuxtApp._middleware.named[key];
                if (!guard) {
                  return;
                }
                if (routeRules.appMiddleware[key]) {
                  middlewareEntries.add(guard);
                } else {
                  middlewareEntries.delete(guard);
                }
              }
            }
          }
          for (const middleware of middlewareEntries) {
            const result = await nuxtApp.runWithContext(() => middleware(to, from));
            {
              if (result === false || result instanceof Error) {
                const error = result || createError$1({
                  statusCode: 404,
                  statusMessage: `Page Not Found: ${initialURL}`,
                  data: {
                    path: initialURL
                  }
                });
                delete nuxtApp._processingMiddleware;
                return nuxtApp.runWithContext(() => showError(error));
              }
            }
            if (result === true) {
              continue;
            }
            if (result || result === false) {
              return result;
            }
          }
        }
      });
      router.afterEach(() => {
        delete nuxtApp._processingMiddleware;
      });
      await router.replace(initialURL);
      if (!isEqual(route.fullPath, initialURL)) {
        await nuxtApp.runWithContext(() => navigateTo(route.fullPath));
      }
    });
    return {
      provide: {
        route,
        router
      }
    };
  }
});
function injectHead(nuxtApp) {
  const nuxt = nuxtApp || tryUseNuxtApp();
  return nuxt?.ssrContext?.head || nuxt?.runWithContext(() => {
    if (hasInjectionContext()) {
      return inject(headSymbol);
    }
  });
}
function useHead(input, options = {}) {
  const head = injectHead(options.nuxt);
  if (head) {
    return useHead$1(input, { head, ...options });
  }
}
function definePayloadReducer(name, reduce) {
  {
    useNuxtApp().ssrContext._payloadReducers[name] = reduce;
  }
}
const reducers = [
  ["NuxtError", (data) => isNuxtError(data) && data.toJSON()],
  ["EmptyShallowRef", (data) => isRef(data) && isShallow(data) && !data.value && (typeof data.value === "bigint" ? "0n" : JSON.stringify(data.value) || "_")],
  ["EmptyRef", (data) => isRef(data) && !data.value && (typeof data.value === "bigint" ? "0n" : JSON.stringify(data.value) || "_")],
  ["ShallowRef", (data) => isRef(data) && isShallow(data) && data.value],
  ["ShallowReactive", (data) => isReactive(data) && isShallow(data) && toRaw(data)],
  ["Ref", (data) => isRef(data) && data.value],
  ["Reactive", (data) => isReactive(data) && toRaw(data)]
];
const revive_payload_server_MVtmlZaQpj6ApFmshWfUWl5PehCebzaBf2NuRMiIbms = /* @__PURE__ */ defineNuxtPlugin({
  name: "nuxt:revive-payload:server",
  setup() {
    for (const [reducer, fn] of reducers) {
      definePayloadReducer(reducer, fn);
    }
  }
});
const components_plugin_z4hgvsiddfKkfXTP6M8M4zG5Cb7sGnDhcryKVM45Di4 = /* @__PURE__ */ defineNuxtPlugin({
  name: "nuxt:global-components"
});
const plugins = [
  unhead_k2P3m_ZDyjlr2mMYnoDPwavjsDN8hBlk9cFai0bbopU,
  router_DclsWNDeVV7SyG4lslgLnjbQUK1ws8wgf2FHaAbo7Cw,
  revive_payload_server_MVtmlZaQpj6ApFmshWfUWl5PehCebzaBf2NuRMiIbms,
  components_plugin_z4hgvsiddfKkfXTP6M8M4zG5Cb7sGnDhcryKVM45Di4
];
const _imports_0 = publicAssetsURL("/icon.png");
const downloadUrl = "/downloads/BlockPilotSetup.exe";
const docsUrl = "/docs";
const docsLanUrl = "/docs/lan";
const docsHostingUrl = "/docs/24-7-hosting";
const docsPortUrl = "/docs/port-forwarding";
const docsTroubleUrl = "/docs/troubleshooting";
const docsPrivacyUrl = "/docs/privacy-and-network";
const version = "1.0.0";
const _sfc_main$2 = /* @__PURE__ */ defineComponent({
  __name: "app",
  __ssrInlineRender: true,
  setup(__props) {
    const downloadOptionLinks = {
      local: downloadUrl,
      lan: docsLanUrl,
      hosting: docsHostingUrl
    };
    const downloadOptionEvents = {
      local: "download_click",
      hosting: "docs_24_7_hosting_click"
    };
    const faqLinks = {
      hosting: docsHostingUrl,
      port: docsPortUrl,
      trouble: docsTroubleUrl
    };
    const translations = {
      en: {
        metaTitle: "BlockPilot | Gamer-first Minecraft server control",
        metaDescription: "A Windows GUI to create, run, and recover Minecraft servers with clear setup, local-only data, and transparent network checks.",
        siteName: "BlockPilot",
        nav: {
          features: "Features",
          help: "Guided Setup",
          how: "How It Works",
          compat: "Java Compatibility",
          security: "Security",
          download: "Download"
        },
        langLabel: "Language",
        langJa: "日本語",
        langEn: "English",
        eyebrow: "Guided hosting for first-time and returning players",
        heroTitle: "Launch with confidence, even on day one.",
        heroSub: "BlockPilot puts setup, checks, and recovery in one place so you can focus on your world.",
        heroNotes: [
          "A GUI to create, run, and recover Minecraft servers on Windows.",
          "Data stays on your PC. Only minimal required network calls. Admin actions are clearly shown."
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
            body: "Open logs and crash reports with one click when things go wrong."
          },
          {
            title: "Safe apply flow",
            body: "Changes are saved, but applied after restart to prevent mistakes."
          },
          {
            title: "Backup & restore built-in",
            body: "World backups and restores are standard, not add-ons."
          },
          {
            title: "Network guidance",
            body: "Checklist, IP lookup, firewall, and UPnP help in one tab."
          }
        ],
        featuresAffiliateLabel: "Always-on hosting is an option if your PC cannot stay on.",
        featuresAffiliateCta: "Compare hosting options (PR)",
        stepsTitle: "3 steps, no noise",
        steps: [
          {
            title: "Create a server profile (EULA required).",
            body: "Name, location, server type, and version."
          },
          {
            title: "Set only what matters.",
            body: "Memory, port, and Java path with clear explanations."
          },
          {
            title: "Start with checks.",
            body: "Java compatibility and network checks show what to fix first."
          }
        ],
        previewTitle: "Live Log",
        previewStatus: "Running",
        previewLines: [
          "[12:40:01] Starting server...",
          "[12:40:05] Preparing spawn area: 64%",
          '[12:40:07] Done (5.8s)! For help, type "help"'
        ],
        helpTitle: "Guided setup that prevents surprises",
        helpSub: "Clear checks and next steps before you press Start.",
        helpItems: [
          {
            title: "Java mismatch",
            body: "Warns you before launch and tells you the required Java version."
          },
          {
            title: "Port/Network confusion",
            body: "Guided checklist, public IP lookup, and port test in one tab."
          },
          {
            title: "Crash recovery",
            body: "One-click access to logs and crash reports with tips to fix fast."
          }
        ],
        helpCta: "Open setup notes",
        compatTitle: "Java compatibility",
        compatSub: "Required Java is checked before launch.",
        compat: [
          { title: "1.20.5 and newer", badge: "Java 21+" },
          { title: "1.18 - 1.20.4", badge: "Java 17+" },
          { title: "1.17", badge: "Java 16+" },
          { title: "1.16 and older", badge: "Java 8+" }
        ],
        compatNotes: [
          "We warn before launch if Java is too old for the selected version.",
          "Recommended Java depends on the Minecraft version you choose.",
          "If a start fails, open logs and crash reports for quick recovery."
        ],
        securityTitle: "Security & Privacy",
        securitySub: "Concrete, transparent, and local-first.",
        securityItems: [
          {
            title: "Local-only storage",
            body: "Server data is stored on your PC and never auto-uploaded to the cloud."
          },
          {
            title: "Minimal network calls",
            body: "External communication is limited to version checks and public IP lookup."
          },
          {
            title: "Published endpoints",
            body: "We publish the network endpoints and their purposes in Docs."
          },
          {
            title: "Explicit admin prompts",
            body: "Firewall/UPnP actions are announced and use UAC for elevation."
          },
          {
            title: "Local logs only",
            body: "Logs and crash reports are stored and viewed locally."
          }
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
            cta: "Download"
          },
          {
            id: "lan",
            title: "LAN only on the same Wi-Fi",
            body: "Keep it local for friends on your home network.",
            cta: "LAN setup guide"
          },
          {
            id: "hosting",
            title: "24/7 hosting or public access (PR)",
            body: "If your PC cannot stay on, hosted servers can help.",
            cta: "See options (PR)"
          }
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
            linkLabel: "View hosting options (PR)"
          },
          {
            title: "External access is not working.",
            body: "Follow the checklist for ports and routers.",
            linkId: "port",
            linkLabel: "Open port-forwarding guide"
          },
          {
            title: "It crashed and I do not know where logs are.",
            body: "Open logs/crash reports from the Console tab or follow the guide.",
            linkId: "trouble",
            linkLabel: "Open troubleshooting"
          },
          {
            title: "Does it work with mods?",
            body: "Vanilla is the main focus. Modded servers may need extra steps."
          },
          {
            title: "Where are server files stored?",
            body: "By default in your AppData folder. Custom locations are supported."
          },
          {
            title: "Is it free?",
            body: "Free for personal, non-commercial use. See license details."
          },
          {
            title: "How do I update?",
            body: "Download and run the latest installer."
          }
        ],
        footerDocs: "Docs",
        footerNotices: "Third-party notices",
        footerLicense: "License",
        footerRight: "Built for local worlds.",
        footerDisclosure: "This page includes affiliate links (PR)."
      },
      ja: {
        metaTitle: "BlockPilot | ゲーマー向けマイクラサーバー管理",
        metaDescription: "WindowsでMinecraftサーバーを作成・起動・復旧するGUI。データはローカル保存、通信は最小限、権限は明示します。",
        siteName: "BlockPilot",
        nav: {
          features: "特長",
          help: "ガイド付きセットアップ",
          how: "使い方",
          compat: "Java互換",
          security: "安心設計",
          download: "ダウンロード"
        },
        langLabel: "言語",
        langJa: "日本語",
        langEn: "English",
        eyebrow: "ゲーマー向け Minecraft サーバー管理",
        heroTitle: "サクッと建てて、安定稼働。",
        heroSub: "Java・ポート・クラッシュのチェックまで、一画面でスマートに。",
        heroNotes: [
          "WindowsでMinecraftサーバーを、迷わず作って・動かして・戻せるGUI",
          "データはPC内。必要最小限の通信のみ。権限が必要な操作は明示します"
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
            body: "クラッシュ時はログ/クラッシュレポートへワンクリック。"
          },
          {
            title: "安全な反映フロー",
            body: "変更は保存できるが反映は再起動後。事故を防ぎます。"
          },
          {
            title: "バックアップ/復元が標準",
            body: "ワールドのバックアップ/復元を標準で搭載。"
          },
          {
            title: "ネットワーク支援",
            body: "公開チェック/IP表示/Firewall/UPnPのガイド。"
          }
        ],
        featuresAffiliateLabel: "PCをつけっぱなしにできない場合は、VPSという選択肢もあります。",
        featuresAffiliateCta: "24時間運用の比較を見る（PR）",
        stepsTitle: "起動まで、たった 3 ステップ",
        steps: [
          {
            title: "サーバープロファイル作成（EULA同意必須）",
            body: "名前/保存先/種別/バージョンを指定。"
          },
          {
            title: "必要な設定だけ",
            body: "メモリ/ポート/Javaパスを説明しながら設定。"
          },
          {
            title: "起動前チェック",
            body: "Java互換/公開チェックで不安を減らす。"
          }
        ],
        previewTitle: "ライブログ",
        previewStatus: "稼働中",
        previewLines: [
          "[12:40:01] サーバー起動中...",
          "[12:40:05] スポーン準備中: 64%",
          "[12:40:07] 完了 (5.8秒)! help と入力でヘルプ表示"
        ],
        helpTitle: "ガイド付きでスムーズに起動",
        helpSub: "起動前のチェックと次の一手を見える化。",
        helpItems: [
          {
            title: "Java のバージョン違い",
            body: "起動前に必要バージョンを警告します。"
          },
          {
            title: "ポート/ネットワークの迷子",
            body: "公開チェック・IP表示・ガイドを一画面に集約。"
          },
          {
            title: "クラッシュ復旧",
            body: "ログ/クラッシュレポートへ即アクセスできます。"
          }
        ],
        helpCta: "セットアップノートを見る",
        compatTitle: "Java 互換表",
        compatSub: "起動前に必要 Java を判定します。",
        compat: [
          { title: "1.20.5 以降", badge: "Java 21+" },
          { title: "1.18 - 1.20.4", badge: "Java 17+" },
          { title: "1.17", badge: "Java 16+" },
          { title: "1.16 以下", badge: "Java 8+" }
        ],
        compatNotes: [
          "必要Javaに足りない場合は起動前に警告します。",
          "推奨Javaはバージョンによって異なります。",
          "失敗時はログ/クラッシュレポートで復旧します。"
        ],
        securityTitle: "セキュリティ/プライバシー",
        securitySub: "曖昧にせず、具体的に。",
        securityItems: [
          {
            title: "ローカル保存",
            body: "サーバーデータは端末内に保存され、クラウドへ自動送信しません。"
          },
          {
            title: "必要最小限の通信",
            body: "外部通信はバージョン取得・公開IP取得など必要最小限です。"
          },
          {
            title: "通信先一覧を公開",
            body: "通信先一覧をDocsで公開しています。"
          },
          {
            title: "権限の明示",
            body: "Firewall/UPnPは実行前に明示し、UACで昇格します。"
          },
          {
            title: "ログはローカル",
            body: "ログ/クラッシュレポートはローカル表示・ローカル保存です。"
          }
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
            cta: "ダウンロード"
          },
          {
            id: "lan",
            title: "同じWi-FiでLAN参加",
            body: "家の中だけで遊びたい人向け。",
            cta: "LAN手順を見る"
          },
          {
            id: "hosting",
            title: "24時間稼働・外部公開したい（PR）",
            body: "PCをつけっぱなしにできない場合の選択肢。",
            cta: "比較を見る（PR）"
          }
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
            linkLabel: "24時間運用の選択肢を見る（PR）"
          },
          {
            title: "外部公開ができません",
            body: "ポート開放チェックリストを確認してください。",
            linkId: "port",
            linkLabel: "ポート開放ガイドを見る"
          },
          {
            title: "クラッシュした/ログが分からない",
            body: "コンソールからログ/クラッシュレポートを開くか、ガイドを参照してください。",
            linkId: "trouble",
            linkLabel: "トラブルシュートを見る"
          },
          {
            title: "MOD は使えますか？",
            body: "基本はバニラ向けです。MOD は追加の手順が必要な場合があります。"
          },
          {
            title: "サーバーファイルはどこ？",
            body: "既定では AppData 配下。任意の場所にも変更できます。"
          },
          {
            title: "料金は？",
            body: "個人・非商用は無料です。詳細はライセンスをご確認ください。"
          },
          {
            title: "アップデート方法は？",
            body: "最新のインストーラーを入手して実行してください。"
          }
        ],
        footerDocs: "ドキュメント",
        footerNotices: "サードパーティ通知",
        footerLicense: "ライセンス",
        footerRight: "ローカル世界のために。",
        footerDisclosure: "本ページにはアフィリエイトリンク（PR）が含まれます。"
      }
    };
    const currentLang = ref("en");
    const t = computed(() => translations[currentLang.value]);
    const runtimeConfig = /* @__PURE__ */ useRuntimeConfig();
    const siteUrl = computed(() => {
      const raw = runtimeConfig.public.siteUrl;
      if (!raw) {
        return "";
      }
      return raw.endsWith("/") ? raw.slice(0, -1) : raw;
    });
    useHead(() => ({
      title: t.value.metaTitle,
      htmlAttrs: { lang: currentLang.value },
      meta: [
        { name: "description", content: t.value.metaDescription },
        { name: "robots", content: "index,follow" },
        { name: "theme-color", content: "#0b0f17" },
        { property: "og:title", content: t.value.metaTitle },
        { property: "og:description", content: t.value.metaDescription },
        { property: "og:type", content: "website" },
        { property: "og:site_name", content: t.value.siteName },
        ...siteUrl.value ? [{ property: "og:url", content: `${siteUrl.value}/` }] : [],
        { name: "twitter:card", content: "summary_large_image" },
        { name: "twitter:title", content: t.value.metaTitle },
        { name: "twitter:description", content: t.value.metaDescription }
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
              url: siteUrl.value ? `${siteUrl.value}/` : void 0,
              inLanguage: currentLang.value
            },
            {
              "@context": "https://schema.org",
              "@type": "SoftwareApplication",
              name: t.value.siteName,
              description: t.value.metaDescription,
              operatingSystem: "Windows 10/11",
              applicationCategory: "UtilitiesApplication",
              softwareVersion: version,
              url: siteUrl.value ? `${siteUrl.value}/` : void 0
            }
          ])
        }
      ]
    }));
    return (_ctx, _push, _parent, _attrs) => {
      _push(`<div${ssrRenderAttrs(mergeProps({ class: "page" }, _attrs))}><header class="nav"><div class="brand"><img${ssrRenderAttr("src", _imports_0)} alt="BlockPilot" class="brand-icon"><span class="brand-name">BlockPilot</span></div><nav class="nav-links"><a href="#features">${ssrInterpolate(t.value.nav.features)}</a><a href="#help">${ssrInterpolate(t.value.nav.help)}</a><a href="#how">${ssrInterpolate(t.value.nav.how)}</a><a href="#compat">${ssrInterpolate(t.value.nav.compat)}</a><a href="#security">${ssrInterpolate(t.value.nav.security)}</a><a href="#download" class="nav-cta">${ssrInterpolate(t.value.nav.download)}</a><div class="lang-toggle" aria-label="Language toggle"><span class="lang-label">${ssrInterpolate(t.value.langLabel)}</span><button class="${ssrRenderClass([{ active: currentLang.value === "ja" }, "lang-btn"])}" type="button">${ssrInterpolate(t.value.langJa)}</button><button class="${ssrRenderClass([{ active: currentLang.value === "en" }, "lang-btn"])}" type="button">${ssrInterpolate(t.value.langEn)}</button></div></nav></header><main><section class="hero"><div class="hero-bg" aria-hidden="true"></div><div class="hero-content"><p class="eyebrow">${ssrInterpolate(t.value.eyebrow)}</p><h1 class="hero-title">${ssrInterpolate(t.value.heroTitle)}</h1><p class="hero-sub">${ssrInterpolate(t.value.heroSub)}</p><ul class="hero-notes"><!--[-->`);
      ssrRenderList(t.value.heroNotes, (note) => {
        _push(`<li>${ssrInterpolate(note)}</li>`);
      });
      _push(`<!--]--></ul><div class="hero-affiliate"><span>${ssrInterpolate(t.value.heroAffiliateLabel)}</span><a${ssrRenderAttr("href", docsHostingUrl)} class="hero-affiliate-link" data-event="docs_24_7_hosting_click">${ssrInterpolate(t.value.heroAffiliateCta)}</a></div><div class="hero-actions"><a${ssrRenderAttr("href", downloadUrl)} class="btn primary" data-event="download_click">${ssrInterpolate(t.value.heroCtaPrimary)}</a><a${ssrRenderAttr("href", docsUrl)} class="btn ghost">${ssrInterpolate(t.value.heroCtaSecondary)}</a></div><div class="hero-meta"><span class="chip">${ssrInterpolate(t.value.heroMetaVersion)} ${ssrInterpolate(version)}</span><span class="chip">${ssrInterpolate(t.value.heroMetaWindows)}</span><span class="chip">${ssrInterpolate(t.value.heroMetaRuntime)}</span></div></div><div class="hero-card"><div class="hero-card-header"><span>${ssrInterpolate(t.value.cardTitle)}</span><span class="pulse"></span></div><div class="hero-card-body"><div class="stat"><span class="stat-label">${ssrInterpolate(t.value.cardStatusLabel)}</span><span class="stat-value">${ssrInterpolate(t.value.cardStatusValue)}</span></div><div class="stat"><span class="stat-label">${ssrInterpolate(t.value.cardPlayersLabel)}</span><span class="stat-value">6 / 20</span></div><div class="stat"><span class="stat-label">${ssrInterpolate(t.value.cardJavaLabel)}</span><span class="stat-value">17.0.x</span></div><div class="hero-bars"><div class="bar"><span>${ssrInterpolate(t.value.cardCpu)}</span><div class="bar-track"><div class="bar-fill" style="${ssrRenderStyle({ "width": "48%" })}"></div></div></div><div class="bar"><span>${ssrInterpolate(t.value.cardMemory)}</span><div class="bar-track"><div class="bar-fill alt" style="${ssrRenderStyle({ "width": "64%" })}"></div></div></div></div><div class="hero-cta"><button class="btn tiny" type="button">${ssrInterpolate(t.value.cardStart)}</button><button class="btn tiny ghost" type="button">${ssrInterpolate(t.value.cardBackup)}</button><button class="btn tiny ghost" type="button">${ssrInterpolate(t.value.cardFirewall)}</button></div></div></div></section><section id="features" class="section reveal"><div class="section-head"><h2>${ssrInterpolate(t.value.featuresTitle)}</h2><p>${ssrInterpolate(t.value.featuresSub)}</p></div><div class="grid features"><!--[-->`);
      ssrRenderList(t.value.features, (item) => {
        _push(`<div class="panel"><h3>${ssrInterpolate(item.title)}</h3><p>${ssrInterpolate(item.body)}</p></div>`);
      });
      _push(`<!--]--></div><div class="features-affiliate"><span>${ssrInterpolate(t.value.featuresAffiliateLabel)}</span><a${ssrRenderAttr("href", docsHostingUrl)} class="features-affiliate-link" data-event="docs_24_7_hosting_click">${ssrInterpolate(t.value.featuresAffiliateCta)}</a></div></section><section id="help" class="section reveal"><div class="section-head"><h2>${ssrInterpolate(t.value.helpTitle)}</h2><p>${ssrInterpolate(t.value.helpSub)}</p></div><div class="grid support"><!--[-->`);
      ssrRenderList(t.value.helpItems, (item) => {
        _push(`<div class="panel"><h3>${ssrInterpolate(item.title)}</h3><p>${ssrInterpolate(item.body)}</p></div>`);
      });
      _push(`<!--]--></div><div class="support-cta"><a${ssrRenderAttr("href", docsUrl)} class="btn ghost">${ssrInterpolate(t.value.helpCta)}</a></div></section><section id="how" class="section split reveal"><div><h2>${ssrInterpolate(t.value.stepsTitle)}</h2><ol class="steps"><!--[-->`);
      ssrRenderList(t.value.steps, (step) => {
        _push(`<li><strong>${ssrInterpolate(step.title)}</strong> ${ssrInterpolate(step.body)}</li>`);
      });
      _push(`<!--]--></ol></div><div class="panel preview"><div class="preview-header"><span>${ssrInterpolate(t.value.previewTitle)}</span><span class="pill">${ssrInterpolate(t.value.previewStatus)}</span></div><div class="preview-body"><!--[-->`);
      ssrRenderList(t.value.previewLines, (line) => {
        _push(`<p>${ssrInterpolate(line)}</p>`);
      });
      _push(`<!--]--></div></div></section><section id="compat" class="section reveal"><div class="section-head"><h2>${ssrInterpolate(t.value.compatTitle)}</h2><p>${ssrInterpolate(t.value.compatSub)}</p></div><div class="compat-grid"><!--[-->`);
      ssrRenderList(t.value.compat, (item) => {
        _push(`<div class="compat-card"><h4>${ssrInterpolate(item.title)}</h4><span class="badge">${ssrInterpolate(item.badge)}</span></div>`);
      });
      _push(`<!--]--></div><ul class="compat-notes"><!--[-->`);
      ssrRenderList(t.value.compatNotes, (note) => {
        _push(`<li>${ssrInterpolate(note)}</li>`);
      });
      _push(`<!--]--></ul></section><section id="security" class="section reveal"><div class="section-head"><h2>${ssrInterpolate(t.value.securityTitle)}</h2><p>${ssrInterpolate(t.value.securitySub)}</p></div><div class="grid security"><!--[-->`);
      ssrRenderList(t.value.securityItems, (item) => {
        _push(`<div class="panel"><h3>${ssrInterpolate(item.title)}</h3><p>${ssrInterpolate(item.body)}</p></div>`);
      });
      _push(`<!--]--></div><div class="security-cta"><a${ssrRenderAttr("href", docsPrivacyUrl)} class="btn ghost">${ssrInterpolate(t.value.securityCta)}</a></div></section><section id="download" class="section reveal"><div class="callout"><div><h2>${ssrInterpolate(t.value.downloadTitle)}</h2><p>${ssrInterpolate(t.value.downloadSub)}</p></div><div class="callout-actions"><a${ssrRenderAttr("href", downloadUrl)} class="btn primary" data-event="download_click">${ssrInterpolate(t.value.downloadPrimary)}</a><a${ssrRenderAttr("href", docsUrl)} class="btn ghost">${ssrInterpolate(t.value.downloadSecondary)}</a></div></div><div class="download-options-block"><div class="section-head"><h2>${ssrInterpolate(t.value.downloadOptionsTitle)}</h2><p>${ssrInterpolate(t.value.downloadOptionsSub)}</p></div><div class="grid download-options"><!--[-->`);
      ssrRenderList(t.value.downloadOptions, (option) => {
        _push(`<div class="panel"><h3>${ssrInterpolate(option.title)}</h3><p>${ssrInterpolate(option.body)}</p><a${ssrRenderAttr("href", downloadOptionLinks[option.id])} class="btn ghost"${ssrRenderAttr("data-event", downloadOptionEvents[option.id])}>${ssrInterpolate(option.cta)}</a></div>`);
      });
      _push(`<!--]--></div></div></section><section class="section reveal"><div class="section-head"><h2>${ssrInterpolate(t.value.faqTitle)}</h2><p>${ssrInterpolate(t.value.faqSub)}</p></div><div class="grid faq"><!--[-->`);
      ssrRenderList(t.value.faq, (item) => {
        _push(`<div class="panel"><h3>${ssrInterpolate(item.title)}</h3><p>${ssrInterpolate(item.body)}</p>`);
        if (item.linkId) {
          _push(`<a${ssrRenderAttr("href", faqLinks[item.linkId])} class="faq-link"${ssrRenderAttr("data-event", item.linkId === "hosting" ? "docs_24_7_hosting_click" : null)}>${ssrInterpolate(item.linkLabel)}</a>`);
        } else {
          _push(`<!---->`);
        }
        _push(`</div>`);
      });
      _push(`<!--]--></div></section></main><footer class="footer"><div class="footer-left"><img${ssrRenderAttr("src", _imports_0)} alt="BlockPilot" class="brand-icon small"><span>BlockPilot</span></div><div class="footer-links"><a${ssrRenderAttr("href", docsUrl)}>${ssrInterpolate(t.value.footerDocs)}</a><a href="/THIRD_PARTY_NOTICES.txt">${ssrInterpolate(t.value.footerNotices)}</a><a href="/LICENSE.txt">${ssrInterpolate(t.value.footerLicense)}</a></div><div class="footer-right">${ssrInterpolate(t.value.footerRight)}</div><div class="footer-disclosure">${ssrInterpolate(t.value.footerDisclosure)}</div></footer></div>`);
    };
  }
});
const _sfc_setup$2 = _sfc_main$2.setup;
_sfc_main$2.setup = (props, ctx) => {
  const ssrContext = useSSRContext();
  (ssrContext.modules || (ssrContext.modules = /* @__PURE__ */ new Set())).add("app.vue");
  return _sfc_setup$2 ? _sfc_setup$2(props, ctx) : void 0;
};
const _sfc_main$1 = {
  __name: "nuxt-error-page",
  __ssrInlineRender: true,
  props: {
    error: Object
  },
  setup(__props) {
    const props = __props;
    const _error = props.error;
    const statusCode = Number(_error.statusCode || 500);
    const is404 = statusCode === 404;
    const statusMessage = _error.statusMessage ?? (is404 ? "Page Not Found" : "Internal Server Error");
    const description = _error.message || _error.toString();
    const stack = void 0;
    const _Error404 = defineAsyncComponent(() => import("./_nuxt/error-404-BvbHrJks.js"));
    const _Error = defineAsyncComponent(() => import("./_nuxt/error-500-zdpq1sEA.js"));
    const ErrorTemplate = is404 ? _Error404 : _Error;
    return (_ctx, _push, _parent, _attrs) => {
      _push(ssrRenderComponent(unref(ErrorTemplate), mergeProps({ statusCode: unref(statusCode), statusMessage: unref(statusMessage), description: unref(description), stack: unref(stack) }, _attrs), null, _parent));
    };
  }
};
const _sfc_setup$1 = _sfc_main$1.setup;
_sfc_main$1.setup = (props, ctx) => {
  const ssrContext = useSSRContext();
  (ssrContext.modules || (ssrContext.modules = /* @__PURE__ */ new Set())).add("node_modules/nuxt/dist/app/components/nuxt-error-page.vue");
  return _sfc_setup$1 ? _sfc_setup$1(props, ctx) : void 0;
};
const _sfc_main = {
  __name: "nuxt-root",
  __ssrInlineRender: true,
  setup(__props) {
    const IslandRenderer = () => null;
    const nuxtApp = useNuxtApp();
    nuxtApp.deferHydration();
    nuxtApp.ssrContext.url;
    const SingleRenderer = false;
    provide(PageRouteSymbol, useRoute());
    nuxtApp.hooks.callHookWith((hooks) => hooks.map((hook) => hook()), "vue:setup");
    const error = /* @__PURE__ */ useError();
    const abortRender = error.value && !nuxtApp.ssrContext.error;
    onErrorCaptured((err, target, info) => {
      nuxtApp.hooks.callHook("vue:error", err, target, info).catch((hookError) => console.error("[nuxt] Error in `vue:error` hook", hookError));
      {
        const p = nuxtApp.runWithContext(() => showError(err));
        onServerPrefetch(() => p);
        return false;
      }
    });
    const islandContext = nuxtApp.ssrContext.islandContext;
    return (_ctx, _push, _parent, _attrs) => {
      ssrRenderSuspense(_push, {
        default: () => {
          if (unref(abortRender)) {
            _push(`<div></div>`);
          } else if (unref(error)) {
            _push(ssrRenderComponent(unref(_sfc_main$1), { error: unref(error) }, null, _parent));
          } else if (unref(islandContext)) {
            _push(ssrRenderComponent(unref(IslandRenderer), { context: unref(islandContext) }, null, _parent));
          } else if (unref(SingleRenderer)) {
            ssrRenderVNode(_push, createVNode(resolveDynamicComponent(unref(SingleRenderer)), null, null), _parent);
          } else {
            _push(ssrRenderComponent(unref(_sfc_main$2), null, null, _parent));
          }
        },
        _: 1
      });
    };
  }
};
const _sfc_setup = _sfc_main.setup;
_sfc_main.setup = (props, ctx) => {
  const ssrContext = useSSRContext();
  (ssrContext.modules || (ssrContext.modules = /* @__PURE__ */ new Set())).add("node_modules/nuxt/dist/app/components/nuxt-root.vue");
  return _sfc_setup ? _sfc_setup(props, ctx) : void 0;
};
let entry;
{
  entry = async function createNuxtAppServer(ssrContext) {
    const vueApp = createApp(_sfc_main);
    const nuxt = createNuxtApp({ vueApp, ssrContext });
    try {
      await applyPlugins(nuxt, plugins);
      await nuxt.hooks.callHook("app:created", vueApp);
    } catch (error) {
      await nuxt.hooks.callHook("app:error", error);
      nuxt.payload.error ||= createError(error);
    }
    if (ssrContext?._renderResponse) {
      throw new Error("skipping render");
    }
    return vueApp;
  };
}
const entry_default = (ssrContext) => entry(ssrContext);
export {
  useNuxtApp as a,
  useRuntimeConfig as b,
  nuxtLinkDefaults as c,
  useHead as d,
  entry_default as default,
  navigateTo as n,
  resolveRouteObject as r,
  useRouter as u
};
//# sourceMappingURL=server.mjs.map
