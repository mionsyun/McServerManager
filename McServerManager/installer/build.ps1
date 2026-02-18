param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $root "McServerManager.csproj"
$issPath = Join-Path $root "installer\McServerManager.iss"
$publishExe = Join-Path $root "bin\$Configuration\net8.0-windows\$Runtime\publish\McServerManager.exe"
$setupExe = Join-Path $root "installer\dist\MaiPilotSetup.exe"

$isccCandidates = @(
    $env:ISCC_PATH,
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$isccPath = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($isccPath)) {
    throw "ISCC.exe not found. Set ISCC_PATH or install Inno Setup 6."
}

Write-Host "Publishing ($Configuration, $Runtime)..."
dotnet publish $projectPath -c $Configuration -r $Runtime
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$signTool = $env:SIGNTOOL_PATH
$pfxPath = $env:CODESIGN_PFX
$pfxPassword = $env:CODESIGN_PASSWORD
$timestampUrl = $env:CODESIGN_TIMESTAMP_URL
if ([string]::IsNullOrWhiteSpace($timestampUrl)) {
    $timestampUrl = "http://timestamp.digicert.com"
}

$canSign =
    -not [string]::IsNullOrWhiteSpace($signTool) -and
    -not [string]::IsNullOrWhiteSpace($pfxPath) -and
    (Test-Path $signTool) -and
    (Test-Path $pfxPath)

if ($canSign) {
    Write-Host "Signing app binary before packaging..."
    & $signTool sign /fd SHA256 /f $pfxPath /p $pfxPassword /tr $timestampUrl /td SHA256 $publishExe
    if ($LASTEXITCODE -ne 0) {
        throw "Signing MaiPilot executable failed."
    }
} else {
    Write-Host "Skipping signing (SIGNTOOL_PATH or CODESIGN_PFX not set)."
}

Write-Host "Building installer..."
& $isccPath $issPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed."
}

if ($canSign) {
    Write-Host "Signing installer..."
    & $signTool sign /fd SHA256 /f $pfxPath /p $pfxPassword /tr $timestampUrl /td SHA256 $setupExe
    if ($LASTEXITCODE -ne 0) {
        throw "Signing installer failed."
    }
}
