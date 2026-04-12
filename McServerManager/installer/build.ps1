param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublicDownloadBaseUrl = "https://stmailpilotje.blob.core.windows.net/public/downloads",
    [string]$ReleaseNotesUrl = "https://www.maipilot.jp/docs/",
    # All   : publish + optional PFX sign + ISCC + optional PFX sign + copy versioned + manifest
    # Publish : dotnet publish only (used in CI before Trusted Signing signs the binary)
    # Package : ISCC + copy versioned only, no signing, no manifest
    #           (used in CI after Trusted Signing has signed the binary)
    [ValidateSet("All", "Publish", "Package")]
    [string]$Phase = "All"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $root "McServerManager.csproj"
$issPath = Join-Path $root "installer\McServerManager.iss"
$publishExe = Join-Path $root "bin\$Configuration\net8.0-windows\$Runtime\publish\McServerManager.exe"
$setupExe = Join-Path $root "installer\dist\MaiPilotSetup.exe"

# ─── Phase: Publish ──────────────────────────────────────────────────────────
if ($Phase -eq "All" -or $Phase -eq "Publish") {
    Write-Host "Publishing ($Configuration, $Runtime)..."
    dotnet publish $projectPath -c $Configuration -r $Runtime
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
}

# ─── PFX signing of app binary (All phase only) ──────────────────────────────
$signTool = $env:SIGNTOOL_PATH
$pfxPath = $env:CODESIGN_PFX
$pfxPassword = $env:CODESIGN_PASSWORD
$timestampUrl = $env:CODESIGN_TIMESTAMP_URL
if ([string]::IsNullOrWhiteSpace($timestampUrl)) { $timestampUrl = "http://timestamp.digicert.com" }

$canSign =
    $Phase -eq "All" -and
    -not [string]::IsNullOrWhiteSpace($signTool) -and
    -not [string]::IsNullOrWhiteSpace($pfxPath) -and
    (Test-Path $signTool) -and
    (Test-Path $pfxPath)

if ($Phase -eq "All") {
    if ($canSign) {
        Write-Host "Signing app binary before packaging..."
        & $signTool sign /fd SHA256 /f $pfxPath /p $pfxPassword /tr $timestampUrl /td SHA256 $publishExe
        if ($LASTEXITCODE -ne 0) { throw "Signing MaiPilot executable failed." }
    } else {
        Write-Host "Skipping app binary signing (SIGNTOOL_PATH or CODESIGN_PFX not set, or Phase != All)."
    }
}

# ─── Early exit for Publish-only ─────────────────────────────────────────────
if ($Phase -eq "Publish") {
    Write-Host "Phase=Publish complete. Binary at: $publishExe"
    exit 0
}

# ─── Phase: Package (ISCC) ───────────────────────────────────────────────────
$isccCandidates = @(
    $env:ISCC_PATH,
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$isccPath = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($isccPath)) {
    throw "ISCC.exe not found. Set ISCC_PATH or install Inno Setup 6."
}

Write-Host "Building installer..."
& $isccPath $issPath
if ($LASTEXITCODE -ne 0) { throw "ISCC failed." }

# ─── PFX signing of installer (All phase only) ───────────────────────────────
if ($canSign) {
    Write-Host "Signing installer..."
    & $signTool sign /fd SHA256 /f $pfxPath /p $pfxPassword /tr $timestampUrl /td SHA256 $setupExe
    if ($LASTEXITCODE -ne 0) { throw "Signing installer failed." }
}

if (-not (Test-Path $setupExe)) { throw "Installer not found: $setupExe" }

# ─── Copy to versioned name ───────────────────────────────────────────────────
[xml]$project = Get-Content $projectPath
$appVersion = ($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ([string]::IsNullOrWhiteSpace($appVersion)) { throw "Version not found in McServerManager.csproj." }

$versionedSetupExe = Join-Path $root "installer\dist\MaiPilotSetup-$appVersion.exe"
Copy-Item -Path $setupExe -Destination $versionedSetupExe -Force
if (-not (Test-Path $versionedSetupExe)) { throw "Versioned installer copy failed: $versionedSetupExe" }
Write-Host "Created versioned installer: $versionedSetupExe"

# ─── Early exit for Package-only (manifest is generated after signing in CI) ─
if ($Phase -eq "Package") {
    Write-Host "Phase=Package complete. Versioned installer: $versionedSetupExe"
    exit 0
}

# ─── Manifest (All phase only, hash computed here after optional PFX signing) ─
$normalizedBaseUrl = $PublicDownloadBaseUrl.TrimEnd("/")
$installerUrl = "$normalizedBaseUrl/MaiPilotSetup-$appVersion.exe"
$sha256 = (Get-FileHash -Path $versionedSetupExe -Algorithm SHA256).Hash.ToLowerInvariant()
$publishedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$manifestPath = Join-Path $root "installer\dist\update.json"

$manifest = [ordered]@{
    version        = $appVersion
    installerUrl   = $installerUrl
    sha256         = $sha256
    publishedAt    = $publishedAt
    releaseNotesUrl = $ReleaseNotesUrl
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), $utf8NoBom)
Write-Host "Generated update manifest: $manifestPath"
