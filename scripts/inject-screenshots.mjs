/**
 * inject-screenshots.mjs
 * 撮影済みスクリーンショットを各記事の HTML に差し込む
 *
 * Usage:
 *   node scripts/inject-screenshots.mjs
 */

import { readFileSync, writeFileSync, readdirSync, existsSync } from 'fs'
import { join, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const repoRoot   = join(__dirname, '..')
const screenshotDir = join(repoRoot, 'McServerManager/landing/public/images/screenshots')
const docsRoot   = join(repoRoot, 'McServerManager/landing/public/docs')

// -----------------------------------------------------------------
// 各記事に差し込む画像の定義
//   article: docs/growth/ 以下のディレクトリ名
//   insertions: [ { after: '差し込む直前の HTML 文字列', image, alt, caption } ]
// -----------------------------------------------------------------
const articleScreenshots = [
  {
    article: 'forge-fabric-mod-installation',
    insertions: [
      {
        after: '<h2',  // 最初の h2 の直前にヒーロー画像
        image: '05-addon-tab.png',
        alt: 'MaiPilot MOD/プラグインタブのスクリーンショット',
        caption: 'MaiPilot の MOD/プラグインタブ。ファイルをドロップするだけで導入できます。',
        insertBefore: true,
      },
      {
        after: 'MODタブ',
        image: '05-addon-tab.png',
        alt: 'MOD タブの表示',
        caption: 'MOD/プラグインタブ：追加済みの MOD が一覧表示されます。',
      },
    ],
  },
  {
    article: 'paper-spigot-plugin-installation',
    insertions: [
      {
        after: 'プラグインタブ',
        image: '05-addon-tab.png',
        alt: 'プラグインタブのスクリーンショット',
        caption: 'プラグインタブ：追加済みのプラグイン一覧と追加ボタン。',
      },
    ],
  },
  {
    article: 'haichi-map-installation',
    insertions: [
      {
        after: 'ワールドタブ',
        image: '04-world-tab.png',
        alt: 'ワールドタブのスクリーンショット',
        caption: 'ワールドタブ：使用するワールドを切り替えられます。',
      },
    ],
  },
  {
    article: 'resource-pack-distribution',
    insertions: [
      {
        after: 'リソースパックタブ',
        image: '06-resource-pack-tab.png',
        alt: 'リソースパックタブのスクリーンショット',
        caption: 'リソースパックタブ：配布するリソースパックを設定します。',
      },
    ],
  },
  {
    article: 'minecraft-server-tatekkata-2026',
    insertions: [
      {
        after: 'サーバー一覧',
        image: '01-main-window.png',
        alt: 'MaiPilot メイン画面のスクリーンショット',
        caption: 'MaiPilot のメイン画面。複数サーバーをまとめて管理できます。',
      },
      {
        after: 'コンソール',
        image: '02-server-console.png',
        alt: 'コンソールタブのスクリーンショット',
        caption: 'コンソールタブ：サーバーログをリアルタイムで確認できます。',
      },
    ],
  },
  {
    article: 'port-open-checklist',
    insertions: [
      {
        after: 'ネットワーク',
        image: '03-network-tab.png',
        alt: 'ネットワークタブのスクリーンショット',
        caption: 'ネットワークタブ：ポート開放状況の確認と UPnP 設定。',
      },
    ],
  },
  {
    article: 'server-properties-recommended-settings',
    insertions: [
      {
        after: '設定タブ',
        image: '07-settings-tab.png',
        alt: '設定タブのスクリーンショット',
        caption: '設定タブ：server.properties をGUIで編集できます。',
      },
    ],
  },
]

// -----------------------------------------------------------------
// 画像 HTML を生成
// -----------------------------------------------------------------
function buildImageHtml(image, alt, caption) {
  const src = `/images/screenshots/${image}`
  return `
<figure class="doc-screenshot">
  <img src="${src}" alt="${alt}" loading="lazy" style="max-width:100%;border-radius:8px;border:1px solid #e0e0e0;box-shadow:0 2px 8px rgba(0,0,0,.12);">
  <figcaption style="text-align:center;font-size:.85em;color:#666;margin-top:.5em;">${caption}</figcaption>
</figure>`
}

// -----------------------------------------------------------------
// メイン処理
// -----------------------------------------------------------------
let totalInserted = 0

for (const { article, insertions } of articleScreenshots) {
  const htmlPath = join(docsRoot, 'growth', article, 'index.html')
  if (!existsSync(htmlPath)) {
    console.log(`[skip] ${article} — HTML が見つかりません`)
    continue
  }

  let html = readFileSync(htmlPath, 'utf-8')
  let changed = false

  for (const ins of insertions) {
    const { after, image, alt, caption, insertBefore } = ins

    // 画像ファイルが存在するかチェック
    if (!existsSync(join(screenshotDir, image))) {
      console.log(`  [skip] ${image} — 画像ファイルがありません (先にキャプチャしてください)`)
      continue
    }

    // すでに差し込まれていたらスキップ
    if (html.includes(`/images/screenshots/${image}`)) {
      console.log(`  [already] ${article} — ${image} は既に差し込み済み`)
      continue
    }

    const imgHtml = buildImageHtml(image, alt, caption)
    const idx = html.indexOf(after)
    if (idx === -1) {
      console.log(`  [not found] "${after}" が ${article}/index.html に見つかりません`)
      continue
    }

    if (insertBefore) {
      html = html.slice(0, idx) + imgHtml + '\n' + html.slice(idx)
    } else {
      // after 文字列の末尾直後に差し込む
      const insertAt = idx + after.length
      html = html.slice(0, insertAt) + imgHtml + html.slice(insertAt)
    }
    changed = true
    totalInserted++
    console.log(`  [+] ${article} — ${image}`)
  }

  if (changed) {
    writeFileSync(htmlPath, html, 'utf-8')
    console.log(`  Saved: ${article}/index.html`)
  }
}

console.log(`\n完了: ${totalInserted} 件の画像を差し込みました。`)
