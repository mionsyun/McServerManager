# McServerManager 要件定義書（現行仕様）

## 1. 目的
- Windows 上で Minecraft ローカルサーバーを GUI で簡単に管理できるようにする。
- サーバー構築に必要な作業（作成/起動/設定/バックアップ/公開補助）を一画面で完結させる。
- 初心者でも迷いにくい導線と、復旧のための情報提示を提供する。

## 2. 対象範囲
- 対象: Windows 10/11 で Minecraft サーバーを運用したいユーザー。
- 対象外: macOS/Linux 向け UI、クラウドホスティング機能。

## 3. 提供物
- デスクトップアプリ（WPF/.NET 8）
- インストーラ（Inno Setup）
- ランディングページ（Nuxt 3 静的生成）
- セットアップノート（/docs 静的ページ）

## 4. 機能要件（アプリ）
### 4.1 サーバー作成
- EULA 同意を必須にした新規作成ウィザード。
- 保存先/メモリ/ポート/サーバー種別/バージョンを指定。

### 4.2 起動・停止・監視
- 起動/停止/再起動が可能。
- 起動ログのリアルタイム表示とコマンド送信。
- 起動完了判定: ログの `Done (` を検知。
- 停止時: `stop` 送信 → タイムアウトで強制終了。

### 4.3 設定編集
- `server.properties` の主要項目を GUI で編集。
- 実行中の変更は保存できるが反映は再起動後。

### 4.4 ワールド管理
- ワールド一覧/切替/削除/新規作成。
- バックアップ/復元機能。

### 4.5 バージョン管理
- Minecraft バージョン取得。
- server.jar 再取得。
- 変更前バックアップを実施。

### 4.6 ネットワーク支援
- LAN IP/グローバル IP の表示。
- 待受チェック（ローカル TCP 接続確認）。
- 外部公開チェックリストを表示。
- ポート使用中プロセスの検出と強制終了（確認あり）。

### 4.7 Firewall / UPnP
- Firewall 受信ルールの作成/削除/再作成（TCP/UDP）。
- UPnP 自動ポート開放/閉鎖（Open.NAT）。
- 管理者権限が必要な場合は UAC で昇格。

### 4.8 互換性チェック
- 起動時に必要 Java バージョンを判定し警告。

### 4.9 クラッシュ対応
- 予期せぬ終了時に警告ダイアログを表示。
- 直近ログ/ExitCode/ログフォルダ/クラッシュレポートへの導線を提示。
- 自動再起動（設定で有効化、秒数指定）。

### 4.10 導線
- サーバー保存先を表示し、ワンクリックで開く。
- ログフォルダ/クラッシュレポートをワンクリックで開く。

### 4.11 UI/UX
- ダーク/ライトテーマ。
- 初回ガイドとチュートリアルオーバーレイ。

## 5. サーバー種別/バージョン取得
- 対応種別: Vanilla / Forge / Spigot / Purpur / Paper / Fabric
- 取得元:
  - Mojang マニフェスト
  - PaperMC API
  - Purpur API
  - Fabric Loader/Installer API
  - Forge Maven メタデータ
  - Spigot BuildTools.jar

## 6. 非機能要件
### 6.1 対応環境
- Windows 10/11
- .NET 8.0 (WPF)
- Java (server.jar 実行用)
- ネットワーク接続（バージョン取得/公開 IP 取得など）

### 6.2 パフォーマンス
- コンソールログは最大 5000 行をメモリ保持。

### 6.3 ローカル完結
- サーバーデータは端末内に保存され、クラウドへ自動送信しない。

## 7. データ保存仕様
- ルート: `%APPDATA%\BlockPilot`
- `appsettings.json`: アプリ設定
- `servers/<serverId>/config.json`: サーバー構成
- `servers/<serverId>/server.properties`: Minecraft 設定
- `servers/<serverId>/server.jar`, `logs/`, `backups/`
- `cache/version_manifest.json`: バージョンキャッシュ

## 8. 設定項目
### 8.1 appsettings.json
- `HasShownFirstRun`
- `HasCompletedTutorial`
- `ServerDirectories`
- `EnableUpnp`
- `PromptUpnp`
- `Theme` (`Dark` / `Light`)

### 8.2 config.json
- `ServerId`, `Name`, `Type`, `Version`, `DirectoryPath`
- `JavaPath`, `MemoryXmsMb`, `MemoryXmxMb`
- `Port`, `MaxPlayers`, `OnlineMode`, `Motd`
- `WorldName`, `Seed`, `Difficulty`, `GameMode`, `Pvp`, `ViewDistance`, `SpawnProtection`
- `CreatedAt`, `LastStartedAt`
- `Firewall.TcpRuleName`, `Firewall.UdpRuleName`

## 9. 外部通信（現行）
- `https://launchermeta.mojang.com` (バージョン取得)
- `https://api.papermc.io` (Paper)
- `https://api.purpurmc.org` (Purpur)
- `https://meta.fabricmc.net` (Fabric)
- `https://maven.minecraftforge.net` (Forge)
- `https://hub.spigotmc.org` (Spigot BuildTools)
- `https://api.ipify.org` (公開 IP)
- 注: BuildTools 実行時に Git/SpigotMC 等へ追加アクセスが発生する場合がある。

## 10. 配布/インストール
- Inno Setup でインストーラ生成。
- .NET を同梱する self-contained 配布。
- 署名フックはビルドスクリプトにより対応可能（PFX/Signtool）。

## 11. LP/Docs 仕様
### 11.1 ランディングページ
- Nuxt 3 による静的生成。
- 日英切替（ローカル保存 + ブラウザ言語）。
- セクション: Hero / Features / Guided Setup / How It Works / Java compatibility / Security / Download / FAQ

### 11.2 セットアップノート (/docs)
- 日英切替、トップへの戻り導線。
- セットアップ手順、要件、注意点、つまずき対策。
- セキュリティ/プライバシー方針と通信先一覧を明記。

## 12. 既知の挙動/注意
- 設定変更は再起動後に反映。
- UPnP/Firewall は環境により失敗する場合あり。
- IPv4 前提でアドレス表示/待受チェックを実施。

## 13. LP/Docs 運用目的（ゴール）
- LPとDocsで「この製品は安全・透明・信用できる」と感じてもらう。
- DL（コンバージョン）を落とさずに、必要なユーザーのみDocs経由で収益導線へ誘導する。
- SNS発信も「安心設計」軸で差別化し、売り込み臭を抑える。

## 14. LP/Docs の制約（重要）
- LPのDownloadボタンより上に広告/アフィ導線を置かない。
- LPは「おすすめ！」ではなく状況別の選択肢として提示する。
- アフィは必ず（PR）表記を明確にする（LP/Docs）。
- 通信・権限・データ取扱いを具体的に明記し、不信を先回りで潰す。
- 収益導線は /docs 側で完結（LPは最小の案内 + /docs へのリンク）。
- 不必要な誇張表現は禁止（最強/絶対/100%安全 等）。

## 15. LP改善の要件（セクション別）
### 15.1 Hero
- 目的: 初心者でも迷わない、復旧導線がある、ローカル完結の安心を1行で伝える。
- 追加する短文（案）:
  - WindowsでMinecraftサーバーを、迷わず作って・動かして・戻せるGUI
  - データはPC内。必要最小限の通信のみ。権限が必要な操作は明示します

### 15.2 Features
- 目的: 機能列挙より「安心に効く体験」を前に出す。
- 追加する見せ方:
  - クラッシュ時の復旧導線（ログ/レポートへワンクリック）
  - 変更は保存できるが反映は再起動後（事故防止の説明）
  - バックアップ/復元が標準搭載

### 15.3 Guided Setup / How It Works
- 目的: 初回導線の安心。危ない設定を勝手に触らない説明。
- 文面指示:
  - EULA同意が必須
  - 必要な設定のみ
  - 何をするか説明しながら進む

### 15.4 Java compatibility
- 目的: 初心者が詰まるポイントを先回り。
- 文面指示:
  - 必要Java判定 → 警告
  - 推奨の考え方（バージョンによる）
  - 失敗時の復旧導線

### 15.5 Security（最重要）
- 目的: ここで信用を作る。曖昧にせず具体的に。
- 必ず入れる箇条書き:
  - サーバーデータは端末内に保存され、クラウドへ自動送信しません
  - 外部通信はバージョン取得・公開IP取得など必要最小限です
  - 通信先一覧をDocsで公開しています（リンク）
  - Firewall/UPnPなど管理者権限が必要な操作は、実行前に明示し、UACで昇格します
  - ログ/クラッシュレポートはローカル表示・ローカル保存です

### 15.6 Download
- 目的: ここが主役。状況別カードを追加（アフィは直接貼らない）。
- UI指示（カード3枚）:
  - このPCでローカル運用（Download）
  - 同じWi-FiでLAN参加（/docs/lan）
  - 24時間稼働・外部公開したい（/docs/24-7-hosting）（PR）
- 注意: 外部のアフィリンクはLPでは貼らない。必ずdocsに送る。

### 15.7 FAQ（収益導線の安全な置き場）
- 追加必須3項目（Q/Aドラフト作成対象）:
  - PCをつけっぱなしにできません → VPS案内（/docs/24-7-hosting）
  - 外部公開ができません → チェックリスト（/docs/port-forwarding）
  - クラッシュした/ログが分からない → 復旧導線の説明（/docs/troubleshooting）

## 16. /docs 新規ページ要件（収益はdocsで、信頼最優先）
### 16.1 /docs/24-7-hosting（PR）
- 目的: 常時稼働したい人だけに、VPS/ゲームサーバーを選択肢として提示。
- 冒頭に必須表記: 「※本ページにはプロモーション（PR）が含まれます」
- 見出し構成（必須）:
  - どんな人がVPS向き？（Yes/Noチェック）
  - 自宅PC運用 vs VPS（比較表）
  - 料金目安と最低構成
  - 候補A/B/C（PR）※選定基準（料金/解約/日本語/管理画面/安定性）
  - McServerManagerからの移行手順
  - よくある落とし穴（CGNAT/回線/ポート）
  - 免責・PR表記

### 16.2 /docs/port-forwarding
- 目的: 公開できない人を救う。最後に「それでも難しいならVPS（PR）」を控えめに置く。
- 内容例: UPnP失敗、二重ルータ、ポート競合、Firewall、IPv4前提、チェック手順。

### 16.3 /docs/privacy-and-network（安心の根拠ページ）
- 目的: 通信先・権限・ログ扱いを仕様として公開する。
- 必須項目:
  - 外部通信先一覧（要件定義のドメイン）
  - 何のために通信するか（1行説明）
  - 管理者権限が必要な操作一覧（Firewall/UPnP）
  - データ保存場所（%APPDATA% 以下、servers/構造）

## 17. 収益導線の文面ルール
- 見出しやリンクに「（PR）」を付ける。
- 説明は1〜2行で十分。煽らない。
- 文例: 「PCをつけっぱなしにできない場合は、VPSで常時稼働が楽なことがあります」
- 「比較はこちら」ボタンは /docs/24-7-hosting へ（外部直リンクしない）。

## 18. 計測設計（GA4想定）
- イベント名:
  - download_click
  - docs_24_7_hosting_click
  - outbound_affiliate_click
- UTM命名規則:
  - utm_source: x / youtube / discord
  - utm_medium: social
  - utm_campaign: launch / week1 など

## 19. SNS発信テンプレ（安心設計軸）
- コンセプト:
  - 「動かす」より「詰まった時に戻れる」
  - 「危ないことを勝手にやらない（権限は明示、通信は最小限）」
  - 「日本語で詰まりどころを潰す」
- X投稿テンプレ（最低6本）:
  - あるある → 解決 → DL
  - 30秒デモ（動画台本付き）
  - クラッシュ復旧導線の紹介
  - ポート競合・Firewallの安心設計
  - バックアップ/復元
  - Java互換性警告

## 20. 完了条件（チェックリスト）
- LPのDownloadより上に広告/アフィが存在しない。
- LP/DocsでPR表記が明確かつ違和感がないこと。
- Securityに「ローカル保存」「必要最小限の通信」「通信先一覧リンク」「UACで昇格」がある。
- /docs に privacy-and-network があり、通信先と目的が説明されている。
- /docs/24-7-hosting は「選定基準」「比較表」「移行手順」を含む。
- 計測イベントが3つ以上定義され、UTM運用がある。
- SNSテンプレが6本以上、売り込み臭が強すぎない。
