# Terraform for LP / 配布基盤 (Azure)

このディレクトリは次のインフラを管理します。

- `landing`（Nuxt LP）の公開（Azure Static Web Apps）
- インストーラー配布用の Azure Blob Storage
- コード署名用の Trusted Signing アカウント

## 作成されるリソース
- `azurerm_resource_group`
- `azurerm_static_web_app`
- `azurerm_static_web_app_custom_domain`（`custom_domain_name` を指定した場合のみ）
- `azurerm_trusted_signing_account`（`trusted_signing_enabled = true` のときのみ。既定は作成しない）
- `azurerm_storage_account`（インストーラー配布用。既定名 `stmaipilot`、既定で LP と同じ RG）
- `azurerm_storage_container`（`public`、匿名 blob 読み取り）

**Terraform では作成しないもの**（手動作業）:
- Trusted Signing の本人確認（Identity validation）と証明書プロファイル（→「Trusted Signing セットアップ」）
- DNS レコード（`maipilot.jp` の DNS はこのディレクトリの管理外）
- Terraform state 用のストレージ（`bootstrap-state.ps1` で作成。→「State の保存先」）
- GitHub Secrets / Variables、OIDC 用アプリ登録

すべてのリソースは 1 つのサブスクリプション（`subscription_id` 既定値 `1456e0ca-79d5-4b67-a5e0-5e98062498fc`、
テナント `d546234e-6471-4152-a48b-609d4cc0ecd6`）に作成します。

## 事前準備
1. Terraform 1.6 以上をインストール（未導入の場合）
   - `winget install Hashicorp.Terraform`
2. Azure CLI でログインし、サブスクリプションを選択
   ```bash
   az login --tenant d546234e-6471-4152-a48b-609d4cc0ecd6
   az account set --subscription 1456e0ca-79d5-4b67-a5e0-5e98062498fc
   ```
3. 新しいサブスクリプションでは、リソースプロバイダーを登録しておく
   （`Microsoft.CodeSigning` は azurerm provider の自動登録対象外）
   ```bash
   az provider register --namespace Microsoft.CodeSigning
   az provider register --namespace Microsoft.Web
   az provider register --namespace Microsoft.Storage
   ```
4. **初回のみ** state 用のストレージを作成する（PowerShell 7 推奨。何度実行しても同じ結果）
   ```powershell
   cd infra/terraform
   ./bootstrap-state.ps1
   ```

## 使い方
```powershell
cd infra/terraform
copy terraform.tfvars.example terraform.tfvars
terraform init          # backend.tf の Azure Storage に state を置く
terraform validate
terraform plan -out tfplan
terraform apply tfplan
```

## State の保存先（remote backend）

state は `backend.tf` で指定した Azure Blob Storage に保存します。state には SWA のデプロイトークンや
ストレージの接続文字列が平文で入るため、**リポジトリにもローカルにも置きません**。

| 項目 | 値 |
|---|---|
| リソースグループ | `mcsm-tfstate-rg`（Japan East） |
| ストレージアカウント | `stmaipilottfstate` |
| コンテナ / キー | `tfstate` / `prod.terraform.tfstate` |
| 認証 | Entra ID（`use_azuread_auth = true`）。アカウントキー認証は無効 |

- **ロック**: apply 中は blob のリースでロックされるので、2 か所から同時に apply しても state は壊れません。
  異常終了でロックが残った場合は `terraform force-unlock <LOCK_ID>` で解除します。
- **復旧**: blob のバージョン管理と 30 日間の論理削除を有効にしているので、state を壊したり消したりしても
  ポータルの「バージョン」から戻せます。
- **権限**: plan / apply を実行するユーザーやサービスプリンシパルには、サブスクリプション（または対象 RG）の
  **共同作成者** に加えて、state 用ストレージの **Storage Blob Data Contributor** が必要です。
  `bootstrap-state.ps1` は実行ユーザーにだけ付与します。ほかの人やサービスプリンシパルは
  `-AssigneeObjectId` を指定して再実行してください。
- **名前を変える場合**: ストレージアカウント名は Azure 全体で一意です。`stmaipilottfstate` が取れなければ、
  `bootstrap-state.ps1 -StorageAccountName <別名>` で作成し、`backend.tf` の `storage_account_name` も同じ名前に変えてください。
- state 用ストレージは Terraform の管理対象に**含めません**（`terraform destroy` などで自分の state を消さないため）。

### Claude Code のクラウド環境や CI から実行する場合

一時的なコンテナから実行しても state は Azure に残るので安全です。ただし、次の準備が必要です。

1. 環境のネットワーク設定で、次のホストへの通信を許可する
   - `management.azure.com`、`login.microsoftonline.com`（Azure API と認証）
   - `stmaipilottfstate.blob.core.windows.net`（state）
   - `registry.terraform.io`、`releases.hashicorp.com`（Terraform 本体・プロバイダーの取得）
2. サービスプリンシパルを作り、上の「権限」の 2 つのロールを付与する
3. 環境変数に認証情報を設定する（シークレットはチャットに貼らず、環境の設定画面で登録する）
   - `ARM_TENANT_ID` / `ARM_SUBSCRIPTION_ID` / `ARM_CLIENT_ID` / `ARM_CLIENT_SECRET`

## ゼロから再構築する手順

旧環境の Azure リソースはすべて削除済みという前提の手順です。ドメイン `maipilot.jp` は残っています。

### 1. `terraform apply`（1 回目：カスタムドメインは付けない）

先に「事前準備」の 4（`bootstrap-state.ps1`）で state 用ストレージを作っておきます。
`terraform.tfvars` では `custom_domain_name = ""` のまま apply します。
DNS がまだ旧 SWA を向いているため、この時点でドメインを付けると検証が通らず apply が止まります。

```powershell
terraform init
terraform plan
terraform apply
```

> **ストレージアカウント名について**: ストレージアカウント名は Azure 全体で一意です。旧アカウント `stmailpilotje` は
> 無効化された旧サブスクリプションに残っていて名前が予約されているため、`stmaipilot` に変更しました。
> 名前を変える場合は `installer_storage_account_name` に加えて、次のファイルにハードコードされている
> `https://stmaipilot.blob.core.windows.net/...` の URL も更新が必要です。
> - `McServerManager/landing/app.vue`
> - `McServerManager/landing/nuxt.config.ts`
> - `McServerManager/installer/build.ps1`
> - `.github/workflows/deploy-installer-to-storage.yml`（`INSTALLER_BASE_URL`）

### 2. 出力から GitHub Secrets / Variables を更新

リポジトリの **Settings → Secrets and variables → Actions** で設定します。

| 種別 | 名前 | 値の取得方法 | 使用箇所 |
|---|---|---|---|
| Secret | `AZURE_STORAGE_CONNECTION_STRING` | `terraform output -raw installer_storage_primary_connection_string` | `deploy-installer-to-storage.yml` |
| Secret | `AZURE_STATIC_WEB_APPS_API_TOKEN_LP` | `terraform output -raw static_web_app_api_key` | `deploy-landing-static-web-apps.yml` |
| Variable | `TRUSTED_SIGNING_ACCOUNT` | `terraform output -raw trusted_signing_account_name` | 署名ステップ |
| Variable | `TRUSTED_SIGNING_ENDPOINT` | `terraform output -raw trusted_signing_endpoint` | 署名ステップ |
| Variable | `TRUSTED_SIGNING_PROFILE` | `terraform output -raw trusted_signing_certificate_profile_name` | 署名ステップ |
| Variable | `AZURE_SUBSCRIPTION_ID` | `terraform output -raw subscription_id` | OIDC ログイン |
| Variable | `AZURE_TENANT_ID` | `terraform output -raw tenant_id` | OIDC ログイン |
| Variable | `AZURE_CLIENT_ID` | OIDC 用アプリ登録のクライアント ID（下記「Trusted Signing セットアップ」） | OIDC ログイン |

> 現在の `.github/workflows/` には Trusted Signing の署名ステップがありません。Trusted Signing 関連の
> Variables は、署名ステップを（再）追加するときに必要になります。

### 3. DNS を新しい SWA に向け直す（カスタムドメイン `www.maipilot.jp`）

LP の正規 URL は `https://www.maipilot.jp` です（`landing/nuxt.config.ts` の `siteUrl`、`sitemap.xml`、
ワークフローの `RELEASE_NOTES_URL` に記載）。そのため、SWA には **`www` サブドメイン**を `cname-delegation` で付けます。

1. 新しい SWA の既定ホスト名を確認する
   ```powershell
   terraform output -raw static_web_app_default_hostname   # 例: xxx-yyy-123.2.azurestaticapps.net
   ```
2. DNS プロバイダー（`maipilot.jp` の管理先）で、`www` の CNAME を上のホスト名に**書き換える**
   （旧 SWA のホスト名を向いたままになっているはずです）
   ```
   www.maipilot.jp.  CNAME  <static_web_app_default_hostname>.
   ```
   旧環境で使っていた検証用 TXT レコード（`_dnsauth.www` など）が残っていれば削除してかまいません。
3. DNS の反映を確認する（`nslookup www.maipilot.jp` で新しいホスト名が返ること）
4. `terraform.tfvars` にドメインを設定し、2 回目の apply を行う
   ```hcl
   custom_domain_name            = "www.maipilot.jp"
   custom_domain_validation_type = "cname-delegation"
   ```
   ```powershell
   terraform apply
   ```
   SSL 証明書の発行まで数分から数十分かかることがあります。

**apex（`maipilot.jp`）も SWA で受ける場合**は、別途ポータルでカスタムドメインを追加します。
apex は `dns-txt-token` で検証し、次の DNS 設定を行います。この Terraform で管理しているドメインは 1 つだけです。
- 表示された TXT トークンを `_dnsauth`（apex）に設定する
- apex 用の ALIAS / ANAME レコード（または DNS プロバイダーの apex CNAME フラット化機能）で、SWA を向ける

`dns-txt-token` を使う場合、TXT に設定する値は `terraform output -raw custom_domain_validation_token` で取得できます。

### 4. インストーラーを blob に置き直す

新しいストレージは空です。`Build And Upload Installer`（`.github/workflows/deploy-installer-to-storage.yml`）を再実行して、
`public/downloads/MaiPilotSetup.exe` とバージョン付き EXE をアップロードし直します。
- GitHub の Actions タブ → `Build And Upload Installer` → **Run workflow**（`develop` ブランチ）で実行する
- 実行後に `terraform output -raw installer_latest_download_url` の URL からダウンロードできることを確認する
- このワークフローは `update.json` を landing に同期し、landing のデプロイも自動で起動します

### 5. landing を再デプロイする

`deploy-landing-static-web-apps.yml` を実行して（手順 4 から自動起動されなかった場合は Actions タブの **Run workflow** から手動で）、新しい SWA に LP をデプロイします。
手順 2 で更新した `AZURE_STATIC_WEB_APPS_API_TOKEN_LP` が使われます。
`https://www.maipilot.jp` とダウンロードリンクが動くことを確認します。

### 6. Trusted Signing の証明書プロファイルを手動作成

下の「Trusted Signing セットアップ」を参照してください。

## Trusted Signing セットアップ

Trusted Signing を使うと、コード署名証明書を購入せずに署名できます。
Terraform が作るのは**署名アカウントまで**です。証明書プロファイルは本人確認が完了しないと作れないため、ポータルで手動作成します
（azurerm 4.x には証明書プロファイルのリソースもありません）。

> ポータル上の名称が「Artifact Signing」に変わっている場合があります。ロール名も同様に読み替えてください。

### 1. Terraform で署名アカウントを作成

署名アカウントは**作成した時点から月額課金**（Basic で約 $9.99/月）されるため、既定では作成しません。
本人確認の準備ができたら `trusted_signing_enabled = true` にして apply します。

```hcl
# terraform.tfvars に追記（省略時はデフォルト値が使われます）
trusted_signing_enabled                  = true
trusted_signing_account_name             = ""        # 空 = 自動生成（${prefix}-${environment}-tsa）
trusted_signing_certificate_profile_name = "MaiPilot" # ポータルで作るプロファイル名と一致させる
trusted_signing_sku_name                 = "Basic"   # または Premium
trusted_signing_location                 = "East Asia"  # Japan East は非対応
```

### 2. 本人確認（Identity validation）

1. 自分のユーザーに、署名アカウントに対する **Trusted Signing Identity Verifier** ロールを付与する
   ```bash
   az role assignment create \
     --role "Trusted Signing Identity Verifier" \
     --assignee "<自分の UPN またはオブジェクト ID>" \
     --scope "$(terraform output -raw trusted_signing_account_id)"
   ```
2. Azure Portal → 対象の Trusted Signing アカウント → **Identity validations** → **New identity** → **Public** を選ぶ
3. 組織または個人の情報を入力して申請し、状態が **Completed** になるまで待つ（数日かかる場合があります）

> Public Trust の本人確認では、申請できる国・地域や組織要件に制限があります。申請前に最新の要件を確認してください。

### 3. 証明書プロファイルを作成

1. 対象アカウント → **Certificate profiles** → **Create** → **Public Trust**
2. プロファイル名は `terraform output -raw trusted_signing_certificate_profile_name`（既定 `MaiPilot`）と同じにする
3. 手順 2 で完了した identity validation を選んで作成する

### 4. OIDC 用フェデレーテッド資格情報を設定

GitHub Actions が OIDC でログインできるよう、Azure AD アプリに Federated Credential を追加します。

1. Azure Portal → Microsoft Entra ID → アプリの登録 → 対象アプリ（なければ新規作成）→ 証明書とシークレット → フェデレーテッド資格情報
2. 「資格情報の追加」→ シナリオ: **GitHub Actions**
   - Organization: `<GitHub org or user>`
   - Repository: `<repo name>`
   - Entity type: `Branch`
   - Branch: `develop`
3. 同様に `Entity type: Pull request` でも追加（PR からのビルドを許可する場合）
4. アプリのクライアント ID を GitHub Variable `AZURE_CLIENT_ID` に設定する

### 5. 署名者ロールを付与

OIDC アプリ（サービスプリンシパル）に、**Trusted Signing Certificate Profile Signer** ロールを付与します。

```bash
az role assignment create \
  --role "Trusted Signing Certificate Profile Signer" \
  --assignee "<CLIENT_ID>" \
  --scope "$(terraform output -raw trusted_signing_account_id)/certificateProfiles/<PROFILE_NAME>"
```

スコープはアカウント全体（`--scope "$(terraform output -raw trusted_signing_account_id)"`）でも構いません。

### 署名フロー（CI、署名ステップを入れた場合）

```
dotnet publish (Phase=Publish)
  → Trusted Signing: McServerManager.exe に署名
  → ISCC でインストーラーをパッケージ (Phase=Package)
  → Trusted Signing: MaiPilotSetup.exe・バージョン付き EXE に署名
  → 署名済み EXE の SHA-256 で update.json を生成
  → Azure Blob Storage にアップロード
```

## インストーラー配布用ストレージ

インストーラー（`MaiPilotSetup.exe` など）は `https://<account>.blob.core.windows.net/public/downloads/` から配布します。

- 既定では LP と同じ RG・リージョンに作成します。`installer_storage_resource_group_name` と
  `installer_storage_location` で変更できます（空なら LP の RG / リージョン）
- ストレージアカウントとコンテナには `prevent_destroy = true` を付けているので、削除や再作成を伴う plan はエラーになります。
  本当に作り直す場合は、`storage.tf` の該当行を一時的に外してください
- コンテナ名 `public` とプレフィックス `downloads/` は、ワークフロー・`McServerManager/installer/build.ps1`・landing に
  ハードコードされているため、Terraform 側でも固定値にしています

### GitHub Secret `AZURE_STORAGE_CONNECTION_STRING` との関係

- アップロードは `.github/workflows/deploy-installer-to-storage.yml` が行います。Secret `AZURE_STORAGE_CONNECTION_STRING`
  （アカウントキーを含む接続文字列）を使って `az storage` を実行します。Terraform はアップロード自体には関与しません。
- ワークフローもコンテナ作成（`--public-access blob`）を実行しますが、既存コンテナに対しては何もしないため Terraform と競合しません。
- 接続文字列はアカウントキー認証のため、`shared_access_key_enabled = true` を維持しています（無効化するとアップロードが失敗します）。
- アカウントを作り直したときやキーをローテーションしたときは、Secret を更新してください。
  ```bash
  terraform output -raw installer_storage_primary_connection_string
  ```

## Static Web App のリージョン

Static Web Apps は Japan East に対応していないため、`static_web_app_location`（既定 `East Asia`）で
RG とは別のリージョンを指定しています。コンテンツはグローバルに配信されるので、リージョンは管理プレーンの場所にすぎません。

## 出力
- `static_web_app_url`: 公開 URL（既定ホスト名）
- `static_web_app_default_hostname`: 既定ホスト名（`www` CNAME の向け先）
- `static_web_app_api_key`: デプロイトークン（sensitive、Secret `AZURE_STATIC_WEB_APPS_API_TOKEN_LP` に設定）
- `custom_domain_name`: 独自ドメイン
- `custom_domain_validation_token`: TXT 検証用トークン（`dns-txt-token` のとき）
- `azure_portal_link`: Azure Portal リンク
- `subscription_id` / `tenant_id`: Variable `AZURE_SUBSCRIPTION_ID` / `AZURE_TENANT_ID` に設定
- `trusted_signing_account_name`: Trusted Signing アカウント名（Variable `TRUSTED_SIGNING_ACCOUNT` に設定）
- `trusted_signing_account_id`: Trusted Signing アカウントのリソース ID（ロール割り当てのスコープ）
- `trusted_signing_endpoint`: Trusted Signing エンドポイント URL（Variable `TRUSTED_SIGNING_ENDPOINT` に設定）
- `trusted_signing_certificate_profile_name`: 証明書プロファイル名（手動作成。Variable `TRUSTED_SIGNING_PROFILE` に設定）
- `installer_storage_account_name`: インストーラー配布用ストレージアカウント名
- `installer_storage_primary_blob_endpoint`: Blob エンドポイント（例: `https://stmaipilot.blob.core.windows.net/`）
- `installer_download_base_url`: インストーラー配布 URL ベース（ワークフローの `INSTALLER_BASE_URL` と同じ値）
- `installer_latest_download_url`: 最新インストーラーの URL（LP の `NUXT_PUBLIC_DOWNLOAD_URL` と同じ値）
- `installer_storage_primary_connection_string`: 接続文字列（sensitive、Secret `AZURE_STORAGE_CONNECTION_STRING` に設定）
