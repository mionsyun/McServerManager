param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$root = Resolve-Path (Join-Path $PSScriptRoot "..")

Write-Host "Publishing ($Configuration, $Runtime)..."
dotnet publish -c $Configuration -r $Runtime
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

Write-Host "Building installer..."
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (Join-Path $root "installer\\McServerManager.iss")
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed."
}

$signTool = $env:SIGNTOOL_PATH
$pfxPath = $env:CODESIGN_PFX
$pfxPassword = $env:CODESIGN_PASSWORD
$timestampUrl = $env:CODESIGN_TIMESTAMP_URL
if ([string]::IsNullOrWhiteSpace($timestampUrl)) {
    $timestampUrl = "http://timestamp.digicert.com"
}

if (![string]::IsNullOrWhiteSpace($signTool) -and (Test-Path $signTool) -and
    ![string]::IsNullOrWhiteSpace($pfxPath) -and (Test-Path $pfxPath)) {
    $publishExe = Join-Path $root "bin\\$Configuration\\net8.0-windows\\$Runtime\\publish\\McServerManager.exe"
    $setupExe = Join-Path $root "installer\\dist\\BlockPilotSetup.exe"

    Write-Host "Signing binaries..."
    & $signTool sign /fd SHA256 /f $pfxPath /p $pfxPassword /tr $timestampUrl /td SHA256 $publishExe
    if ($LASTEXITCODE -ne 0) {
        throw "Signing McServerManager.exe failed."
    }
    & $signTool sign /fd SHA256 /f $pfxPath /p $pfxPassword /tr $timestampUrl /td SHA256 $setupExe
    if ($LASTEXITCODE -ne 0) {
        throw "Signing installer failed."
    }
} else {
    Write-Host "Skipping signing (SIGNTOOL_PATH or CODESIGN_PFX not set)."
}
