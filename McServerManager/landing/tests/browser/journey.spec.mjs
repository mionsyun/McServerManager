import { test, expect } from '@playwright/test';
import { readFile, stat, mkdir } from 'node:fs/promises';
import path from 'node:path';

const root = path.resolve('.output/public');
const screenshotRoot = path.resolve('test-results/previews');
const windowsUA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36';
const mobileUA = 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1';
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml', '.xml': 'application/xml', '.txt': 'text/plain' };

async function setup(browser, { locale='ja-JP', width=1440, mobile=false, hostname='www.maipilot.jp', clipboard='success', share='success', storage='enabled' }={}) {
  const context = await browser.newContext({ viewport: { width, height: mobile ? 844 : 1000 }, locale,
    userAgent: mobile ? mobileUA : windowsUA, isMobile: mobile, hasTouch: mobile });
  // Intercept EVERY request. This suite never contacts GA, BOOTH, installer storage, ads or the live site.
  const requests = []; const errors = []; const missing = [];
  await context.route('**/*', async route => {
    const url = new URL(route.request().url()); requests.push(url.href);
    if (url.hostname === hostname) {
      let file = path.resolve(root, '.' + decodeURIComponent(url.pathname));
      if (!file.startsWith(root + path.sep) && file !== root) return route.fulfill({ status: 400, body: '' });
      try {
        if ((await stat(file)).isDirectory()) file = path.join(file, 'index.html');
        return route.fulfill({ status: 200, contentType: mime[path.extname(file)] || 'application/octet-stream', body: await readFile(file) });
      } catch { missing.push(url.pathname); return route.fulfill({ status: 404, body: 'Missing local test asset' }); }
    }
    if (url.hostname === 'www.googletagmanager.com') return route.fulfill({ contentType: 'text/javascript', body: 'window.__gaMockLoaded = (window.__gaMockLoaded || 0) + 1;' });
    if (url.pathname.endsWith('.exe')) return route.fulfill({ contentType: 'application/octet-stream', headers: { 'content-disposition': 'attachment; filename="TEST-ONLY.txt"' }, body: 'Installer request mocked; no executable downloaded.' });
    return route.fulfill({ status: 204, body: '' });
  });
  await context.addInitScript(({ clipboard, share, storage }) => {
    window.__copied = []; window.__shares = [];
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: async text => {
      if (clipboard === 'deny') throw new DOMException('Denied', 'NotAllowedError'); window.__copied.push(text);
    } } });
    Object.defineProperty(navigator, 'share', { configurable: true, value: async data => {
      if (share === 'cancel') throw new DOMException('Cancelled', 'AbortError');
      if (share === 'fail') throw new Error('Unavailable'); window.__shares.push(data);
    } });
    if (storage === 'disabled') Object.defineProperty(window, 'localStorage', { get() { throw new DOMException('Denied', 'SecurityError'); } });
  }, { clipboard, share, storage });
  const page = await context.newPage(); page.on('pageerror', err => errors.push(err.message));
  await page.goto(`https://${hostname}/`); await page.waitForFunction(() => document.querySelector('.pc-handoff'));
  await page.waitForTimeout(250);
  return { page, context, requests, errors, missing };
}
async function events(page, name) {
  return page.evaluate(name => (window.dataLayer || []).map(entry => Array.from(entry)).filter(entry => entry[0] === 'event' && (!name || entry[1] === name)), name);
}
async function assertNoOverflow(page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
}

test('desktop free-download path, truthful copy, manifest/schema and one event per click', async ({ browser }) => {
  const { page, context, errors, missing } = await setup(browser);
  const hero = page.locator('.hero-actions [data-event="download_click"]');
  await expect(hero).toContainText('無料'); await expect(hero).toHaveAttribute('href', /MaiPilotSetup.exe$/);
  await expect(page.locator('.hero-meta')).toContainText('2.2.0');
  await expect(page.locator('.hero-sub')).toContainText('統合版');
  await expect(page.locator('#support')).toContainText('100円');
  expect(await page.locator('.hero-actions [data-event="booth_click"]').count()).toBe(0);
  const offers = await page.locator('script[type="application/ld+json"]').evaluate(el => JSON.parse(el.textContent).find(x => x['@type'] === 'SoftwareApplication').offers);
  expect(offers.map(x => x.price)).toEqual(['0','100']);
  for (let i=1; i<=2; i++) { const download = page.waitForEvent('download'); await hero.click(); await download; expect((await events(page, 'download_click')).length).toBe(i); }
  const parameters = (await events(page, 'download_click'))[0][2];
  expect(parameters).toMatchObject({ cta_location: 'hero', platform_class: 'windows', ui_language: 'ja', download_kind: 'official_free', page_path: '/' });
  expect(await events(page, 'booth_click')).toHaveLength(0);
  expect(errors).toEqual([]); expect(missing).toEqual([]); await assertNoOverflow(page);
  await mkdir(screenshotRoot, { recursive: true }); await page.screenshot({ path: path.join(screenshotRoot, 'ja-desktop.png') });
  await context.close();
});

test('mobile CTA hands off to PC; confirmed copy/share and cancellation are distinct', async ({ browser }) => {
  const { page, context, errors, missing } = await setup(browser, { mobile: true, width: 390 });
  const hero = page.locator('.hero-actions .primary');
  await expect(hero).toHaveAttribute('href', '#pc-handoff'); await expect(hero).toContainText('Windows PC');
  const bounds = await hero.boundingBox(); expect(bounds.y + bounds.height).toBeLessThan(844);
  await hero.click(); expect(await events(page, 'download_click')).toHaveLength(0);
  expect(await events(page, 'pc_handoff_open')).toHaveLength(1);
  await page.getByRole('button', { name: 'PC用リンクをコピー' }).click();
  await expect(page.getByRole('status')).toContainText('コピーしました');
  expect(await page.evaluate(() => window.__copied)).toEqual(['https://www.maipilot.jp/?from=mobile_handoff#download']);
  expect(await events(page, 'pc_link_copy')).toHaveLength(1);
  await page.getByRole('button', { name: 'リンクを共有', exact: true }).click();
  expect(await events(page, 'pc_link_share')).toHaveLength(1);
  await page.evaluate(() => { navigator.share = async () => { throw new DOMException('Cancelled','AbortError'); }; });
  await page.getByRole('button', { name: 'リンクを共有', exact: true }).click();
  expect(await events(page, 'pc_link_share')).toHaveLength(1);
  await assertNoOverflow(page); expect(errors).toEqual([]); expect(missing).toEqual([]);
  await page.evaluate(() => window.scrollTo(0,0)); await page.screenshot({ path: path.join(screenshotRoot,'ja-mobile.png') });
  await context.close();
});

test('clipboard denied + unavailable storage fail safely; mobile menu Escape/close/reopen', async ({ browser }) => {
  const { page, context, errors } = await setup(browser, { mobile:true, width:320, clipboard:'deny', storage:'disabled' });
  await page.getByRole('button', { name:'PC用リンクをコピー' }).click();
  await expect(page.getByRole('status')).toContainText('手動');
  await expect(page.locator('.handoff-manual input')).toHaveValue('https://www.maipilot.jp/?from=mobile_handoff#download');
  expect(await events(page,'pc_link_copy')).toHaveLength(0);
  const menu = page.locator('button.hamburger[aria-controls="main-navigation"]');
  await menu.click(); await expect(menu).toHaveAttribute('aria-expanded','true');
  await page.keyboard.press('Escape'); await expect(menu).toHaveAttribute('aria-expanded','false');
  await menu.click(); await page.locator('.nav-links a[href="#download"]').click();
  await expect(menu).toHaveAttribute('aria-expanded','false');
  await menu.click(); await page.getByRole('button',{name:'English', exact:true}).click();
  await page.keyboard.press('Escape'); await expect(page.locator('html')).toHaveAttribute('lang','en');
  expect(errors).toEqual([]); await assertNoOverflow(page); await context.close();
});

for (const [width, mobile, locale] of [[1440,false,'en-US'],[768,false,'ja-JP'],[390,true,'en-US'],[320,true,'ja-JP']]) {
  test(`responsive images and local links ${width} ${locale}`, async ({ browser }) => {
    const { page, context, errors, missing } = await setup(browser, { width,mobile,locale });
    await page.evaluate(async () => { for (const img of document.images) { img.loading = 'eager'; } });
    await expect(page.locator('.product-shot img')).toHaveCount(5);
    await page.waitForFunction(() => [...document.querySelectorAll('.product-shot img')].every(img=>img.complete && img.naturalWidth>0));
    await assertNoOverflow(page); expect(errors).toEqual([]); expect(missing).toEqual([]);
    const hrefs = await page.locator('a[href]').evaluateAll(links => links.map(a=>a.getAttribute('href')).filter(h=>h.startsWith('/')));
    for (const href of hrefs) {
      const pathname = new URL(href, 'https://www.maipilot.jp').pathname;
      const file = path.resolve(root,'.'+pathname); await expect(stat(file), `local link exists: ${href}`).resolves.toBeTruthy();
    }
    if(locale==='en-US') { await mkdir(screenshotRoot,{recursive:true}); await page.screenshot({path:path.join(screenshotRoot,mobile?'en-mobile.png':'en-desktop.png')}); }
    await context.close();
  });
}

for (const hostname of ['localhost','127.0.0.2','preview.example.com']) {
  test(`local/preview ${hostname} never loads or emits GA`, async ({ browser }) => {
    const { page, context, requests } = await setup(browser, { hostname });
    const download=page.waitForEvent('download'); await page.locator('.hero-actions .primary').click(); await download;
    expect(requests.some(url=>/googletagmanager|google-analytics/.test(url))).toBe(false);
    expect(await events(page)).toHaveLength(0); await context.close();
  });
}

test('docs to download is a navigation, with one shared loader; return marker is anonymous', async ({ browser }) => {
  const { page, context, requests, errors } = await setup(browser);
  await page.goto('https://www.maipilot.jp/docs/growth/windows-minecraft-server-2026/');
  await page.locator('[data-event="docs_to_download"]').first().click();
  await expect(page).toHaveURL(/maipilot.jp\//);
  await page.goto('https://www.maipilot.jp/?from=mobile_handoff&email=DO_NOT_TRACK#private');
  const download=page.waitForEvent('download'); await page.locator('.hero-actions .primary').click(); await download;
  const payload=(await events(page,'download_click'))[0][2]; expect(payload.entry_point).toBe('mobile_handoff');
  const layer=await page.evaluate(()=>JSON.stringify(window.dataLayer)); expect(layer).not.toContain('DO_NOT_TRACK'); expect(layer).not.toContain('#private');
  expect(await page.locator('script[src="/analytics.js"]').count()).toBe(1);
  expect(errors).toEqual([]); expect(requests.filter(url=>/google-analytics/.test(url))).toEqual([]);
  await context.close();
});

for (const [slug, anchor] of [['haichi-map-installation', 'maipilot-map'], ['minecraft-server-recommended-specs', 'maipilot-memory']]) {
  for (const width of [320, 1440]) {
    test(`practical guide ${slug} at ${width}px`, async ({ browser }) => {
      const { page, context, errors, missing } = await setup(browser, { width, mobile: width === 320 });
      await page.goto(`https://www.maipilot.jp/docs/growth/${slug}/`);
      await expect(page.locator('.quick-answer')).toBeVisible();
      await expect(page.locator('link[rel="canonical"]')).toHaveAttribute('href', `https://www.maipilot.jp/docs/growth/${slug}/`);
      await expect(page.locator('.guide-actions a[href="/#download"]').first()).toContainText('無料');
      const operation = page.locator(`.hero a[href="#${anchor}"]`).first();
      await operation.click();
      await expect(page).toHaveURL(new RegExp(`#${anchor}$`));
      expect(await events(page, 'docs_to_download')).toHaveLength(0);
      expect(await events(page, 'download_click')).toHaveLength(0);
      await assertNoOverflow(page);
      await page.locator('.guide-actions a[href="/#pc-handoff"]').first().click();
      await expect(page.locator('.pc-handoff')).toHaveAttribute('open', '');
      await page.goBack();
      await expect(page.locator('h1')).toBeVisible();
      await assertNoOverflow(page);
      await page.evaluate(() => window.scrollTo(0, 0));
      await mkdir(screenshotRoot, { recursive: true });
      await page.screenshot({ path: path.join(screenshotRoot, `${slug}-${width}.png`), fullPage: true });
      expect(errors).toEqual([]); expect(missing).toEqual([]);
      await context.close();
    });
  }
}
