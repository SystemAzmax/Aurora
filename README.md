# 壁紙チェンジャー (WallpaperChanger)

Windows 10 / 11 向けのマルチディスプレイ対応壁紙チェンジャー。タスクトレイに常駐し、モニターごとに指定フォルダの画像を定期的に壁紙へ設定する。壁紙の設定には `IDesktopWallpaper` COM API を使用する（`SystemParametersInfo` は使用しない）。

## 構成

| プロジェクト | 役割 | 主な型 |
|---|---|---|
| `WallpaperChanger.Core` (`net10.0`) | OS 非依存のモデル・インターフェース・ロジック | `IWallpaperService` `IMonitorService` `ISettingsService` `IImageProvider` `ITrayIconService` `IWallpaperScheduler` / `WallpaperRotationService` `WallpaperScheduler` |
| `WallpaperChanger.Infrastructure` | Windows 依存の実装 | `DesktopWallpaperComHost`（専用 STA スレッド）`DesktopWallpaperService` `DesktopWallpaperMonitorService` `JsonSettingsService` `FileSystemImageProvider` |
| `WallpaperChanger.UI` (WinExe) | WPF / MVVM / タスクトレイ | `Program`（コンポジションルート）`SettingsViewModel` `TrayIconViewModel` `TrayIconService` `SettingsWindowService` |
| `WallpaperChanger.Tests` | xUnit v3 テスト | Core / Infrastructure / ViewModel / 実機 COM 統合テスト |

- View にコードビハインドは無い。`x:Class` から生成される `InitializeComponent()` は `SettingsWindowService` と `Program` から呼び出す。
- 設定ファイル: `%LOCALAPPDATA%\WallpaperChanger\settings.json`
- アプリアイコン: `tools/IconGenerator/generate-icons.cs` で図形のみから生成したオリジナル（外部素材なし）。作り直す場合は `dotnet run tools/IconGenerator/generate-icons.cs -- <出力先>` を実行し、`D-combined.ico` を `WallpaperChanger.UI/Resources/AppIcon.ico` に上書きする。
- 分割表示（4 分割 / 16 分割）: IDesktopWallpaper は 1 モニター 1 枚のため、`WicWallpaperComposer` が 4 枚・16 枚をモニター解像度の 1 枚に合成して設定する（保存先 `%LOCALAPPDATA%\WallpaperChanger\Composites`、モニターごとに最新 3 枚を保持）。フォルダは左上・右上・左下・右下の 4 単位で指定でき（`MonitorSettings.Tiles`）、4 分割では各マス、16 分割では 2 × 2 の 4 マスずつの区画に適用する（両レイアウトで設定を共有）。

## ビルド・実行・テスト

```powershell
dotnet build Aurora.slnx
dotnet run --project WallpaperChanger.UI

dotnet test --project WallpaperChanger.Tests
# 対話セッションの無い CI では実機 COM の統合テストを除外する
dotnet test --project WallpaperChanger.Tests -- --filter-not-trait "Category=Integration"
```

## 配布 (Self-contained)

```powershell
dotnet publish WallpaperChanger.UI -p:PublishProfile=win-x64
# => artifacts\publish\win-x64\WallpaperChanger.exe（.NET ランタイム同梱の単一ファイル）
```

WEBP の表示・壁紙設定には Windows の WebP コーデックが必要（Windows 11 は標準搭載。Windows 10 は「WebP 画像拡張機能」を導入）。
