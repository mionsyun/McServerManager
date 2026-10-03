import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync, readdirSync } from 'node:fs';

const guides = ['haichi-map-installation', 'minecraft-server-recommended-specs'];
const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8');
for (const slug of guides) {
  test(`${slug}: practical answer, canonical URL, relevant operation and PC handoff`, () => {
    const html = read(`public/docs/growth/${slug}/index.html`);
    assert.equal((html.match(/<h1\b/g) || []).length, 1);
    const title = html.match(/<title>([^<]+)<\/title>/)?.[1];
    const description = html.match(/<meta name="description" content="([^"]+)"/)?.[1];
    assert.ok(title && description);
    for (const name of ['og:title', 'twitter:title']) assert.ok(html.includes(`${name}" content="${title}"`), `${name} matches title`);
    for (const name of ['og:description', 'twitter:description']) assert.ok(html.includes(`${name}" content="${description}"`), `${name} matches description`);
    const faq = [...html.matchAll(/<script[^>]*type="application\/ld\+json"[^>]*>([\s\S]*?)<\/script>/g)].map(([, json]) => JSON.parse(json)).find(data => data['@type'] === 'FAQPage');
    const visibleFaq = [...html.matchAll(/<dt data-faq-question>([\s\S]*?)<\/dt>\s*<dd data-faq-answer>([\s\S]*?)<\/dd>/g)].map(([, question, answer]) => ({ question, answer }));
    assert.deepEqual(faq.mainEntity.map(item => ({ question: item.name, answer: item.acceptedAnswer.text })), visibleFaq);
    assert.match(html, /class="quick-answer"/);
    assert.match(html, new RegExp(`rel="canonical" href="https://www.maipilot.jp/docs/growth/${slug}/"`));
    assert.match(html, /href="\/#download"[^>]*data-event="docs_to_download"/);
    assert.match(html, /href="\/#pc-handoff"[^>]*data-event="docs_to_download"/);
    assert.doesNotMatch(html, /<a\b[^>]*href="#[^"]*"[^>]*data-event="docs_to_download"/);
    assert.doesNotMatch(html, /data-event="download_click"/); // Navigation is never an installer click.
    assert.match(html, /個人・非商用利用は無料/);
    assert.equal((html.match(/src="\/analytics.js"/g) || []).length, 1);
    assert.ok(html.indexOf('href="/guide-enhancements.css"') > html.indexOf('</style>'), 'responsive overrides follow base inline CSS');
    for (const [, hash] of html.matchAll(/href="#([^" ]+)"/g)) assert.match(html, new RegExp(`id="${hash}"`));
    assert.ok(existsSync(new URL('../public/guide-enhancements.css', import.meta.url)));
  });
}
test('map guide matches source-backed stopped import and explicit overwrite behavior', () => {
  const html = read('public/docs/growth/haichi-map-installation/index.html');
  for (const phrase of ['サーバーを停止', '導入元フォルダ', '導入先ワールド名', '配布マップを導入', 'このワールドに切り替え', '同名ワールドは上書きして導入', '.schematic', '.mcworld']) assert.ok(html.includes(phrase), phrase);
  assert.doesNotMatch(html, /ドロップダウンから配布マップ|追加のMODやデータパックは不要|Done!|サーバーと同じバージョンのマップを選ぶと確実/);
  assert.match(html, /上書き前に自動バックアップするとは限りません/);
});
test('specs guide distinguishes Windows RAM, Java heap and process working set without player guarantees', () => {
  const html = read('public/docs/growth/minecraft-server-recommended-specs/index.html');
  for (const phrase of ['Xms (MB)', 'Xmx (MB)', '設定を保存', 'ワーキングセット', '統合版BDS', '人数だけでは決まりません']) assert.ok(html.includes(phrase), phrase);
  assert.doesNotMatch(html, /1〜5人|5〜10人|10〜20人|20〜50人|1人あたり約|3GHz以上|プラグイン10本|SSD.*必須|メモリ設定まで自動化/);
  assert.match(html, /公式の起動例/);
  assert.match(html, /設定方法の例/);
});
test('QA has no unconditional Google preconnect and stable multilingual menu selector', () => {
  assert.doesNotMatch(read('nuxt.config.ts'), /rel: "preconnect", href: "https:\/\/www.googletagmanager.com"/);
  assert.match(read('tests/browser/journey.spec.mjs'), /button\.hamburger\[aria-controls=/);
  assert.match(read('app.vue'), /pcHandoffOpen.value = isMobileDevice.value \|\| window.location.hash === "#pc-handoff"/);
});

test('published guides use current support terms and released product capabilities', () => {
  const docs = new URL('../public/docs/', import.meta.url);
  for (const file of readdirSync(docs, { recursive: true }).filter(name => name.endsWith('.html'))) {
    const html = readFileSync(new URL(file, docs), 'utf8');
    assert.doesNotMatch(html, /980円|完全無料・日本語対応|はい、Java 21以上が必要|UPnP対応ルーターであれば設定不要|CPU・TPS・プレイヤー数|CPU, TPS, players|サーバー停止時にトンネルを自動切断|Minecraftクライアント側の制限はありません/, file);
  }
  const setup = read('public/docs/growth/minecraft-server-setup-maipilot/index.html');
  assert.match(setup, /応援版100円・公式無料版と同機能/);
  assert.match(setup, /maipilot\.booth\.pm\/items\/8118402/);
  assert.match(setup, /個人・非商用利用/);
  assert.doesNotMatch(setup, /全部解決|一切不要|間隔と保存先を指定/);
  const troubleshooting = read('public/docs/troubleshooting/index.html');
  assert.match(troubleshooting, /自動再起動を有効にしている場合/);
  assert.match(troubleshooting, /When auto-restart is enabled/);
});
