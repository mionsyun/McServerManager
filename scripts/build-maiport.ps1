<#
.SYNOPSIS
    MaiPort（ポート開放ツール）をビルド・テストし、配布用 exe を生成する。

.DESCRIPTION
    Windows 上で実行する。既定では Release ビルド → ユニットテスト → 自己完結型の
    単一ファイル exe を dist/maiport-selfcontained/MaiPort.exe に出力する。

.EXAMPLE
    pwsh -File scripts/build-maiport.ps1
    pwsh -File scripts/build-maiport.ps1 -SelfContained:$false   # .NET 8 Desktop Runtime が必要な軽量exe
    pwsh -File scripts/build-maiport.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "MaiPort/MaiPort.csproj"
$testProject = Join-Path $root "MaiPort.Tests/MaiPort.Tests.csproj"
$outputName = if ($SelfContained) { "maiport-selfcontained" } else { "maiport-framework-dependent" }
$output = Join-Path $root "dist/$outputName"

Write-Host "== Build ($Configuration) =="
dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

if (-not $SkipTests) {
    Write-Host "== Test =="
    dotnet test $testProject -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed." }
}

Write-Host "== Publish ($Runtime, SelfContained=$SelfContained) =="
$publishArgs = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $SelfContained.ToString().ToLowerInvariant(),
    "-p:PublishSingleFile=true",
    "-o", $output
)
if ($SelfContained) {
    $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
    $publishArgs += "-p:EnableCompressionInSingleFile=true"
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$exe = Join-Path $output "MaiPort.exe"
if (-not (Test-Path $exe)) { throw "Executable not found: $exe" }

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "完成: $exe"
Write-Host "サイズ: $sizeMb MB / sha256=$hash"
