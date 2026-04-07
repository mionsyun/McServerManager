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

# ===== べた塗りリダクション (モザイク廃止) =====
# アプリの背景色 (#0f1318) と同色で塗り潰し、視覚的ノイズを最小化
$RedactColor = [System.Drawing.Color]::FromArgb(15, 19, 24)

function Apply-Redact {
    param([System.Drawing.Bitmap]$Bmp, [int]$X, [int]$Y, [int]$W, [int]$H)
    $pad = 2
    $X = [Math]::Max(0, $X - $pad);  $Y = [Math]::Max(0, $Y - $pad)
    $W = [Math]::Min($Bmp.Width - $X, $W + $pad*2)
    $H = [Math]::Min($Bmp.Height - $Y, $H + $pad*2)
    $g = [System.Drawing.Graphics]::FromImage($Bmp)
    $brush = New-Object System.Drawing.SolidBrush($RedactColor)
    $g.FillRectangle($brush, $X, $Y, $W, $H)
    $brush.Dispose(); $g.Dispose()
}

$ipPat   = [regex]'(?<!\d)(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)(?!\d)'
$pathPat = [regex]'(?i)[A-Za-z]:\\(?:Users|ユーザー)\\[^\\\s/]+'

function Get-ElementText([System.Windows.Automation.AutomationElement]$el) {
    # WPF TextBlock は Name にテキストが入る。コンテナ全体を誤検知しないよう
    # 500文字未満のものだけ対象にする。
    try {
        $n = $el.Current.Name
        if ($n -and $n.Length -gt 0 -and $n.Length -lt 500) { return $n }
    } catch {}
    # TextBox / ComboBox は ValuePattern
    try { return $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch {}
    return ""
}

function Detect-And-Redact([System.Drawing.Bitmap]$bmp, [System.Windows.Automation.AutomationElement]$win, [System.Drawing.Rectangle]$winRect) {
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
            if ($isPass) { Apply-Redact $bmp $rx $ry $rw $rh; $count++; continue }

            # テキストがIP・パスに一致する要素をべた塗り
            $text = Get-ElementText $el
            if ($text -and ($ipPat.IsMatch($text) -or $pathPat.IsMatch($text))) {
                Apply-Redact $bmp $rx $ry $rw $rh; $count++
            }
        } catch {}
    }
    return $count
}

# ===== ネットワークタブ: アドレスセクションをUIAutomationで特定して塗り潰し =====
function Redact-NetworkAddressSection([System.Drawing.Bitmap]$bmp,
                                      [System.Windows.Automation.AutomationElement]$win,
                                      [System.Drawing.Rectangle]$winRect) {
    try {
        # "LAN IP" ラベル TextBlock は DataTemplate 外なので UIAutomation で取得できる
        $condText = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)
        $condLan = [System.Windows.Automation.AndCondition]::new($condText,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty, "LAN IP"))

        $lanEl = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condLan)
        if ($lanEl) {
            $eb = $lanEl.Current.BoundingRectangle
            # "LAN IP" ラベルの直下・IP数値の幅 (約160px) だけ塗り潰す
            $rx = [int]($eb.X - $winRect.X)
            $ry = [int]($eb.Y + $eb.Height - $winRect.Y) + 2
            $rw = 160   # IP アドレス文字列の最大幅
            $rh = 26    # 1行ぶん
            Apply-Redact $bmp $rx $ry $rw $rh
            return $true
        }
    } catch {}
    return $false
}

function Redact-SensitiveAreas([System.Drawing.Bitmap]$bmp, [string]$shotName, [int]$uaCount,
                                [System.Windows.Automation.AutomationElement]$win,
                                [System.Drawing.Rectangle]$winRect) {
    if ($shotName -ne '03-network-tab') { return }
    # UIAutomation でアドレスセクション上端を特定できれば使う、なければ座標ベース
    $ok = Redact-NetworkAddressSection $bmp $win $winRect
    if (-not $ok) {
        # フォールバック: IP 値テキストの実測範囲
        # x: 右パネル内 (x=36-50%)、y: IP値行 (y=79-85%)
        Apply-Redact $bmp ([int]($bmp.Width*0.36)) ([int]($bmp.Height*0.79)) `
                          ([int]($bmp.Width*0.14)) ([int]($bmp.Height*0.05))
    }
}

# ===== スクリーンショット保存 =====
function Save-Screenshot([System.Windows.Automation.AutomationElement]$winEl, [string]$name) {
    $proc = Get-Process -Name "McServerManager" -ErrorAction SilentlyContinue
    if ($proc) {
        [WinApi]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
        # 最大化状態を UIAutomation で保証
        try {
            $wp = $winEl.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
            if ($wp.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Maximized) {
                $wp.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
                Start-Sleep -Milliseconds 600
            }
        } catch {
            [WinApi]::ShowWindow($proc.MainWindowHandle, 3) | Out-Null
        }
        Start-Sleep -Milliseconds 300
    }

    $b = $winEl.Current.BoundingRectangle
    # Windows 11 の角丸・ウィンドウ枠による背景透過を避けるため 4px 内側でクロップ
    $inset = 4
    $rect = [System.Drawing.Rectangle]::new(
        [int]$b.X + $inset,
        [int]$b.Y + $inset,
        [int]$b.Width  - $inset * 2,
        [int]$b.Height - $inset * 2)
    $bmp = New-Object System.Drawing.Bitmap($rect.Width, $rect.Height)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Location, [System.Drawing.Point]::Empty, $rect.Size)
    $g.Dispose()

    $redactCount = Detect-And-Redact $bmp $winEl $rect
    if (-not $NoMosaic) {
        Redact-SensitiveAreas $bmp $name $redactCount $winEl $rect
    }

    $outPath = Join-Path $outDir "$name.png"
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $redactStr = if ($redactCount -gt 0) { " (リダクション:${redactCount}箇所)" } else { "" }
    Log-Ok "保存: $name.png$redactStr"
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

# ウィンドウを最大化して前面に (UIAutomation WindowPattern を優先、Win32 をフォールバック)
[WinApi]::SetForegroundWindow($hwnd) | Out-Null
try {
    $winPat = $winEl.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    $winPat.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
} catch {
    [WinApi]::ShowWindow($hwnd, 3) | Out-Null   # SW_MAXIMIZE フォールバック
}
Start-Sleep -Milliseconds 1800   # 最大化アニメーション + 初回ロード待ち

Log-Ok "ウィンドウ取得完了"
Dismiss-AllDialogs   # 起動時に残っているダイアログを全て閉じる

# ===== STEP 3: スクリーンショット撮影 =====
Log-Step "スクリーンショット撮影開始"

# ---- 01: メイン画面 ----
Log-Info "01-main-window"
$winEl = Get-MainWindowElement
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
