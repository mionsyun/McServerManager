# Terraform state の保存先（Azure Blob Storage）
#
# state には SWA のデプロイトークンやストレージの接続文字列などの機密値が入るため、
# リポジトリやローカル（クラウドセッションの一時コンテナなど）には置かない。
# blob のリースで自動的にロックされるので、複数人・複数環境から同時に apply しても壊れない。
#
# このストレージは Terraform の管理対象とは別に bootstrap-state.ps1 で作る
# （管理対象と同じ state に入れると、destroy などで自分自身の state を消しうるため）。
# 名前を変える場合は bootstrap-state.ps1 の既定値も合わせること。
terraform {
  backend "azurerm" {
    tenant_id            = "d546234e-6471-4152-a48b-609d4cc0ecd6"
    subscription_id      = "1456e0ca-79d5-4b67-a5e0-5e98062498fc"
    resource_group_name  = "mcsm-tfstate-rg"
    storage_account_name = "stmaipilottfstate"
    container_name       = "tfstate"
    key                  = "prod.terraform.tfstate"

    # アカウントキーではなく Entra ID で認証する（state 用ストレージはキー認証を無効化している）。
    # 実行するユーザー / サービスプリンシパルに「Storage Blob Data Contributor」が必要。
    use_azuread_auth = true
  }
}
