# Terraform for LP (Azure Static Web Apps)

このディレクトリは `landing`（Nuxt LP）公開に必要な最小インフラを作成します。

## 作成されるリソース
- `azurerm_resource_group`
- `azurerm_static_web_app`
- `azurerm_static_web_app_custom_domain`（`custom_domain_name` を指定した場合のみ）
- `azurerm_trusted_signing_account`
- `azurerm_trusted_signing_certificate_profile`

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

## LP デプロイの次ステップ
- `landing` を `npm run build` して成果物をデプロイ
- GitHub Actions を使う場合は `static_web_app_api_key` をシークレット登録
