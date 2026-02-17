# McServerManager

Minecraft サーバーを Windows 上で管理するための WPF デスクトップアプリです。サーバーの作成、起動/停止、設定編集、ワールド管理、バックアップ、バージョン切替、ネットワーク/Firewall 設定までを GUI で完結させます。

## 主な機能
- サーバー作成: EULA 同意を必須にした新規作成ウィザード、保存先/メモリ/ポート/種別/バージョン指定
- ランタイム管理: 起動/停止/再起動、起動ログのリアルタイム表示、コマンド送信
- 設定編集: `server.properties` の主要項目を GUI で編集、実行中の変更は再起動で反映
- ワールド管理: ワールド一覧/切替/削除/新規作成、バックアップ/復元
- MOD/プラグイン管理: サーバー種別に応じて `mods` / `plugins` をGUI管理、追加・有効化・無効化・削除、ドラッグ＆ドロップ追加
- バージョン管理: Minecraft バージョン取得、server.jar の再取得、変更前バックアップ
- 起動安定化: 初回ヘルスチェック、Forge起動方式の自動判定/手動上書き、起動プリセット適用
- ネットワーク支援: LAN IP/グローバル IP の表示、公開チェックリスト
- Firewall: 受信ルールの作成/削除/再作成 (TCP/UDP)
- UPnP: ルーターの自動ポート開放 (Open.NAT)
- UI/UX: ダーク/ライト切替、初回ガイド + チュートリアルオーバーレイ

## 対応環境
- Windows 10/11
- .NET 8.0 (WPF)
- Java (server.jar 実行用)
- ネットワーク接続 (バージョン情報/サーバー jar/公開 IP の取得)

## 起動/ビルド
- 開発: `McServerManager.sln` を Visual Studio で開き、`net8.0-windows` をビルド
- 配布物: `bin/Release/net8.0-windows/McServerManager.exe`

## サーバー作成フロー
1. サーバー名、保存先、種別 (Forge/Spigot/Purpur/Paper/Fabric/Vanilla)、バージョンを指定
2. メモリ割当 (Xms/Xmx)、Java パス、ポート/最大人数/オンラインモードを設定
3. EULA 同意を確認して作成
4. 生成物: `server.jar`、`eula.txt`、`server.properties`、`config.json`、`logs/`、`backups/`

## サーバー実行仕様
- 起動コマンド: `java -Xms{Xms}M -Xmx{Xmx}M -jar "server.jar" nogui`
- 起動完了判定: コンソール出力の `Done (` を検知
- 停止: `stop` を送信し、10 秒タイムアウトで強制終了
- ログ: 最大 5000 行をメモリ保持、テキストファイルとしてエクスポート可能
- 異常終了: `ServerCrashed` で警告ダイアログを表示

## 設定項目 (GUI)
- ポート (1-65535)
- 最大人数 (1-500)
- MOTD
- オンラインモード
- 難易度 (`peaceful`/`easy`/`normal`/`hard`)
- ゲームモード (`survival`/`creative`/`adventure`/`spectator`)
- PvP
- 描画距離 (2-32)
- スポーン保護 (0-100)
- ワールド名/シード
- Java パス

## ワールド/バックアップ仕様
- ワールド検出: `level.dat` または `region/` が存在するフォルダを対象
- バックアップ: `backups/` に ZIP 保存、`.json` メタデータと `backup-info.txt` を同梱
- 復元: 既存ワールドを削除後に展開 (必要なら復元前バックアップ作成)

## MOD/プラグイン管理仕様
- 対象種別: Forge/Fabric (MOD), Spigot/Paper/Purpur (プラグイン)
- 管理先: `mods` または `plugins` フォルダ
- 無効化方式: `mods/disabled` または `plugins/disabled` へ移動
- 追加方式: ファイル選択 (`.jar`) またはドラッグ＆ドロップ
- 互換性チェック: 重複候補と Loader 不一致候補（Forge/Fabric/Plugin系）を警告

## 起動安定化仕様
- 初回ヘルスチェック: 種別ごとに必要フォルダ/補助ファイルを確認し、不足時はガイド表示
- 起動方式: `Auto` / `server.jar 固定` / `Forge run.bat 固定` / `Forge win_args 固定`
- 起動プリセット: メモリ (`Xms/Xmx`) と Java追加引数 (GC設定含む) をテンプレート適用

## ネットワーク/公開
- LAN IP 一覧表示
- グローバル IP 取得: `https://api.ipify.org`
- 共有アドレス: `PublicIp:Port`
- ポート使用中のプロセス検出と強制終了 (ユーザー確認あり)

## Firewall / UPnP
- Firewall ルール: `netsh` で TCP/UDP の受信許可を作成・削除
- 管理者権限が必要 (権限がない場合は UAC で昇格)
- UPnP: Open.NAT で TCP ポートの自動開放/閉鎖

## バージョン取得/サーバー種別
- Vanilla: Mojang 公式マニフェストから取得
- Paper: PaperMC API の最新ビルド
- Purpur: Purpur API の最新ビルド
- Fabric: Loader/Installer の最新 stable
- Forge: Maven メタデータから最新バージョン
- Spigot: BuildTools.jar をダウンロードしてビルド

## データ保存先
- ルート: `%APPDATA%\BlockPilot`
- `appsettings.json`: アプリ設定
- `servers/<serverId>/config.json`: サーバー構成
- `servers/<serverId>/server.properties`: Minecraft 設定
- `servers/<serverId>/server.jar`, `logs/`, `backups/`
- `cache/version_manifest.json`: バージョンキャッシュ

## 設定ファイル仕様
### appsettings.json
- `HasShownFirstRun`: 初回ガイド表示済み
- `HasCompletedTutorial`: チュートリアル完了済み
- `ServerDirectories`: 追加のサーバー探索パス (再帰探索)
- `EnableUpnp`: UPnP 自動開放を有効化
- `PromptUpnp`: 起動時に UPnP を確認する
- `Theme`: `Dark` / `Light`

### config.json (サーバー)
- `ServerId`: サーバー ID
- `Name`: サーバー名
- `Type`: 種別 (`Vanilla`/`Forge`/`Spigot`/`Purpur`/`Paper`/`Fabric`)
- `Version`: バージョン ID
- `DirectoryPath`: サーバー実体ディレクトリ
- `JavaPath`: java.exe のフルパス
- `MemoryXmsMb` / `MemoryXmxMb`: メモリ割当
- `Port`, `MaxPlayers`, `OnlineMode`, `Motd`
- `WorldName`, `Seed`, `Difficulty`, `GameMode`, `Pvp`, `ViewDistance`, `SpawnProtection`
- `CreatedAt`, `LastStartedAt`
- `Firewall.TcpRuleName`, `Firewall.UdpRuleName`

## 画面構成
- 左ペイン: サーバー一覧、テーマ切替
- 右ペイン: サーバー詳細 (コンソール/設定/ワールド/バージョン/ネットワーク)
- 追加ウィンドウ: 新規サーバー作成、初回ガイド、チュートリアルオーバーレイ

## 既知の挙動/注意
- サーバー作成時に EULA へ同意が必須
- 変更中の設定は保存できるが反映は再起動後
- UPnP/Firewall 操作は環境により失敗する場合あり
- IPv4 前提でアドレス表示を実施
