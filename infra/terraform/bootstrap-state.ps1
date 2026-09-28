<#
.SYNOPSIS
  Terraform state 用の Azure Storage を作成する（初回のみ・何度実行しても同じ結果）。

.DESCRIPTION
  backend.tf が参照するリソースグループ・ストレージアカウント・コンテナを作成し、
  実行ユーザーに Storage Blob Data Contributor を付与する。
  - キー認証 (shared key) と匿名アクセスは無効。Entra ID 認証のみ
  - blob のバージョン管理と、blob / コンテナの論理削除 (30 日) を有効化して、state の誤削除・破損から復旧できるようにする
  このストレージは Terraform 本体では管理しない（backend.tf のコメント参照）。

.EXAMPLE
  az login --tenant d546234e-6471-4152-a48b-609d4cc0ecd6
  ./bootstrap-state.ps1

.EXAMPLE
  # サービスプリンシパルにも state の読み書きを許可する（ストレージは作成済みでも再実行してよい）
  ./bootstrap-state.ps1 -AssigneeObjectId <SP のオブジェクト ID> -AssigneePrincipalType ServicePrincipal
#>
[CmdletBinding()]
param(
    [string]$SubscriptionId = "1456e0ca-79d5-4b67-a5e0-5e98062498fc",
    [string]$ResourceGroupName = "mcsm-tfstate-rg",
    [string]$StorageAccountName = "stmaipilottfstate",
    [string]$ContainerName = "tfstate",
    [string]$Location = "japaneast",
    # 付与先のオブジェクト ID。省略時は az login しているユーザー
    [string]$AssigneeObjectId = "",
    # 付与先がサービスプリンシパル（CI やクラウドセッション用）の場合は ServicePrincipal
    [ValidateSet("User", "ServicePrincipal", "Group")]
    [string]$AssigneePrincipalType = "User"
)

$ErrorActionPreference = "Stop"

function Invoke-Az {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    $output = & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "az $($Arguments -join ' ') が失敗しました (ExitCode=$LASTEXITCODE)"
    }
    return $output
}

Write-Host "サブスクリプション: $SubscriptionId"
Invoke-Az account set --subscription $SubscriptionId | Out-Null
Invoke-Az provider register --namespace Microsoft.Storage --wait | Out-Null

Write-Host "リソースグループ $ResourceGroupName を作成..."
Invoke-Az group create `
    --name $ResourceGroupName `
    --location $Location `
    --tags project=McServerManager managedBy=bootstrap-state workload=terraform-state `
    --only-show-errors | Out-Null

Write-Host "ストレージアカウント $StorageAccountName を作成..."
Invoke-Az storage account create `
    --name $StorageAccountName `
    --resource-group $ResourceGroupName `
    --location $Location `
    --sku Standard_LRS `
    --kind StorageV2 `
    --https-only true `
    --min-tls-version TLS1_2 `
    --allow-blob-public-access false `
    --allow-shared-key-access false `
    --tags project=McServerManager managedBy=bootstrap-state workload=terraform-state `
    --only-show-errors | Out-Null

Write-Host "バージョン管理と論理削除を有効化..."
Invoke-Az storage account blob-service-properties update `
    --account-name $StorageAccountName `
    --resource-group $ResourceGroupName `
    --enable-versioning true `
    --enable-delete-retention true `
    --delete-retention-days 30 `
    --enable-container-delete-retention true `
    --container-delete-retention-days 30 `
    --only-show-errors | Out-Null

if ([string]::IsNullOrWhiteSpace($AssigneeObjectId)) {
    $AssigneeObjectId = (Invoke-Az ad signed-in-user show --query id -o tsv).Trim()
}

$accountId = (Invoke-Az storage account show --name $StorageAccountName --resource-group $ResourceGroupName --query id -o tsv).Trim()
Write-Host "Storage Blob Data Contributor を $AssigneeObjectId に付与..."
$existing = Invoke-Az role assignment list `
    --assignee $AssigneeObjectId `
    --role "Storage Blob Data Contributor" `
    --scope $accountId `
    --query "length(@)" -o tsv
if ([int]$existing -eq 0) {
    Invoke-Az role assignment create `
        --assignee-object-id $AssigneeObjectId `
        --assignee-principal-type $AssigneePrincipalType `
        --role "Storage Blob Data Contributor" `
        --scope $accountId `
        --only-show-errors | Out-Null
}

# ロールの反映には数分かかることがあるため、コンテナ作成はリトライする
Write-Host "コンテナ $ContainerName を作成..."
$created = $false
for ($attempt = 1; $attempt -le 10 -and -not $created; $attempt++) {
    & az storage container create --name $ContainerName --account-name $StorageAccountName --auth-mode login --only-show-errors | Out-Null
    if ($LASTEXITCODE -eq 0) {
        $created = $true
    }
    else {
        Write-Host "  ロールの反映待ち... ($attempt/10)"
        Start-Sleep -Seconds 30
    }
}
if (-not $created) {
    throw "コンテナを作成できませんでした。数分待ってから再実行してください。"
}

Write-Host ""
Write-Host "完了しました。続けて次を実行してください:"
Write-Host "  terraform init"
