# オーロラ壁紙チェンジャー (Aurora Wallpaper Changer)

[English](README.md) | 日本語

Windows 10 / 11 向けのマルチディスプレイ対応壁紙チェンジャー。タスクトレイに常駐し、モニターごとに指定したフォルダの画像を、決めた間隔で壁紙に設定する。

## 主な機能

- モニターごとに壁紙のフォルダを設定できる（サブフォルダも検索できる）。対応する画像は JPG / JPEG / PNG / BMP / WEBP
- 表示レイアウト: 1 枚表示、4 分割表示（2 × 2）、16 分割表示（4 × 4）、スパン（複数のモニターにまたがる 1 枚表示）
- 1 枚表示の表示方法: 画面を埋める、全体を表示、引き伸ばす、中央に表示（モニターごとに選べる）
- 画像の選び方: ランダム、順送り
- 切り替え間隔: 1 分〜24 時間
- タスクトレイのメニューから「次の壁紙」「前の壁紙」「全壁紙を更新」「一時停止」「この画像を表示しない」「画像の場所を開く」を使える
- 全画面表示のアプリの実行中や、バッテリー駆動の間は、自動の切り替えを待つ（設定で切り替えられる）
- モニターを買い替えたときに、前のモニターの設定をコピーできる
- Windows の起動時に自動的に開始できる
- 設定をファイルに書き出し、ほかの PC や入れ直したあとに読み込める
- 表示言語は日本語と英語

## 動作環境

- Windows 10 バージョン 1809 以降、または Windows 11（x64）
- WEBP の画像を使うには、Windows の WebP コーデックが必要（Windows 11 は標準搭載。Windows 10 は Microsoft Store の「WebP 画像拡張機能」を入れる）

## 入手方法

Microsoft Store で配布する準備をしている。それまでは、ソースからビルドして使う（下の「開発者向けの情報」を参照）。

## 使い方

1. アプリを起動すると、タスクトレイにアイコンが表示される。
2. アイコンをダブルクリックするか、右クリックのメニューの「設定」で、設定画面を開く。
3. モニターを選び、「壁紙フォルダ」に画像のフォルダを追加する。
4. 表示レイアウト・表示方法・画像の選び方・切り替え間隔を選ぶ。

## プライバシー

このアプリは個人情報を収集せず、外部に送信もしない。詳しくは [プライバシーポリシー](docs/PrivacyPolicy.md) を参照。

## お問い合わせ

不具合の報告や要望は [GitHub Issues](https://github.com/SystemAzmax/Aurora/issues) で受け付ける。

## 使用しているソフトウェアのライセンス

[ThirdPartyNotices.txt](WallpaperChanger.UI/ThirdPartyNotices.txt) に記載している。アプリの設定画面の「ライセンス情報」からも開ける。

## 開発者向けの情報

壁紙の設定には `IDesktopWallpaper` COM API を使用する（`SystemParametersInfo` は使用しない）。

### 構成

| プロジェクト | 役割 | 主な型 |
|---|---|---|
| `WallpaperChanger.Core` (`net10.0`) | OS 非依存のモデル・インターフェース・ロジック | `IWallpaperService` `IMonitorService` `ISettingsService` `IImageProvider` `ITrayIconService` `IWallpaperScheduler` / `WallpaperRotationService` `WallpaperScheduler` |
| `WallpaperChanger.Infrastructure` | Windows 依存の実装 | `DesktopWallpaperComHost`（専用 STA スレッド）`DesktopWallpaperService` `DesktopWallpaperMonitorService` `JsonSettingsService` `FileSystemImageProvider` |
| `WallpaperChanger.UI` (WinExe) | WPF / MVVM / タスクトレイ | `Program`（コンポジションルート）`SettingsViewModel` `TrayIconViewModel` `TrayIconService` `SettingsWindowService` |
| `WallpaperChanger.Tests` | xUnit v3 テスト | Core / Infrastructure / ViewModel / 実機 COM 統合テスト |

- View にコードビハインドは無い。`x:Class` から生成される `InitializeComponent()` は `SettingsWindowService` と `Program` から呼び出す。
- 設定ファイル: `%LOCALAPPDATA%\WallpaperChanger\settings.json`
- 履歴ファイル: `%LOCALAPPDATA%\WallpaperChanger\history.json`（モニターごとの「前の壁紙」の履歴と順送りの位置。切り替えのたびに書き込むため、設定ファイルとは分けている）
- アプリアイコン: `tools/IconGenerator/generate-icons.cs` で図形のみから生成したオリジナル（外部素材なし）。作り直す場合は `dotnet run tools/IconGenerator/generate-icons.cs -- <出力先>` を実行し、`D-combined.ico` を `WallpaperChanger.UI/Resources/AppIcon.ico` に上書きする。
- 分割表示（4 分割 / 16 分割）: IDesktopWallpaper は 1 モニター 1 枚のため、`WicWallpaperComposer` が 4 枚・16 枚をモニター解像度の 1 枚に合成して設定する（保存先 `%LOCALAPPDATA%\WallpaperChanger\Composites`、モニターごとに最新 3 枚を保持）。フォルダは左上・右上・左下・右下の 4 単位で指定でき（`MonitorSettings.Tiles`）、4 分割では各マス、16 分割では 2 × 2 の 4 マスずつの区画に適用する（両レイアウトで設定を共有）。マスへの収め方（埋める / 全体を表示）・間隔・間隔の色・空きマスと余白の色は、モニターごとに設定できる（`MonitorSettings.TileStyle`）。
- スパン（複数モニターにまたがる 1 枚表示）: 表示レイアウトに「スパン」を選んだモニターどうしで、1 枚の画像を各モニターの位置（`MonitorInfo.Bounds`）に合わせて切り分けて表示する（Windows の「スパン」は全モニター共通のため使わない）。フォルダ・選び方・収め方はスパン専用の設定（`AppSettings.Span`）で、履歴はスパン全体で 1 つ持つ。
- 自動切り替えを見合わせる条件: 全画面表示のアプリ（`SHQueryUserNotificationState`）やバッテリー駆動（`GetSystemPowerStatus`）の間は、定期実行の切り替えを待ち、当てはまらなくなったら切り替える（`AppSettings.SkipConditions`、既定で両方有効）。手動の切り替えは止めない。
- 表示言語: 日本語（既定）と英語。文言は `WallpaperChanger.UI/Resources/Strings.resx`（日本語）と `Strings.en.resx`（英語）にあり、ビルド時に生成される `Strings` クラスから参照する（XAML は `{x:Static res:Strings.Xxx}`）。文言を足すときは両方のファイルに同じキーで書く（英語が無い・埋め込む値の番号が違う場合はテストが失敗する）。Infrastructure で作る画面向けの文言は `WallpaperChanger.Infrastructure/Resources/Strings.resx` にある。言語の変更はアプリの再起動後に反映し、ログは常に日本語で書く。
- アプリの情報: 設定画面の最後の「アプリの情報」に「このアプリについて」（アプリ名・バージョン・著作権表記。パッケージありで動いているときは「ストア版」、なしのときは「exe 版」）と「ライセンス情報」（`ThirdPartyNotices.txt` を既定のアプリで開く）を置く。表示は、Microsoft Store で配るアプリで共通の決まり（StoreReleaseKit）に従う。アプリ名・バージョン・著作権表記は `Directory.Build.props`、ライセンス表示の作り直しとバージョンの上げ方は [docs/BuildGuide.md](docs/BuildGuide.md)、プライバシーポリシーは [docs/PrivacyPolicy.md](docs/PrivacyPolicy.md)。
- 1 枚表示の表示方法: 既定（`MonitorSettings.SingleImageFit` が null）では画像をそのまま設定し、Windows の「調整方法」で表示させる。Windows の調整方法は全モニター共通のため、モニターごとに「画面を埋める」「全体を表示」「引き伸ばす」「中央に表示」を選んだ場合は、分割表示と同じ `WicWallpaperComposer` でモニター解像度の画像を作って設定する（余白は黒）。
- README は英語版の `README.md` と日本語版の `README_ja.md` に分けている。内容を変えるときは両方を直す。

### ビルド・実行・テスト

```powershell
dotnet build Aurora.slnx
dotnet run --project WallpaperChanger.UI
dotnet test --project WallpaperChanger.Tests
# 対話セッションの無い CI では実機 COM の統合テストを除外する
dotnet test --project WallpaperChanger.Tests -- --filter-not-trait "Category=Integration"
```

master への push と pull request のたびに、GitHub Actions（`.github/workflows/ci.yml`）がクリーンな状態から Release でビルドし、統合テストを除いたテストを実行する。

### 配布 (Self-contained)

```powershell
dotnet publish WallpaperChanger.UI -p:PublishProfile=win-x64
# => artifacts\publish\win-x64\WallpaperChanger.exe（.NET ランタイム同梱の単一ファイル）
```
