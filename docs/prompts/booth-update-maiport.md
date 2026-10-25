# プロンプト: MaiPort の BOOTH 商品ページ更新（PC 操作エージェント用）

> 使い方: 「--- ここから ---」〜「--- ここまで ---」を、PC を操作できるエージェント
> （Claude のコンピューター操作 / Cowork など）にそのまま渡す。
> BOOTH へのログインだけは自分で済ませておくこと。エージェントにパスワードは渡さない。

--- ここから ---

あなたは私の Windows PC を操作して、BOOTH で販売している「MaiPort」の商品ページを
最新版（v1.2.0）に更新する作業者です。以下の【絶対ルール】を守り、【作業手順】を順番に進めてください。

## 【絶対ルール】

1. **BOOTH で「保存」「公開」を押す前に、必ず私に確認を取る。** 変更内容（説明文・画像の並び・ファイル）を一覧で見せ、私が「OK」と言うまで押さない
2. **価格・公開状態・商品名・カテゴリ・バリエーションは変更しない**
3. **バリエーションと既存の配布ファイルは削除しない。** 削除すると過去の購入者がダウンロードできなくなる
4. ショップ内の **MaiPort 以外の商品には触れない**。MaiPort の商品が見つからなければ、新規作成せずに止まって私に報告する
5. **パスワードを入力しない。** ログイン画面が出たら止まって私に頼む
6. スクリーンショット撮影のために、**実際のポート開放・解除（「ポートを開放する」「解除」「この内容で開放する」ボタン）を押さない**。UAC の確認画面が出たら「いいえ」
7. **公開する画像に IP アドレスと Windows のユーザー名を写さない。** 写った場合は塗りつぶす
8. 迷ったら推測で進めず、止まって私に聞く

## 【前提情報】

- ショップ: https://maipilot.booth.pm （管理画面は BOOTH ログイン後の「ショップ管理」→「商品管理」）
- 商品: MaiPort（ポート開放ツール）
- ソースコード: https://github.com/mionsyun/McServerManager のブランチ `claude/epic-carson-dx3xvg`、フォルダ `MaiPort/`
- 最新版: **v1.2.0**（アプリのヘッダーに「MaiPort v1.2.0」と表示される）
- 配布物: .NET 同梱の単一 exe（約 68MB）を zip にしたもの

### 前回から変わったこと（説明文の根拠。ここに無いことは書かない）

v1.2.0
- Minecraft 統合版サーバー（BDS）に対応。server.properties を読み込むと transport（NetherNet / RakNet）を判定し、開けるべきポートを一覧表示してまとめて開放できる
  - NetherNet: TCP の server-port（シグナリング）＋ server-udp-ports の UDP 範囲
  - RakNet: UDP の server-port と server-portv6
- ポート範囲の開放（例: 49152-49200）。ファイアウォールは範囲を1つの規則で、ルーター（UPnP）は1ポートずつ最大64ポートまで
- プリセットに「統合版 NetherNet」「統合版 RakNet」を追加。Valheim を 2456-2458 に修正

v1.1.0
- 画面デザインを全面リニューアル（明るい配色・余白の広いレイアウト）
- 入力中のポートが「開放中／未開放」かを画面上部に大きく表示
- プロトコルをボタンで切り替え（UDP / TCP / UDP + TCP）
- Enter キーで開放、F5 で状態を再確認
- ポート番号欄は数字のみ入力可、不正な値はその場で枠の色と注意文で表示
- 処理中はボタン表示とプログレスバーで表示、ログは最新行へ自動スクロール
- 「接続先をコピー」で「グローバルIP:ポート」をクリップボードへ
- ヘッダーにバージョンを表示

v1.0.0（初版）
- ルーター（UPnP）のポート転送と Windows ファイアウォールの受信許可をまとめて設定・解除
- 開放したポートの記録と一覧からの解除、ポートを使用中のアプリの確認、LAN IP・グローバル IP・UPnP ルーターの有無の表示

## 【作業手順】

### Step 1. 現状の確認（変更はしない）

1. BOOTH の商品管理で MaiPort の編集画面を開く
2. 次を記録して私に報告する
   - 現在の商品説明の全文
   - 商品画像の枚数と、それぞれ何が写っているか（旧デザインの画面かどうか）
   - バリエーションの名前・価格と、それぞれに付いている配布ファイル名
   - 現在掲載されているバージョン（説明文や画像から読み取れる範囲で）
3. **既存の商品画像をすべて `C:\MaiPortDemo\booth-backup\` に保存**しておく（元に戻せるように）

→ ここで一度止まり、報告して私の「続けて」を待つ。

### Step 2. 配布ファイル（zip）を作る

PowerShell（`powershell`。管理者でなくてよい）で実行する。

```powershell
# .NET SDK が無ければ入れる（入れたら PowerShell を開き直す）
dotnet --version
# エラーなら:
winget install --id Microsoft.DotNet.SDK.8 -e

# git が無ければ入れる（入れたら PowerShell を開き直す）
git --version
# エラーなら:
winget install --id Git.Git -e

# ソースを取得（既にあれば git pull だけ）
cd $HOME
git clone https://github.com/mionsyun/McServerManager.git
cd McServerManager
git checkout claude/epic-carson-dx3xvg
git pull

# ビルド・テスト・exe 生成
powershell -ExecutionPolicy Bypass -File scripts\build-maiport.ps1

# バージョン確認（1.2.0 であること）
(Get-Item dist\maiport-selfcontained\MaiPort.exe).VersionInfo.ProductVersion
```

テストが失敗したら止まって私に報告する。

次に、下の【README.txt】の内容を `dist\README.txt` として保存し、zip にまとめる。
Windows のメモ帳で文字化けしないよう、PowerShell 5.1 の `Set-Content -Encoding UTF8`（BOM 付き）で書く。

```powershell
@'
（ここに【README.txt】の本文を貼る）
'@ | Set-Content -Encoding UTF8 dist\README.txt

Compress-Archive -Force `
  -Path dist\maiport-selfcontained\MaiPort.exe, dist\README.txt `
  -DestinationPath dist\MaiPort_v1.2.0_win-x64.zip
Get-Item dist\MaiPort_v1.2.0_win-x64.zip | Select-Object Name, Length
```

### Step 3. スクリーンショットを撮る

#### 3-1. デモ用データを用意する（本物の設定は触らない）

```powershell
New-Item -ItemType Directory -Force C:\MaiPortDemo, C:\MaiPortDemo\shots | Out-Null

# 1) 現在の開放記録をバックアップ（無ければスキップされる）
$rules = "$env:LOCALAPPDATA\MaiPort\rules.json"
if (Test-Path $rules) { Copy-Item $rules C:\MaiPortDemo\rules.json.bak -Force }

# 2) 見本用の開放記録を置く（画面表示のためだけ。実際の開放はしない）
New-Item -ItemType Directory -Force "$env:LOCALAPPDATA\MaiPort" | Out-Null
$demo = @'
[
  { "Id": "demo1", "Port": 8211,  "EndPort": 8211,  "Protocol": "Udp",  "Description": "Palworld",        "UpnpOpened": true,  "FirewallOpened": true, "CreatedAt": "2026-10-25T20:00:00+09:00" },
  { "Id": "demo2", "Port": 19132, "EndPort": 19133, "Protocol": "Udp",  "Description": "Bedrock RakNet",  "UpnpOpened": true,  "FirewallOpened": true, "CreatedAt": "2026-10-25T20:05:00+09:00" },
  { "Id": "demo3", "Port": 25565, "EndPort": 25565, "Protocol": "Both", "Description": "Minecraft Java",  "UpnpOpened": false, "FirewallOpened": true, "CreatedAt": "2026-10-25T20:10:00+09:00" }
]
'@
[System.IO.File]::WriteAllText($rules, $demo)   # BOM なし UTF-8

# 3) 見本用の server.properties（ユーザー名がパスに入らない場所に置く）
@'
transport=nethernet
server-port=19132
server-udp-ports=49152-49200
'@ | Set-Content -Encoding ASCII C:\MaiPortDemo\server.properties
```

#### 3-2. 撮影

`dist\maiport-selfcontained\MaiPort.exe` を起動する（SmartScreen が出たら「詳細情報」→「実行」）。
ウィンドウは画面に収まる範囲で縦に広げてよい。**「接続先をコピー」は押さない**（ログに IP が出るため）。

撮影は「MaiPort のウィンドウをクリックしてアクティブにする → **Alt + PrintScreen**」の直後に、次で保存する。

```powershell
(Get-Clipboard -Format Image).Save("C:\MaiPortDemo\shots\<ファイル名>.png")
```

| ファイル名 | 撮る状態 |
|---|---|
| `01_main.png` | 起動直後の一番上（ヘッダーの v1.2.0、緑の「開放中」と UDP 8211、プリセット、入力欄が写る）※サムネイル用 |
| `02_bedrock.png` | 下へスクロールし「Minecraft 統合版サーバー (Bedrock)」→「server.properties を読み込む」で `C:\MaiPortDemo\server.properties` を選んだ後の判定結果（TCP 19132 と UDP 49152-49200 の2行） |
| `03_presets.png` | 一番上に戻り、プリセット「統合版 RakNet」を押した状態（ポート欄が 19132-19133、UDP が選択） |
| `04_list.png` | 「開放中のポート」カード（見本の3件が並んでいる状態） |
| `05_validation.png` | ポート番号欄に `70000` と入力した状態（枠がオレンジ＋注意文）。撮影後は 8211 に戻す |

#### 3-3. 写り込みチェックと加工

各画像を目視し、**IP アドレス（`192.168.` で始まるもの、グローバル IP）、Windows のユーザー名、メールアドレス**が写っていないか確認する。
「この PC とネットワーク」カードやログ欄に写っていたら、その範囲を塗りつぶす。
その後、BOOTH 用に 1280×1280 の正方形に整える（引き伸ばさず、余白を足して中央に置く）。

```powershell
Add-Type -AssemblyName System.Drawing

function Hide-Region {
    param([string]$Path, [int]$X, [int]$Y, [int]$Width, [int]$Height)
    $src = [System.Drawing.Image]::FromFile($Path)
    $bmp = New-Object System.Drawing.Bitmap $src
    $src.Dispose()
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#C9D2DE'))
    $g.FillRectangle($brush, $X, $Y, $Width, $Height)
    $brush.Dispose(); $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function ConvertTo-Square {
    param([string]$Source, [string]$Destination, [int]$Size = 1280, [int]$Margin = 60, [string]$Background = '#F4F6FA')
    $src = [System.Drawing.Image]::FromFile($Source)
    $canvas = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.ColorTranslator]::FromHtml($Background))
    $limit = $Size - 2 * $Margin
    $scale = [Math]::Min(1.0, [Math]::Min($limit / $src.Width, $limit / $src.Height))
    $w = [int]($src.Width * $scale); $h = [int]($src.Height * $scale)
    $g.DrawImage($src, [int](($Size - $w) / 2), [int](($Size - $h) / 2), $w, $h)
    $g.Dispose(); $src.Dispose()
    $canvas.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
}

# 例: 写り込みがあれば先に塗る（座標は画像を見て決める）
# Hide-Region -Path C:\MaiPortDemo\shots\04_list.png -X 180 -Y 620 -Width 360 -Height 60

New-Item -ItemType Directory -Force C:\MaiPortDemo\booth | Out-Null
Get-ChildItem C:\MaiPortDemo\shots\*.png | ForEach-Object {
    ConvertTo-Square -Source $_.FullName -Destination ("C:\MaiPortDemo\booth\" + $_.Name)
}
```

加工後の画像をもう一度目視確認する。

#### 3-4. 後片付け（必ず実行）

MaiPort を閉じてから、開放記録を元に戻す。

```powershell
$rules = "$env:LOCALAPPDATA\MaiPort\rules.json"
if (Test-Path C:\MaiPortDemo\rules.json.bak) {
    Copy-Item C:\MaiPortDemo\rules.json.bak $rules -Force
} else {
    Remove-Item $rules -ErrorAction SilentlyContinue
}
```

### Step 4. BOOTH の商品ページを編集する

1. **商品説明**: 下の【掲載文】に置き換える。BOOTH の説明欄はプレーンテキストなので、そのまま貼る。
   既存の説明に私が独自に書いた文（問い合わせ先、利用規約、他商品への案内など）があれば、消さずに【掲載文】の末尾に残し、どこを残したか報告する
2. **商品画像**: `C:\MaiPortDemo\booth\` の画像をアップロードし、次の順に並べる
   1. `01_main.png`（サムネイル）
   2. `02_bedrock.png`
   3. `03_presets.png`
   4. `04_list.png`
   5. `05_validation.png`
   旧デザインの画面の画像は外してよい（Step 1 でバックアップ済みであること）。ロゴや告知画像など、画面写真以外の画像は残して私に確認する
3. **配布ファイル**: 「ファイルの追加・管理」から `MaiPort_v1.2.0_win-x64.zip` をアップロードし、**既存のバリエーションに紐付ける**
   - 旧バージョンのファイルは**削除しない**。非公開に切り替えられる場合は非公開にし、できなければ残したまま私に報告する
4. ここで**保存せずに止まり**、次を一覧にして私に確認を取る
   - 説明文の変更前と変更後
   - 画像の並び（ファイル名と順番）と、外した画像
   - 配布ファイルの一覧（ファイル名・公開状態・紐付くバリエーション）
5. 私が OK と言ったら保存する

### Step 5. 公開ページの確認と報告

1. 公開側の商品ページ（https://maipilot.booth.pm/ 内の MaiPort）を開き、説明文・画像の並び・サムネイルを確認する
2. 次の形で報告する

```
■ 完了報告
- 説明文: 更新済み（残した独自記述: …）
- 画像: 5枚に差し替え（外した旧画像: … / バックアップ: C:\MaiPortDemo\booth-backup\）
- 配布ファイル: MaiPort_v1.2.0_win-x64.zip を追加（旧ファイル: 非公開 / 残置）
- 開放記録（rules.json）: 復元済み
- 気になった点: …
```

## 【掲載文】（商品説明にそのまま貼る）

MaiPort ― ゲームサーバーのポート開放を、ボタンひとつで。

ルーターのポート転送（UPnP）と Windows ファイアウォールの受信許可を、まとめて設定・解除できる Windows 用ツールです。
Palworld や Minecraft のサーバーを友達に公開したいときに、ルーターの管理画面やコマンドを触らずに済みます。

■ v1.2.0 の主な更新
・Minecraft 統合版サーバーに対応
　server.properties を読み込むだけで、接続方式（NetherNet / RakNet）を判定し、開けるべきポートを自動で一覧表示。そのまままとめて開放できます
・ポート範囲の開放に対応（例: 49152-49200）
・プリセットに「統合版 NetherNet」「統合版 RakNet」を追加

■ v1.1.0 の主な更新
・画面デザインを全面リニューアル（明るく見やすいレイアウト）
・入力中のポートが「開放中／未開放」かを画面上部に大きく表示
・Enter キーで開放、F5 キーで状態を再確認
・ポート番号は数字のみ入力でき、間違いはその場で表示
・「接続先をコピー」で「グローバルIP:ポート」をコピー。友達にそのまま送れます

■ できること
・ルーター（UPnP）へのポート転送の追加・削除
・Windows ファイアウォールの受信許可の追加・削除
・TCP / UDP / 両方 を選択
・ポート範囲の指定（例: 49152-49200）
・プリセット: Palworld / Minecraft Java / 統合版 NetherNet / 統合版 RakNet / ARK / Valheim / Terraria
・Minecraft 統合版サーバーの server.properties から必要なポートを自動判定
・開放したポートの一覧表示と、ワンクリックでの解除
・そのポートを使っているアプリの確認
・LAN IP / グローバル IP / UPnP 対応ルーターの有無を表示

■ 動作環境
・Windows 10 / 11（64bit）
・.NET のインストールは不要です（同梱済み）

■ ご注意
・ルーターへの設定は、ルーターの UPnP が有効な場合のみ行えます。UPnP を使えない場合は、ルーターの管理画面で手動設定が必要です
・回線の方式（CGNAT・共有IP など）によっては、ポート開放そのものができない場合があります
・ファイアウォールの設定時に Windows の確認画面（ユーザーアカウント制御）が表示されます
・初回起動時に Windows の保護画面（SmartScreen）が表示された場合は「詳細情報」→「実行」で起動できます
・Minecraft 統合版サーバーの接続方式は、サーバーのバージョンによって変わります。MaiPort は server.properties の設定に合わせて必要なポートを判定します

■ 更新履歴
v1.2.0　Minecraft 統合版サーバー対応、ポート範囲の開放、プリセット追加
v1.1.0　デザイン刷新、操作性の改善、接続先コピー
v1.0.0　初版公開

## 【README.txt】（zip に同梱。UTF-8 BOM 付きで保存）

MaiPort v1.2.0 ― ポート開放ツール

■ 使い方
1. MaiPort.exe をダブルクリックして起動します
2. プリセットを選ぶか、ポート番号とプロトコルを入力します
   （範囲は「49152-49200」のように入力できます）
3. 「ポートを開放する」を押します（Enter キーでも可）
4. Windows の確認画面（ユーザーアカウント制御）が出たら「はい」を押します
5. 不要になったら「開放中のポート」の一覧から「解除」を押します

■ Minecraft 統合版サーバーの場合
「Minecraft 統合版サーバー (Bedrock)」から server.properties を選ぶと、
開けるべきポートを自動で判定します。「この内容で開放する」でまとめて開放できます。

■ 動作環境
Windows 10 / 11（64bit）。.NET のインストールは不要です。

■ はじめて起動するとき
「Windows によって PC が保護されました」と表示された場合は、
「詳細情報」→「実行」を押してください。

■ アンインストール
1. MaiPort で、開放中のポートをすべて「解除」します
2. MaiPort.exe を削除します
3. 記録を消す場合は、次のフォルダを削除します
   %LOCALAPPDATA%\MaiPort

--- ここまで ---
