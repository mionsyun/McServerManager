# Terraform for LP (Azure Static Web Apps)

このディレクトリは `landing`（Nuxt LP）公開に必要な最小インフラを作成します。

## 作成されるリソース
- `azurerm_resource_group`
- `azurerm_static_web_app`
- `azurerm_static_web_app_custom_domain`（`custom_domain_name` を指定した場合のみ）

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

## 独自ドメイン設定
`terraform.tfvars` で以下を設定してください。

```hcl
custom_domain_name            = "lp.maipilot.jp"
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

## LP デプロイの次ステップ
- `landing` を `npm run build` して成果物をデプロイ
- GitHub Actions を使う場合は `static_web_app_api_key` をシークレット登録
