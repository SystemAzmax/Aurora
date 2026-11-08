#!/usr/bin/env bash
#
# ThirdPartyNotices.txt を、NuGet パッケージ付属のライセンスファイルなどから作成する。
# NuGet パッケージや .NET のランタイムを更新したら、下のバージョンを書き換えてから実行する
# （詳しくは docs/BuildGuide.md の「ライセンス表示の更新」を参照）。
#
# 使い方（Git Bash で、リポジトリのフォルダーから実行する）:
#   bash tools/notices/build-notices.sh [出力先] [ライセンスファイルのフォルダー]
# 出力先の既定は WallpaperChanger.UI/ThirdPartyNotices.txt。
# ライセンスファイルのフォルダーの既定は tools/notices/licenses
# （パッケージにライセンスファイルが無いソフトウェアの LICENSE を置く）。
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# アプリのプロジェクトのフォルダー（リポジトリのフォルダーからの相対パス）
APP_DIR="WallpaperChanger.UI"
OUT="${1:-$SCRIPT_DIR/../../$APP_DIR/ThirdPartyNotices.txt}"
DL="${2:-$SCRIPT_DIR/licenses}"
P="$(cygpath -u "$USERPROFILE")/.nuget/packages"

# 改行を LF にそろえ、末尾に改行を付けて出力する
emit_file() {
  tr -d '\r' < "$1"
  printf '\n'
}

section() {
  printf '\n\n================================================================================\n'
  printf '%s\n' "$1"
  printf '================================================================================\n\n'
}

{
  printf '\xEF\xBB\xBF'
  cat <<'EOF'
オーロラ壁紙チェンジャー サードパーティ ソフトウェアのライセンス情報
================================================================================

オーロラ壁紙チェンジャー（Aurora Wallpaper Changer）は、次のソフトウェアを使用しています。
各ソフトウェアの著作権表示とライセンスを、原文のまま記載します。

  1. .NET Community Toolkit (CommunityToolkit.Mvvm 8.4.2) - MIT License
  2. Hardcodet.NotifyIcon.Wpf 2.0.1 - MIT License
  3. Microsoft.Extensions（Hosting、DependencyInjection、Logging、Options、Configuration など）10.0.12
     - MIT License
  4. .NET 10（.NET ランタイム）- MIT License
  5. .NET 10 Windows Desktop ランタイム（WPF）- MIT License
  6. Windows SDK の .NET 向けプロジェクション (Microsoft.Windows.SDK.NET.Ref 10.0.17763.57)
     - Microsoft ソフトウェア ライセンス条項

付録として、.NET Community Toolkit と .NET（Microsoft.Extensions を含む）に含まれる
サードパーティのソフトウェアの表示を、各ソフトウェアに付属のファイルから原文のまま記載します。

  付録 A. .NET Community Toolkit - ThirdPartyNotices.txt
  付録 B. .NET - THIRD-PARTY-NOTICES.TXT
EOF

  section "1. .NET Community Toolkit (CommunityToolkit.Mvvm 8.4.2) - MIT License"
  emit_file "$P/communitytoolkit.mvvm/8.4.2/License.md"

  section "2. Hardcodet.NotifyIcon.Wpf 2.0.1 - MIT License"
  emit_file "$P/hardcodet.notifyicon.wpf/2.0.1/LICENSE"

  # パッケージにライセンスファイルが無いため、dotnet/runtime の LICENSE を置いて使う
  section "3. Microsoft.Extensions (Hosting, DependencyInjection, Logging, Options, Configuration, etc.) 10.0.12 - MIT License"
  emit_file "$DL/dotnet-runtime-LICENSE.TXT"

  section "4. .NET 10 (.NET Runtime) - MIT License"
  emit_file "$P/microsoft.netcore.app.runtime.win-x64/10.0.13/LICENSE.TXT"

  section "5. .NET 10 Windows Desktop Runtime (WPF) - MIT License"
  emit_file "$P/microsoft.windowsdesktop.app.runtime.win-x64/10.0.13/LICENSE"

  section "6. Windows SDK .NET Projection (Microsoft.Windows.SDK.NET.Ref 10.0.17763.57) - Microsoft Software License Terms"
  cat <<'EOF'
Copyright (c) Microsoft Corporation. All rights reserved.
Redistributed under the Microsoft Software License Terms for the Windows SDK.
https://aka.ms/WinSDKLicenseURL
EOF

  section "付録 A. .NET Community Toolkit - ThirdPartyNotices.txt"
  emit_file "$P/communitytoolkit.mvvm/8.4.2/ThirdPartyNotices.txt"

  # Microsoft.Extensions の各パッケージに付属の THIRD-PARTY-NOTICES.TXT も、これと同じ内容
  section "付録 B. .NET - THIRD-PARTY-NOTICES.TXT"
  emit_file "$P/microsoft.netcore.app.runtime.win-x64/10.0.13/THIRD-PARTY-NOTICES.TXT"
} | sed 's/$/\r/' > "$OUT"  # Windows で開くファイルのため、改行を CRLF にして書き出す
