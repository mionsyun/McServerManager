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

## ビルド（Windows）

WPF アプリのため、**exe の生成とアプリの実行は Windows でのみ可能**です。

```powershell
# ビルド + テスト + 単一ファイル exe の生成（dist/maiport-selfcontained/MaiPort.exe）
pwsh -File scripts/build-maiport.ps1

# .NET 8 Desktop Runtime がある環境向けの軽量 exe
pwsh -File scripts/build-maiport.ps1 -SelfContained:$false
```

個別に実行する場合:

```powershell
dotnet build MaiPort/MaiPort.csproj -c Release
dotnet test MaiPort.Tests/MaiPort.Tests.csproj
dotnet run --project MaiPort/MaiPort.csproj
```

## ビルド（GitHub Actions）

`.github/workflows/build-maiport.yml` が windows-latest で
ビルド → テスト → exe 生成まで行い、成果物を Artifacts として添付します。

| Artifact | 内容 |
|---|---|
| `MaiPort-win-x64-selfcontained` | .NET 不要の単一 exe（サイズ大） |
| `MaiPort-win-x64-framework-dependent` | .NET 8 Desktop Runtime が必要な軽量 exe |

## 検証（Linux / Claude Code on the web）

Linux では WPF（`Microsoft.NET.Sdk.WindowsDesktop`）をビルドできないため、
WPF 非依存部分（Models / Services / ViewModels）の型チェックとユニットテストのみを行います。

```bash
sudo apt-get update && sudo apt-get install -y --no-install-recommends dotnet-sdk-8.0
bash scripts/verify-maiport-linux.sh
```

XAML・コードビハインド・exe は Windows 側（上記のスクリプトまたは GitHub Actions）で確認してください。

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
