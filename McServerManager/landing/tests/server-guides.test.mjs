import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
const slugs = ['minecraft-server-memory-settings', 'minecraft-server-lag-fix', 'forge-fabric-mod-installation'];
const read = slug => readFileSync(new URL(`../public/docs/growth/${slug}/index.html`, import.meta.url), 'utf8');
for (const slug of slugs) test(`${slug}: stable canonical, valid internal links and synchronized FAQ`, () => {
  const html = read(slug);
  assert.equal((html.match(/<h1\b/g) || []).length, 1);
  assert.ok(html.includes(`rel="canonical" href="https://www.maipilot.jp/docs/growth/${slug}/"`));
  for (const [, href] of html.matchAll(/href="(\/[^"?#]*)[^"]*"/g)) {
    const file = `../public${href}${href.endsWith('/') ? 'index.html' : ''}`;
    if (href !== '/') assert.ok(existsSync(new URL(file, import.meta.url)), href);
  }
  const data = [...html.matchAll(/<script[^>]*type="application\/ld\+json"[^>]*>([\s\S]*?)<\/script>/g)].map(([, json]) => JSON.parse(json));
  const faq = data.find(x => x['@type'] === 'FAQPage');
  const visible = [...html.matchAll(/<dt data-faq-question>([\s\S]*?)<\/dt>\s*<dd data-faq-answer>([\s\S]*?)<\/dd>/g)].map(([, q, a]) => ({q, a}));
  assert.deepEqual(faq.mainEntity.map(x => ({q:x.name, a:x.acceptedAnswer.text})), visible);
  assert.doesNotMatch(html, /pc-handoff|template-safe-core|Pro機能/);
});
test('memory guide separates Java heap, process and BDS and avoids blanket sizing', () => {
  const html = read(slugs[0]);
  for (const text of ['初期サイズ', '最大サイズ', 'ワーキングセット', 'ヒープ外', 'Bedrock Dedicated Server', '一度に一つ']) assert.ok(html.includes(text), text);
  assert.doesNotMatch(html, /50〜75%|1Gずつ増やす|バニラ 1〜5人|Paper 5〜15人/);
});
test('lag guide uses qualified current profiling and no invalid property', () => {
  const html = read(slugs[1]);
  assert.match(html, /1\.21/); assert.match(html, /spark profiler start --timeout 600/);
  assert.doesNotMatch(html, /max-entities=150|\/timings report|GCによる「一瞬フリーズ」を大幅に軽減/);
  assert.match(html, /start-script-gen/);
});
test('mod guide distinguishes installation sides and compatibility', () => {
  const html = read(slugs[2]);
  for (const text of ['サーバー専用', 'クライアント専用', '依存', '/docs/java-setup/', '/docs/troubleshooting/']) assert.ok(html.includes(text), text);
  assert.doesNotMatch(html, /はい、必要です。サーバーとクライアントの両方に同じMOD/);
});
