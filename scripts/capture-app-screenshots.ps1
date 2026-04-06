<#
.SYNOPSIS
    MaiPilot のビルド・起動・スクリーンショット撮影・モザイク・記事差し込みを全自動で行います。

.EXAMPLE
    .\scripts\capture-app-screenshots.ps1
    .\scripts\capture-app-screenshots.ps1 -SkipBuild      # ビルドをスキップ
    .\scripts\capture-app-screenshots.ps1 -SkipBuild -SkipInject  # 撮影のみ
    .\scripts\capture-app-screenshots.ps1 -NoMosaic        # モザイクなし確認用
#>
param(
    [switch]$SkipBuild,
    [switch]$SkipInject,
    [switch]$NoMosaic,
    [int]$MosaicBlockSize = 14
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ===== パス定義 =====
$repoRoot   = Split-Path -Parent $PSScriptRoot
$projFile   = Join-Path $repoRoot "McServerManager\McServerManager.csproj"
$exePath    = Join-Path $repoRoot "McServerManager\bin\Release\net8.0-windows\win-x64\McServerManager.exe"
$outDir     = Join-Path $repoRoot "McServerManager\landing\public\images\screenshots"
$injectScript = Join-Path $repoRoot "scripts\inject-screenshots.mjs"

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

# ===== Win32 API (最前面表示・最大化) =====
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class WinApi {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
}
'@

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# ===== ユーティリティ =====
function Log-Step([string]$msg) {
    Write-Host ""
    Write-Host "▶ $msg" -ForegroundColor Cyan
}
function Log-Ok([string]$msg)   { Write-Host "  ✓ $msg" -ForegroundColor Green }
function Log-Warn([string]$msg) { Write-Host "  ! $msg" -ForegroundColor Yellow }
function Log-Info([string]$msg) { Write-Host "  $msg" -ForegroundColor Gray }

# ===== モザイク =====
function Apply-Mosaic {
    param([System.Drawing.Bitmap]$Bmp, [int]$X, [int]$Y, [int]$W, [int]$H, [int]$Block = 14)
    $X = [Math]::Max(0, $X - 2);  $Y = [Math]::Max(0, $Y - 2)
    $W = [Math]::Min($Bmp.Width - $X, $W + 4);  $H = [Math]::Min($Bmp.Height - $Y, $H + 4)
    $bx = $X
    while ($bx -lt $X + $W) {
        $by = $Y
        while ($by -lt $Y + $H) {
            $cx = [Math]::Min($bx + [int]($Block/2), $Bmp.Width-1)
            $cy = [Math]::Min($by + [int]($Block/2), $Bmp.Height-1)
            $color = $Bmp.GetPixel($cx, $cy)
            $ex = [Math]::Min($bx + $Block, $X + $W)
            $ey = [Math]::Min($by + $Block, $Y + $H)
            for ($px = $bx; $px -lt $ex; $px++) {
                for ($py = $by; $py -lt $ey; $py++) {
                    if ($px -lt $Bmp.Width -and $py -lt $Bmp.Height) { $Bmp.SetPixel($px,$py,$color) }
                }
            }
            $by += $Block
        }
        $bx += $Block
    }
}

$ipPat = [regex]'(?<!\d)(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)(?!\d)'

function Detect-And-Mosaic([System.Drawing.Bitmap]$bmp, [System.Windows.Automation.AutomationElement]$win, [System.Drawing.Rectangle]$winRect) {
    if ($NoMosaic) { return 0 }
    $count = 0
    try {
        $elements = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                                 [System.Windows.Automation.Condition]::TrueCondition)
    } catch { return 0 }

    foreach ($el in $elements) {
        try {
            $eb = $el.Current.BoundingRectangle
            if ($eb.Width -le 0 -or $eb.Height -le 0) { continue }
            $rx = [int]($eb.X - $winRect.X);  $ry = [int]($eb.Y - $winRect.Y)
            $rw = [int]$eb.Width;              $rh = [int]$eb.Height
            if ($rx -lt 0 -or $ry -lt 0 -or $rx -ge $bmp.Width -or $ry -ge $bmp.Height) { continue }

            # パスワードフィールド
            $isPass = $false
            try { $isPass = [bool]$el.Current.IsPassword } catch {}
            if ($isPass) { Apply-Mosaic $bmp $rx $ry $rw $rh -Block $MosaicBlockSize; $count++; continue }

            # テキスト値を取得してIP検査
            $ct = $el.Current.ControlType
            if ($ct -eq [System.Windows.Automation.ControlType]::Edit -or
                $ct -eq [System.Windows.Automation.ControlType]::Text -or
                $ct -eq [System.Windows.Automation.ControlType]::Document -or
                $ct -eq [System.Windows.Automation.ControlType]::DataItem -or
                $ct -eq [System.Windows.Automation.ControlType]::ListItem) {
                $text = ""
                try { $vp = $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern); $text = $vp.Current.Value } catch {}
                if (-not $text) { try { $text = $el.Current.Name } catch {} }
                if ($text -and ($ipPat.IsMatch($text) -or $pathPat.IsMatch($text))) {
                    Apply-Mosaic $bmp $rx $ry $rw $rh -Block $MosaicBlockSize; $count++
                }
            }
        } catch {}
    }
    return $count
}

# ===== スクリーンショット保存 =====
function Save-Screenshot([System.Windows.Automation.AutomationElement]$winEl, [string]$name) {
    $proc = Get-Process -Name "McServerManager" -ErrorAction SilentlyContinue
    if ($proc) {
        [WinApi]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 300
    }

    $b = $winEl.Current.BoundingRectangle
    $rect = [System.Drawing.Rectangle]::new([int]$b.X, [int]$b.Y, [int]$b.Width, [int]$b.Height)
    $bmp = New-Object System.Drawing.Bitmap($rect.Width, $rect.Height)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Location, [System.Drawing.Point]::Empty, $rect.Size)
    $g.Dispose()

    $mosaicCount = Detect-And-Mosaic $bmp $winEl $rect
    $path = Join-Path $outDir "$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $mosaicStr = if ($mosaicCount -gt 0) { " (モザイク:${mosaicCount}箇所)" } else { "" }
    Log-Ok "保存: $name.png$mosaicStr"
}

# ===== UIAutomation ヘルパー =====
function Find-Element {
    param($root, [string]$name = "", [System.Windows.Automation.ControlType]$type = $null, [int]$timeoutMs = 5000)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        try {
            $conditions = [System.Collections.Generic.List[System.Windows.Automation.Condition]]::new()
            if ($type -ne $null) {
                $conditions.Add([System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type))
            }
            if ($name) {
                $conditions.Add([System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $name))
            }
            $cond = if ($conditions.Count -eq 1) { $conditions[0] }
                    elseif ($conditions.Count -gt 1) { [System.Windows.Automation.AndCondition]::new($conditions.ToArray()) }
                    else { [System.Windows.Automation.Condition]::TrueCondition }
            $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
            if ($el) { return $el }
        } catch {}
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Click-Button($root, [string]$name) {
    $btn = Find-Element $root $name ([System.Windows.Automation.ControlType]::Button)
    if ($btn) {
        try { $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch {}
        return $true
    }
    return $false
}

function Select-Tab($root, [string]$header) {
    $tab = Find-Element $root $header ([System.Windows.Automation.ControlType]::TabItem)
    if ($tab) {
        try { $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch {
            # フォールバック: クリック
            try {
                $pt = $tab.GetClickablePoint()
                [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$pt.X, [int]$pt.Y)
                Start-Sleep -Milliseconds 50
                $mouse = [System.Windows.Forms.MouseEventArgs]::new([System.Windows.Forms.MouseButtons]::Left, 1, [int]$pt.X, [int]$pt.Y, 0)
                [System.Windows.Forms.Application]::DoEvents()
                Add-Type -AssemblyName Microsoft.VisualBasic
                [Microsoft.VisualBasic.Interaction]::AppActivate((Get-Process -Name "McServerManager").Id)
                [System.Windows.Forms.SendKeys]::SendWait("")
            } catch {}
        }
        Start-Sleep -Milliseconds 800
        return $true
    }
    return $false
}


# ===== 開いているダイアログを全て閉じる =====
function Dismiss-AllDialogs {
    $proc = Get-Process -Name 'McServerManager' -ErrorAction SilentlyContinue
    if (-not $proc) { return }
    $mainHwnd = [int64]$proc.MainWindowHandle
    $desktop  = [System.Windows.Automation.AutomationElement]::RootElement

    # プロセスIDで直接絞り込み
    $pidCond = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
    $appWins = $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCond)

    $dismissed = 0
    foreach ($w in $appWins) {
        try {
            $h = [int64]$w.Current.NativeWindowHandle
            if ($h -le 0 -or $h -eq $mainHwnd) { continue }
            # メインウィンドウ以外 = ダイアログ → 前面に出して Escape
            [WinApi]::SetForegroundWindow([IntPtr]::new($h)) | Out-Null
            Start-Sleep -Milliseconds 300
            [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
            Start-Sleep -Milliseconds 500
            $dismissed++
        } catch {}
    }
    # メインウィンドウを前面に戻す
    try { [WinApi]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null } catch {}
    if ($dismissed -gt 0) {
        Start-Sleep -Milliseconds 800
        Log-Info "ダイアログを ${dismissed} 個閉じました"
    }
}

function Press-Escape {
    [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
    Start-Sleep -Milliseconds 500
}

function Get-MainWindowElement {
    $proc = Get-Process -Name "McServerManager" -ErrorAction SilentlyContinue
    if (-not $proc -or $proc.MainWindowHandle -eq 0) { return $null }
    return [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
}

function Wait-ForWindow([int]$timeoutSec = 30) {
    Log-Info "ウィンドウ待機中..."
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        $el = Get-MainWindowElement
        if ($el -and $el.Current.BoundingRectangle.Width -gt 0) { return $el }
        Start-Sleep -Milliseconds 500
    }
    throw "タイムアウト: ウィンドウが表示されませんでした"
}

# ===== STEP 1: ビルド =====
if (-not $SkipBuild) {
    Log-Step "ビルド (Release)"
    $buildResult = & dotnet build $projFile --configuration Release --nologo -v q 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host $buildResult -ForegroundColor Red
        throw "ビルド失敗"
    }
    Log-Ok "ビルド完了"
} else {
    Log-Info "ビルドスキップ"
}

if (-not (Test-Path $exePath)) { throw "実行ファイルが見つかりません: $exePath" }

# ===== STEP 2: 起動 =====
Log-Step "アプリ起動"
# クリーンな状態にするため既存プロセスを終了してから再起動する
$existing = Get-Process -Name "McServerManager" -ErrorAction SilentlyContinue
if ($existing) {
    Log-Info "既存プロセスを終了中..."
    $existing | Stop-Process -Force
    Start-Sleep -Milliseconds 1200
}
Start-Process $exePath -WorkingDirectory (Split-Path $exePath)
Log-Info "起動しました: $exePath"

$winEl = Wait-ForWindow 30
$proc  = Get-Process -Name "McServerManager"
$hwnd  = $proc.MainWindowHandle

# ウィンドウを最大化して前面に
[WinApi]::ShowWindow($hwnd, 3) | Out-Null   # SW_MAXIMIZE
[WinApi]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 1500   # 初回ロード待ち

Log-Ok "ウィンドウ取得完了"
Dismiss-AllDialogs   # 起動時に残っているダイアログを全て閉じる

# ===== STEP 3: スクリーンショット撮影 =====
Log-Step "スクリーンショット撮影開始"

# ---- 01: メイン画面 ----
Log-Info "01-main-window"
$winEl = Get-MainWindowElement
# アプリを確実に前面に出してから撮影
[WinApi]::ShowWindow($hwnd, 9) | Out-Null   # SW_RESTORE
[WinApi]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 1000
Save-Screenshot $winEl "01-main-window"

$winEl = Get-MainWindowElement

# ---- サーバーリスト内の最初のアイテムをクリック ----
Log-Info "サーバーリストの最初のアイテムを選択..."
$serverList = Find-Element $winEl "" ([System.Windows.Automation.ControlType]::List) 3000
if (-not $serverList) {
    $serverList = Find-Element $winEl "" ([System.Windows.Automation.ControlType]::ListBox) 3000
}

$firstServer = $null
if ($serverList) {
    try {
        $itemCond = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)
        $items = $serverList.FindAll([System.Windows.Automation.TreeScope]::Children, $itemCond)
        if ($items.Count -gt 0) {
            $firstServer = $items[0]
            try { $firstServer.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch {}
            Start-Sleep -Milliseconds 1000
            Log-Ok "サーバー選択: $($firstServer.Current.Name)"
        } else {
            Log-Warn "サーバーが登録されていません。タブ画面はスキップします。"
        }
    } catch { Log-Warn "サーバーリスト操作失敗: $_" }
}

# ---- タブごとにスクリーンショット ----
$tabShots = @(
    @{ Tab = "コンソール";    File = "02-server-console"    }
    @{ Tab = "ネットワーク";  File = "03-network-tab"       }
    @{ Tab = "ワールド";      File = "04-world-tab"         }
    @{ Tab = "MOD/プラグイン"; File = "05-addon-tab"        }
    @{ Tab = "リソースパック"; File = "06-resource-pack-tab" }
    @{ Tab = "設定";          File = "07-settings-tab"      }
)

if ($firstServer) {
    $winEl = Get-MainWindowElement
    foreach ($s in $tabShots) {
        Log-Info "$($s.File)"
        Dismiss-AllDialogs   # タブ切り替え前に残っているダイアログを閉じる
        $ok = Select-Tab $winEl $s.Tab
        if ($ok) {
            $winEl = Get-MainWindowElement
            Save-Screenshot $winEl $s.File
        } else {
            Log-Warn "タブ「$($s.Tab)」が見つかりません"
        }
    }
} else {
    Log-Warn "サーバー未選択のためタブ画面をスキップ"
}

Write-Host ""
Log-Ok "全スクリーンショット完了 → $outDir"

# ===== STEP 4: 記事への差し込み =====
if (-not $SkipInject) {
    Log-Step "記事へ画像差し込み (inject-screenshots.mjs)"
    $nodeResult = & node $injectScript 2>&1
    $nodeResult | ForEach-Object { Log-Info $_ }
    Log-Ok "差し込み完了"
}

Write-Host ""
Write-Host "======================================" -ForegroundColor Green
Write-Host "  全自動撮影 完了！" -ForegroundColor Green
Write-Host "  出力: $outDir" -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Green
