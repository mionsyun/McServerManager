# MaiPilot (McServerManager) — Claude Code 開発ガイド

このファイルはClaude Codeがこのリポジトリで作業するときに自動的に読み込まれます。
ここに書かれたルールはすべてのコーディング作業に適用されます。

---

## プロジェクト概要

| 項目 | 内容 |
|---|---|
| アプリ名 | MaiPilot (旧: McServerManager) |
| 用途 | Minecraftサーバー管理ツール（Windows デスクトップ） |
| 主言語 | C# .NET 8.0 / WPF |
| サブ | Vue.js ランディングページ (`McServerManager/landing/`) |
| インストーラー | Inno Setup (`McServerManager/installer/`) |
| アーキテクチャ | MVVM + サービス層 |

---

## ディレクトリ構成ルール

```
McServerManager/                  ← リポジトリルート
├── McServerManager/              ← WPF プロジェクト本体
│   ├── Models/                   ← データクラスのみ（ロジック禁止）
│   ├── Services/                 ← ビジネスロジック・インフラ
│   ├── ViewModels/               ← MVVM ViewModel（UIロジックのみ）
│   ├── Views/                    ← XAML + code-behind
│   ├── Utilities/                ← RelayCommand, ObservableObject など
│   ├── Resources/                ← XAML テーマ・スタイル
│   └── installer/                ← Inno Setup (.iss ファイル)
└── McServerManager/landing/      ← Vue.js ランディングページ
```

**絶対に編集してはいけないディレクトリ:**
- `bin/`, `obj/` — .NET ビルド出力（自動生成）
- `dist/` — Vue.js / インストーラービルド出力
- `node_modules/` — npm パッケージ（自動管理）
- `.vs/` — Visual Studio キャッシュ

---

## よく使うコマンド

### C# ビルド・実行

```bash
# ビルド（Releaseモード）
dotnet build McServerManager/McServerManager.csproj --configuration Release

# 実行（開発用）
dotnet run --project McServerManager/McServerManager.csproj

# 発行（自己完結型 win-x64）
dotnet publish McServerManager/McServerManager.csproj \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  --output ./dist/
```

### Vue.js ランディングページ

```bash
cd McServerManager/landing
npm install
npm run dev     # 開発サーバー
npm run build   # 本番ビルド
```

### Inno Setup インストーラー

```bash
# Windows の場合（ISCC がパスに通っていること）
iscc McServerManager/installer/McServerManager.iss
```

### Git

```bash
git status
git diff
git log --oneline -10
```

---

## C# コーディング規約

### 命名規則（変更禁止）

| 対象 | 規則 | 例 |
|---|---|---|
| クラス・インターフェイス | PascalCase | `ServerConfigService`, `IDialogService` |
| メソッド | PascalCase + 動詞 | `LoadAsync`, `CreateServer` |
| 非同期メソッド | + `Async` suffix 必須 | `StartServerAsync()` |
| プロパティ | PascalCase | `ServerName`, `IsRunning` |
| private フィールド | `_camelCase` | `_serverName` |
| 定数 | PascalCase | `MaxRetryCount` |
| イベント | PascalCase | `ServerCrashed` |

### 禁止事項

```csharp
// ❌ async void は書かない（UIイベントハンドラのみ例外）
private async void DoSomething() { }

// ❌ Console.WriteLine でデバッグしない
Console.WriteLine("debug: " + value);

// ❌ AppServices をコンストラクタで受け取らない（サービスロケーター禁止）
public SomeViewModel(AppServices services) { }

// ❌ ViewModelにビジネスロジックを書かない
private async Task StartServerAsync()
{
    var javaPath = FindJavaInProgramFiles(); // ← サービス層の責務
}
```

### 推奨パターン

```csharp
// ✅ コンストラクタインジェクション（必要な依存だけ受け取る）
public ServerViewModel(
    IServerRuntimeManager runtime,
    IDialogService dialog)
{ }

// ✅ 非同期は Task を返す
private async Task LoadDataAsync(CancellationToken ct = default)
{
    await _service.LoadAsync(ct);
}

// ✅ UIスレッドに戻す（ViewModel内では ConfigureAwait(false) 不要）
await _runtime.StartAsync();
OnPropertyChanged(nameof(Status));
```

---

## ViewModel 設計ルール

| ルール | 理由 |
|---|---|
| 1ファイル 300行以内 | `ServerViewModel` が1000行超になった反省 |
| タブごとに専用 ViewModel を作る | 責務の分離 |
| `ObservableObject` を継承する | INotifyPropertyChanged の実装 |
| `DispatcherTimer` は必ず `Dispose()` で止める | メモリリーク防止 |

**タブ別 ViewModel の対応:**

| ViewModel | 担当 |
|---|---|
| `ServerViewModel` | 状態集約・Start/Stop/Restart のみ |
| `ServerConsoleViewModel` | ログ・コンソール入力 |
| `ServerNetworkViewModel` | IP・ファイアウォール・UPnP |
| `ServerWorldViewModel` | ワールド・バックアップ |
| `ServerAddonViewModel` | プラグイン・Mod |
| `ServerSettingsViewModel` | server.properties 編集 |

---

## サービス層のルール

- サービスクラスはインターフェイスとセットで作る（`IXxxService` + `XxxService`）
- HTTP通信には適切なタイムアウトを設定する（最低4秒、外部APIは10秒）
- ファイル保存にはリトライロジックを入れる（`ServerConfigService` の実装を参考）
- `AppServices` への新しい依存追加は禁止（廃止予定のサービスロケーター）

---

## エラーハンドリング方針

```csharp
// サービス層: 例外はそのまま伝播（握りつぶさない）
public async Task<ServerConfig> LoadAsync(string id)
{
    return await _fileSystem.ReadJsonAsync<ServerConfig>(path); // 例外はそのまま上へ
}

// ViewModel層: ユーザーに見せるべきエラーのみキャッチ
private async Task StartServerAsync()
{
    try { await _runtime.StartAsync(); }
    catch (JavaNotFoundException ex)
    {
        await _dialog.ShowAsync($"Javaが見つかりません: {ex.JavaPath}");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "サーバー起動失敗");
        await _dialog.ShowAsync("予期しないエラーが発生しました。");
    }
}
```

---

## セキュリティ注意点

```csharp
// プロセス引数は ArgumentList（配列）で渡す（文字列結合禁止）
var info = new ProcessStartInfo("java.exe");
info.ArgumentList.Add("-Xmx" + config.MemoryXmxMb + "M"); // ✅
// info.Arguments = $"-Xmx{config.MemoryXmxMb}M {userInput}"; // ❌

// ユーザー入力パスは必ず正規化してから検証
var fullPath = Path.GetFullPath(userInputPath);
if (!fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
    throw new SecurityException("許可されていないパスです");
```

---

## Vue.js (landing/) のルール

- コンポーネントは `PascalCase.vue` で命名（例: `HeroSection.vue`）
- `<script setup lang="ts">` を使う
- Props / Emits は型定義を明示する
- ランディングページは Pinia / Vuex 不要（シンプルに `ref` / `reactive` で十分）

---

## コードレビューチェックリスト

PRを出す前に確認:

- [ ] `dotnet build` がエラーなし
- [ ] `async void` を使っていない（UIイベントハンドラ除く）
- [ ] `Console.WriteLine` が残っていない
- [ ] ViewModel が 300行以内
- [ ] `AppServices` への新しい参照を追加していない
- [ ] ユーザー入力パスを `Path.GetFullPath` で検証している
- [ ] `bin/`, `obj/`, `dist/`, `node_modules/` を直接編集していない
