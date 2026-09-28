# Terraform for LP (Azure Static Web Apps)

このディレクトリは `landing`（Nuxt LP）公開に必要な最小インフラと、インストーラー配布用ストレージを管理します。

## 作成されるリソース
- `azurerm_resource_group`
- `azurerm_static_web_app`
- `azurerm_static_web_app_custom_domain`（`custom_domain_name` を指定した場合のみ）
- `azurerm_trusted_signing_account`
- `azurerm_trusted_signing_certificate_profile`
- `azurerm_storage_account`（インストーラー配布用 `stmailpilotje`。**既存アカウントを import で取り込み**。別サブスクリプション）
- `azurerm_storage_container`（`public`、匿名 blob 読み取り。**既存コンテナを import で取り込み**）

> Terraform は **1.7.0 以上** が必要です。インストーラー配布用ストレージの `import` ブロックで
> `for_each`（1.7 で追加）を使い、`installer_storage_import_existing = false` のときに
> import をスキップして新規作成できるようにしているためです（id に変数を使えるのは 1.6 以降）。

## 事前準備
1. Terraform インストール（未導入の場合）
   - `winget install Hashicorp.Terraform`
2. Azure CLI ログイン
   - `az login`
   - 必要ならサブスクリプション指定: `az account set --subscription <SUBSCRIPTION_ID_OR_NAME>`

## 使い方
```powershell
cd infra/terraform
copy terraform.tfvars.example terraform.tfvars
terraform init
terraform validate
terraform plan
terraform apply -auto-approve
```

## サブスクリプション移行手順

別サブスクリプション・テナントへ移行する場合:

1. `terraform.tfvars`（または `variables.tf` のデフォルト値）を更新
   ```hcl
   subscription_id = "<新しい SUBSCRIPTION_ID>"
   tenant_id       = "<新しい TENANT_ID>"
   ```
2. 既存のステートを持つ場合は先に `terraform state pull` でバックアップ
3. `terraform init -upgrade && terraform plan` で差分を確認
4. `terraform apply` で新サブスクリプションにリソースを作成

> **注意**: リソースグループをまたいだリソース移行は Azure ポータルの「移動」機能を使うか、`terraform destroy` → `terraform apply` で再作成します。

## Trusted Signing セットアップ

Trusted Signing を使うとコード署名証明書なしで EV 相当の署名ができます。

### 1. Terraform で署名アカウントを作成

```hcl
# terraform.tfvars に追記（省略時はデフォルト値が使われます）
trusted_signing_account_name             = ""        # 空 = 自動生成
trusted_signing_certificate_profile_name = "MaiPilot"
trusted_signing_sku_name                 = "Basic"   # または Premium
trusted_signing_location                 = "East Asia"  # Japan East は非対応
```

```powershell
terraform apply
```

### 2. OIDC 用フェデレーテッド資格情報を設定

GitHub Actions が OIDC でログインできるよう、Azure AD アプリに Federated Credential を追加します。

1. Azure Portal → Microsoft Entra ID → アプリの登録 → 対象アプリ → 証明書とシークレット → フェデレーテッド資格情報
2. 「資格情報の追加」→ シナリオ: **GitHub Actions**
   - Organization: `<GitHub org or user>`
   - Repository: `<repo name>`
   - Entity type: `Branch`
   - Branch: `develop`
3. 同様に `Entity type: Pull request` でも追加（PR からのビルドを許可する場合）

### 3. GitHub Variables を設定

リポジトリの **Settings → Secrets and variables → Actions → Variables** に以下を追加:

| Variable 名 | 値 | 取得元 |
|---|---|---|
| `AZURE_CLIENT_ID` | アプリ（クライアント）ID | Azure AD アプリ登録 |
| `AZURE_TENANT_ID` | テナント ID | `terraform output` → `tenant_id` / Azure AD |
| `AZURE_SUBSCRIPTION_ID` | サブスクリプション ID | `terraform output` → `subscription_id` |
| `TRUSTED_SIGNING_ENDPOINT` | エンドポイント URL | `terraform output trusted_signing_endpoint` |
| `TRUSTED_SIGNING_ACCOUNT` | アカウント名 | `terraform output trusted_signing_account_name` |
| `TRUSTED_SIGNING_PROFILE` | プロファイル名 | `terraform output trusted_signing_certificate_profile_name` |

> **`TRUSTED_SIGNING_ACCOUNT` が未設定の場合**、署名ステップはスキップされてビルドは継続します（開発環境向け）。

### 4. 署名者ロールを付与

Trusted Signing の署名アカウントに対して、OIDC アプリへ **Trusted Signing Certificate Profile Signer** ロールを付与します。

```bash
az role assignment create \
  --role "Trusted Signing Certificate Profile Signer" \
  --assignee "<CLIENT_ID>" \
  --scope "/subscriptions/<SUB_ID>/resourceGroups/<RG>/providers/Microsoft.CodeSigning/codeSigningAccounts/<ACCOUNT>"
```

### 署名フロー（CI）

```
dotnet publish (Phase=Publish)
  → Trusted Signing: McServerManager.exe に署名
  → ISCC でインストーラーをパッケージ (Phase=Package)
  → Trusted Signing: MaiPilotSetup.exe・バージョン付き EXE に署名
  → 署名済み EXE の SHA-256 で update.json を生成
  → Azure Blob Storage にアップロード
```

## インストーラー配布用ストレージ（既存アカウントの取り込み）

インストーラー（`MaiPilotSetup.exe` など）は Azure Blob Storage の
`https://stmailpilotje.blob.core.windows.net/public/downloads/` から配布しています。
このストレージアカウントは手動作成されたもので、`storage.tf` の `import` ブロックで Terraform 管理下に取り込みます。

### 注意: サブスクリプションが 2 つある点に注意

| 対象 | 変数 | 既定値 |
|---|---|---|
| LP（Static Web App）・Trusted Signing・RG `azurerm_resource_group.lp` | `subscription_id` | `c5a6db5e-a2c1-4e3c-8a67-f7e7e4d166a2` |
| インストーラー配布用ストレージ（`stmailpilotje`） | `installer_storage_subscription_id` | `1456e0ca-79d5-4b67-a5e0-5e98062498fc` |

**既存リソースのサブスクリプション（`variables.tf` の `subscription_id` 既定値）と、ストレージのサブスクリプションは異なります。
どちらが現在正しいかは apply 前に必ず確認してください。**
（テナントはどちらも `tenant_id` = `d546234e-6471-4152-a48b-609d4cc0ecd6` を共有します。）

ストレージは別サブスクリプションにあるため、`versions.tf` の alias 付き provider
`azurerm.installer_storage`（`subscription_id = var.installer_storage_subscription_id`）で管理しています。
`terraform plan/apply` を実行するアカウントには、両方のサブスクリプションへの権限
（ストレージ側はアカウントキー取得 `listKeys` を含む共同作成者相当）が必要です。

### 取り込み手順

1. 既存アカウントの実際の値を確認する
   ```bash
   az storage account show --name stmailpilotje \
     --subscription 1456e0ca-79d5-4b67-a5e0-5e98062498fc \
     --query "{rg:resourceGroup,location:location,sku:sku.name,kind:kind,tls:minimumTlsVersion}"
   ```
   `sku` は `<tier>_<replication>` 形式（例: `Standard_LRS` → tier=`Standard`, replication=`LRS`）。
2. `terraform.tfvars` に設定する（**`installer_storage_resource_group_name` は必須**、既定値なし）
   ```hcl
   installer_storage_subscription_id          = "1456e0ca-79d5-4b67-a5e0-5e98062498fc"
   installer_storage_account_name             = "stmailpilotje"
   installer_storage_resource_group_name      = "<手順1の rg>"
   installer_storage_location                 = "Japan East"   # 手順1の location
   installer_storage_account_tier             = "Standard"
   installer_storage_account_replication_type = "LRS"
   installer_storage_account_kind             = "StorageV2"
   installer_storage_import_existing          = true
   ```
3. `terraform plan` で import 内容と差分を確認する
   - `azurerm_storage_account.installer` と `azurerm_storage_container.public` が **"will be imported"** と表示されること
   - 差分が **in-place 更新（`~`）のみ** であること。想定される差分はタグ追加（`managedBy = "terraform"` など）や
     `min_tls_version = "TLS1_2"` 程度です
   - **`must be replaced` / `destroy` が出た場合は apply しないこと。** 両リソースには `prevent_destroy = true` を
     付けているので、置き換えが必要な差分（location / account_tier / account_kind などの食い違い）は plan がエラーで止まります。
     その場合は tfvars の値を実際の値に合わせて再度 plan してください
4. 差分が破壊的でないことを確認したら `terraform apply`
5. import 完了後も `import` ブロックはそのまま残して問題ありません（state に取り込み済みなら no-op）

> 新しいサブスクリプションにストレージを新規作成する場合のみ `installer_storage_import_existing = false` にします。

### GitHub Secret `AZURE_STORAGE_CONNECTION_STRING` との関係

- アップロードは `.github/workflows/deploy-installer-to-storage.yml` が Secret `AZURE_STORAGE_CONNECTION_STRING`
  （アカウントキーを含む接続文字列）で `az storage` を実行して行います。Terraform はアップロード自体には関与しません。
- ワークフローもコンテナ作成（`--public-access blob`）を実行しますが、既存コンテナに対しては何もしないため Terraform と競合しません。
- 接続文字列はアカウントキー認証のため、`shared_access_key_enabled = true` を維持しています（無効化するとアップロードが失敗します）。
- アカウントキーをローテーションした場合や新規作成した場合は、次の出力値で Secret を更新してください。
  ```bash
  terraform output -raw installer_storage_primary_connection_string
  ```
- コンテナ名 `public` と プレフィックス `downloads/` はワークフロー・`McServerManager/installer/build.ps1`・LP 側に
  ハードコードされているため、Terraform 側でも固定値にしています。

## 独自ドメイン設定
`terraform.tfvars` で以下を設定してください。

```hcl
custom_domain_name            = "www.maipilot.jp"
custom_domain_validation_type = "cname-delegation"
```

- サブドメイン（`lp.example.com` など）: 通常は `cname-delegation`
- ルートドメイン（`example.com`）: `dns-txt-token` を使用
- このリポジトリでは `terraform.tfvars` を作成済みなので、値を自分のドメインへ変更して使えます

`dns-txt-token` の場合は、`terraform apply` 後に `custom_domain_validation_token` 出力値を使って DNS の TXT レコードを設定してください。

## 出力
- `static_web_app_url`: 公開URL
- `static_web_app_api_key`: CI/CD 用デプロイトークン（sensitive）
- `custom_domain_name`: 独自ドメイン
- `custom_domain_validation_token`: TXT 検証用トークン（必要時）
- `azure_portal_link`: Azure Portal リンク
- `trusted_signing_account_name`: Trusted Signing アカウント名（GitHub var `TRUSTED_SIGNING_ACCOUNT` へ設定）
- `trusted_signing_endpoint`: Trusted Signing エンドポイント URL（GitHub var `TRUSTED_SIGNING_ENDPOINT` へ設定）
- `trusted_signing_certificate_profile_name`: 証明書プロファイル名（GitHub var `TRUSTED_SIGNING_PROFILE` へ設定）
- `installer_storage_account_name`: インストーラー配布用ストレージアカウント名
- `installer_storage_primary_blob_endpoint`: Blob エンドポイント（例: `https://stmailpilotje.blob.core.windows.net/`）
- `installer_download_base_url`: インストーラー配布 URL ベース（ワークフローの `INSTALLER_BASE_URL` と同じ値）
- `installer_latest_download_url`: 最新インストーラーの URL（LP の `NUXT_PUBLIC_DOWNLOAD_URL` と同じ値）
- `installer_storage_primary_connection_string`: 接続文字列（sensitive、GitHub Secret `AZURE_STORAGE_CONNECTION_STRING` へ設定）

## LP デプロイの次ステップ
- `landing` を `npm run build` して成果物をデプロイ
- GitHub Actions を使う場合は `static_web_app_api_key` をシークレット登録
