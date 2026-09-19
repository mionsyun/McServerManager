#!/usr/bin/env bash
# MaiPort を Linux（Claude Code on the web / CI コンテナ）で検証する。
#
#   - WPF は Windows でしかビルドできないため、WPF 非依存部分
#     （Models / Services / ViewModels）を net8.0 としてコンパイルする
#   - MaiPort.Tests のユニットテストをそのまま実行する
#
# XAML と WPF コードビハインド、および exe の生成は Windows 環境
#（scripts/build-maiport.ps1 または GitHub Actions: build-maiport.yml）で確認すること。
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet が見つかりません。Ubuntu では以下で導入できます:" >&2
    echo "  sudo apt-get update && sudo apt-get install -y --no-install-recommends dotnet-sdk-8.0" >&2
    exit 1
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

dotnet test "$root/scripts/linux-verify/Tests/Tests.csproj" "$@"
