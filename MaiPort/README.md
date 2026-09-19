# MaiPort — ポート開放ツール

Windows デスクトップ向けの、ポート開放（ポートフォワード）専用ツールです。
MaiPilot (McServerManager) のネットワーク層（UPnP / ファイアウォール / IP 取得）を流用しています。

## できること

- **UPnP でのポート転送**：ルーターに対して TCP / UDP のポートマッピングを作成・削除する
- **Windows ファイアウォールの受信許可**：`netsh` で受信規則を作成・削除する（必要に応じて UAC 昇格）
- **状態表示**：管理者権限の有無 / LAN IP / グローバル IP / UPnP ルーター検出状況
- **使用状況の確認**：指定ポートを使用中のプロセス（TCP は LISTENING、UDP はバインド中）を表示
- **開放中ポートの記録**：`%LOCALAPPDATA%\MaiPort\rules.json` に保存し、あとから解除できる
- **Palworld プリセット**：UDP 8211 をワンクリックで入力欄にセット

## UDP 8211（Palworld）を開放する手順

1. MaiPort を起動する
2. 「Palworld (UDP 8211)」ボタンを押す（ポート番号 8211 / プロトコル「UDP のみ」/ 用途「Palworld」が入る）
3. 「ルーターに UPnP でポート転送を設定する」「Windows ファイアウォールの受信を許可する」にチェックが入っていることを確認する
4. 「開放する」を押す
5. UAC の確認が出たら「はい」を選ぶ（ファイアウォール設定に管理者権限が必要なため）
6. ログに「ファイアウォール: UDP 8211 の受信を許可しました。」「UPnP: UDP 8211 を開放しました。」が出れば完了

解除するときは、一覧の「閉じる」または入力欄を合わせて「開放を解除する」を押します。

> UPnP が失敗する場合は、ルーター側で UPnP が無効、二重ルーター構成、または
> 共有回線 / CGNAT の可能性があります。ログに表示される案内を確認してください。

## ビルド

```bash
# ビルド（Releaseモード）
dotnet build MaiPort/MaiPort.csproj --configuration Release

# 実行（開発用 / Windows のみ）
dotnet run --project MaiPort/MaiPort.csproj

# 発行（自己完結型 win-x64・単一ファイル）
dotnet publish MaiPort/MaiPort.csproj \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  --output ./dist/maiport/

# テスト
dotnet test MaiPort.Tests/MaiPort.Tests.csproj
```

## 構成

```
MaiPort/
├── Models/        … データクラスのみ（PortRule, PortProtocol など）
├── Services/      … UPnP・ファイアウォール・ネットワーク・永続化
│   └── Interfaces/… IXxxService
├── ViewModels/    … MVVM ViewModel（各 300 行以内）
├── Utilities/     … ObservableObject / RelayCommand（MaiPilot から流用）
└── Resources/     … ダークテーマ・共通スタイル（MaiPilot から流用）
```

依存の組み立ては `App.xaml.cs` の 1 箇所のみで行い、各クラスは必要な依存だけを
コンストラクタで受け取ります（サービスロケーターは使いません）。
