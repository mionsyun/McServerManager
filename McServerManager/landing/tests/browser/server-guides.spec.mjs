import { test, expect } from '@playwright/test';
import { readFile, stat, mkdir } from 'node:fs/promises';
import path from 'node:path';
const slugs = ['minecraft-server-memory-settings', 'minecraft-server-lag-fix', 'forge-fabric-mod-installation'];
const root = path.resolve('.output/public');
for (const slug of slugs) for (const width of [390, 1440]) test(`${slug} renders at ${width}px with working related guides`, async ({ browser }) => {
  const context = await browser.newContext({ viewport: { width, height: 900 } });
  const errors = [];
  await context.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.hostname !== 'guide-preview.test') return route.fulfill({status:204,body:''});
    let file = path.resolve(root, '.' + decodeURIComponent(url.pathname));
    if (!file.startsWith(root + path.sep)) return route.fulfill({status:400,body:''});
    try {
      if ((await stat(file)).isDirectory()) file = path.join(file, 'index.html');
      const types = {'.html':'text/html','.js':'text/javascript','.css':'text/css','.png':'image/png'};
      return route.fulfill({status:200,contentType:types[path.extname(file)] || 'application/octet-stream',body:await readFile(file)});
    } catch { return route.fulfill({status:404,body:'missing'}); }
  });
  const page = await context.newPage(); page.on('pageerror', e => errors.push(e.message));
  await page.goto(`https://guide-preview.test/docs/growth/${slug}/`);
  await expect(page.locator('h1')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await expect(page.locator('[data-faq-question]').first()).toBeAttached();
  const related = page.locator('a[href^="/docs/"]').last();
  await related.click(); await expect(page.locator('h1')).toBeVisible();
  await page.goBack(); await expect(page.locator('h1')).toBeVisible();
  expect(errors).toEqual([]);
  await mkdir('test-results/server-guides', {recursive:true});
  await page.screenshot({path:`test-results/server-guides/${slug}-${width}.png`,fullPage:true});
  await context.close();
});
