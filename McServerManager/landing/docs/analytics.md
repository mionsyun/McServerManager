# Landing and docs analytics

## Implementation

`public/analytics.js` is the shared classic script. The landing page and all 33 docs load it with a `data-measurement-id` attribute. `npm run prepare:analytics` removes the old inline Google loaders/click trackers, preserves the docs' language controls and JSON-LD, and regenerates the fixed docs route/title catalog. It is byte-idempotent and runs before docs aliases are created.

The only permitted collection origins are HTTPS `maipilot.jp` and `www.maipilot.jp` on the standard port. Localhost, every 127.* address, IPv6 loopback, *.localhost, local files, LAN and preview hosts cannot load Google analytics or enqueue custom events. The configured GA property's disable flag is also set outside production. There is no debug bypass. Tests use a VM or an intercepted production origin with network mocking; they must never send real Google requests.

The API is `window.MaiPilotAnalytics.init({ measurementId })` and `track(eventName, { cta_location })`. It reuses an existing `gtag`/Google script, adds one pair of delegated click/auxclick listeners and suppresses a duplicate automatic page view when a matching config command is already queued. An independently installed tag that ran before this script cannot be retroactively undone; audit for additional loaders if tags are changed.

## Metric definitions

- `download_click`: one custom event per native activation of the real installer link; `download_kind=official_free`
- `booth_click`: BOOTH support-store navigation; `download_kind=booth_support`. It is not a completed purchase
- `docs_to_download`: docs CTA navigation to the landing page, not a file download
- Existing provider events (`pr_conoha_click`, `pr_xserver_click`, `pr_shinvps_click`) and their aggregate `outbound_affiliate_click` are retained. Those two affiliate views should not be summed
- `download_section_click`, `setup_guide_click`, docs article/navigation events remain distinct from actual installer intent

Native Enter activation uses the same `click` event as primary-button clicks. Middle-button `auxclick` is supported. Right clicks, prevented/disabled activations and non-links do not count. The same event object is never processed twice; separate legitimate activations are not artificially suppressed.

These are intent measurements. They do not demonstrate download completion, installation, server setup, revenue, or causal conversion improvement. In particular, do not sum the custom `download_click` with GA's enhanced `file_download` event.

## Bounded fields and privacy

Custom events include `cta_location`, `page_path`, `ui_language` (`ja`, `en`, `unknown`), `platform_class` (`windows`, `mobile`, `other`, `unknown`), and `journey_origin`/`journey_stage`. Only download/support events include `download_kind`. Provider events may include an allowlisted `affiliate_partner`.

Paths come from the fixed public route catalog; docs aliases resolve to canonical `/docs/` routes and unknown paths become `/other/`. Page titles are generated from controlled static docs titles plus fixed Japanese/English homepage titles. No arbitrary DOM text, link labels, link URLs, email addresses, user IDs, arbitrary input parameters, query strings or fragments are copied into custom events. A same-site referrer is normalized to a known route; an external referrer retains only its origin. Page-location overrides contain only origin + safe path.


The homepage's initial page view omits `ui_language` because Nuxt may not have hydrated the selected language yet. Custom activations always read the currently displayed document language. Docs language controls update `html.lang` before the deferred script runs.

## Campaign attribution tradeoffs

The script preserves exact known `utm_source` values (`google`, `bing`, `yahoo`, `duckduckgo`, `youtube`, `x`, `twitter`, `instagram`, `facebook`, `reddit`, `discord`, `booth`, `newsletter`, `maipilot`) as `campaign_source`, and known `utm_medium` values (`organic`, `cpc`, `ppc`, `paid_social`, `social`, `referral`, `email`, `display`, `video`, `banner`, `affiliate`) as `campaign_medium`.

Unknown source/medium values are omitted, not guessed or relabeled. Freeform `utm_campaign`, `utm_term`, `utm_content`, click IDs and other query values are deliberately not forwarded by this module. Therefore campaign-level/ad-click attribution is incomplete by design. Add a reviewed fixed campaign vocabulary if that reporting becomes necessary; do not restore arbitrary query forwarding. Google's supported config fields are documented in [GA4 configuration](https://developers.google.com/analytics/devguides/collection/ga4/reference/config).

## GA property checks still required

These repository tests do not verify receipt in the live property or its settings. Before using the metrics:

1. Register the custom event dimensions needed for reporting and verify the expected events in an authorized GA session
2. Inspect existing enhanced measurement, tag-manager rules and other installed tags. Enhanced outbound/file-download measurement can independently send a full `link_url`, including query values, and duplicate the intent represented by custom events. The shared module does not configure these property-level switches. Disable or appropriately configure automatic collection if strict URL-field minimization is required. See [enhanced measurement settings](https://developers.google.com/analytics/devguides/config/admin/v1/rest/v1alpha/EnhancedMeasurementSettings) and [the `link_url` field](https://support.google.com/analytics/answer/13784088?hl=en)
3. Production visits by the owner/developers still count. Configure internal-traffic definitions and validate a filter in Testing before making it Active. An active exclusion removes future matching data from processed reports; it does not clean historical data and excluded data cannot be recovered. This requires GA access and cannot be established by a repository change. See [Google's internal-traffic instructions](https://support.google.com/analytics/answer/10104470?hl=en)
4. Compare matching date ranges and acquisition/device/landing-route cohorts only after establishing a clean baseline. The code change alone does not establish a numerical improvement

## Verification

Run `npm test` (or `node --test tests/analytics.test.mjs`) for the closed host/route/parameter vocabularies, single activation semantics, mocked injected-GA handling, ignored legacy query parameters, docs migration preservation/idempotence, static titles and safe campaign extraction. Browser tests should intercept all requests before navigating to a mocked production hostname. No QA run should contact Google collection endpoints.
