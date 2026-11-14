# ビルド方法

ビルド・実行・テストのコマンドと、exe 版の発行は [README_ja.md](../README_ja.md) の「ビルド・実行・テスト」「配布 (Self-contained)」を参照してください。
ここには、ストア版のビルドと、リリースのときに行う作業をまとめます。

## ストア版のビルド

ストア版（MSIX）のパッケージは、パッケージのプロジェクト `WallpaperChanger.Package`
（Windows アプリケーション パッケージ プロジェクト）で作ります。
Visual Studio のビルドの仕組みを使うため、`dotnet build` ではビルドできません。
ソリューション プラットフォームが Any CPU のとき（`dotnet build Aurora.slnx` を含む）は、このプロジェクトはビルドされません。

### はじめに 1 回だけ: ストアと関連付ける

1. ソリューション エクスプローラーで **WallpaperChanger.Package** を右クリックし、
   **公開** → **アプリケーションをストアと関連付ける**（英語版は Publish → Associate App with the Store）を選びます。
2. Partner Center にサインインし、予約済みのアプリ名（オーロラ壁紙チェンジャー）を選びます。
3. `Package.appxmanifest` の `Identity` の `Name` と `Publisher` が Partner Center のアプリの値に書き換わり、
   `Package.StoreAssociation.xml` ができます。どちらもコミットします。
   - 書き換わる前の `Name` と `Publisher`（`SystemAzmax.AuroraWallpaperChanger`、`CN=SystemAzmax`）は仮の値です。
   - パッケージ名が変わるため、手元で試したときのデータの保存先（`%LOCALAPPDATA%\Packages\<パッケージ名>`）も変わります。

### 手元で試す

1. Windows の **設定** → **システム** → **開発者向け** で、**開発者モード** をオンにします。
2. Visual Studio のツール バーで、ソリューション プラットフォームを **x64** にし、
   **WallpaperChanger.Package** をスタートアップ プロジェクトにして実行します。
   パッケージがこの PC に登録され、ストア版として起動します（「このアプリについて」に「ストア版」と出ます）。
3. 次を確かめます。
   - 壁紙（1 枚表示・分割表示・スパン・表示方法の指定）が表示される。分割表示などで作った画像を、壁紙を描くエクスプローラーが読めるか
   - 「Windows の起動時に自動的に開始する」を切り替えると、タスク マネージャーの **スタートアップ アプリ** に反映される。
     タスク マネージャーで無効にしたあとに設定画面で有効にすると、Windows の設定で有効にするよう案内が出る
   - 「ログフォルダを開く」でログが見える。設定が保存され、再起動しても残る
- exe 版とストア版は、同時には起動できません（多重起動の防止）。試す前に、起動中の exe 版を終了してください。
- ストア版のデータ（設定・履歴・合成画像・ログ）は、`%LOCALAPPDATA%\Packages\<パッケージ ファミリ名>\LocalState` に保存されます。
  exe 版の `%LOCALAPPDATA%\WallpaperChanger` とは別のため、exe 版の設定を使うときは「設定の書き出し・読み込み」で移します。
- 試し終わったら、スタート メニューのアプリを右クリックしてアンインストールできます。

### 提出するパッケージを作る

1. ソリューション エクスプローラーで **WallpaperChanger.Package** を右クリックし、
   **公開** → **アプリ パッケージの作成**（英語版は Publish → Create App Packages）を選びます。
2. 配布方法で **Microsoft Store (オーロラ壁紙チェンジャー)** を選びます。
3. アーキテクチャの **x86**・**x64**・**ARM64** にチェックを入れ、構成を **Release** にして **作成** を押します。
4. `WallpaperChanger.Package\AppPackages\` にできた `.msixupload` を Partner Center にアップロードします。
   最後に出る **Windows アプリ認定キット** も実行しておくと、提出前に問題を見つけられます。

- 3 つのアーキテクチャのパッケージ（`.msixbundle`）を作る設定は、wapproj に保存してあります
  （`AppxBundlePlatforms`、`AppxBundle`、バージョンを自動で増やさない `AppxAutoIncrementPackageRevision`、
  認定キット用の `GenerateTestArtifacts`）。
- コマンドラインでパッケージのビルドだけを確かめるときは、Visual Studio の MSBuild を使います（署名はしません）。

  ```powershell
  & "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" Aurora.slnx -restore -p:Configuration=Release -p:Platform=x64
  ```

### パッケージの中身

- アプリは CPU ごとの発行プロファイル（`WallpaperChanger.UI\Properties\PublishProfiles\win-x86.pubxml`・`win-x64.pubxml`・`win-arm64.pubxml`）で、
  .NET ランタイムを同梱し、単一ファイルにせずに発行したものが入ります。Store には .NET のフレームワーク パッケージが無いためです。
  exe 版の発行プロファイル（`exe-win-x64.pubxml`）は、ストア版には影響しません。
- パッケージの表示名と説明は、`WallpaperChanger.Package\Strings\ja-JP\Resources.resw`（日本語）と
  `Strings\en-US\Resources.resw`（英語）にあります。表示名は、ストアで予約したアプリ名と同じにします。
- 自動起動は、マニフェストのスタートアップ タスク（`windows.startupTask`）で切り替えます。
  `TaskId` は `PackagedStartupRegistrationService.TaskId` と同じ値にします。

## バージョンの上げ方

「このアプリについて」に表示するバージョンは、版によって読み取る場所が違います。
リリースのときは、次の 2 か所を同じバージョンにそろえてください。

| | 読み取る場所 | 書き方の例 |
| --- | --- | --- |
| ストア版 | `WallpaperChanger.Package\Package.appxmanifest` の `Identity` の `Version` | `1.1.0.0` |
| exe 版 | `Directory.Build.props` の `<Version>` | `1.1.0` |

- ストアに提出するパッケージは、4 つ目の数字（リビジョン）を 0 にする必要があります。
- 画面では、4 つ目の数字が 0 のときは省いて `1.1.0` のように表示します。
- アプリ名（`<Product>`）と著作権表記（`<Copyright>`）も `Directory.Build.props` にあります。
  著作権表記は、Partner Center のストア登録情報の「著作権と商標の情報」と同じ文にします。

## ライセンス表示の更新

「ライセンス情報」ボタンで開く `WallpaperChanger.UI\ThirdPartyNotices.txt` は、
アプリと一緒に配布するソフトウェアのライセンスを、原文のまま並べたものです。
NuGet パッケージを追加・更新したときや、.NET のランタイムを更新したときは、このファイルも更新してください。

ファイルは `tools\notices\build-notices.sh` で作ります。

1. スクリプトに書いてあるパッケージのバージョン（フォルダー名と、冒頭の一覧・各節の見出し）を、
   使っているバージョンに書き換えます。パッケージを追加したときは、節も追加します。
   配布物に入る DLL は、自己完結型で単一ファイルにせずに発行すると確かめられます
   （`dotnet publish WallpaperChanger.UI -c Release -p:PublishProfile=win-x64 -o <出力先>`。ストア版と同じ発行プロファイル）。
   .NET のランタイムのバージョンは、出力先の `WallpaperChanger.deps.json` の `runtimepack.` で始まる項目で分かります。
2. 一度ビルド（.NET のランタイムを更新した場合は、上の発行も）して、NuGet のキャッシュにパッケージを取得しておきます。
3. Git Bash で、リポジトリのフォルダーから次を実行します。
   `WallpaperChanger.UI\ThirdPartyNotices.txt` が上書きされます。

   ```bash
   bash tools/notices/build-notices.sh
   ```

各節の取り出し元は次のとおりです。

- 各節の内容は、`%USERPROFILE%\.nuget\packages\<パッケージ名>\<バージョン>\` にある
  `LICENSE`、`License.md`、`ThirdPartyNotices.txt` などから取っています。
  .NET の節は `microsoft.netcore.app.runtime.win-x64` の `LICENSE.TXT` と `THIRD-PARTY-NOTICES.TXT` です。
- Windows Desktop ランタイム（WPF）の節は、`microsoft.windowsdesktop.app.runtime.win-x64` の `LICENSE` です。
- パッケージにライセンスファイルが無い Microsoft.Extensions の各パッケージは、
  そのリポジトリ（dotnet/runtime）の LICENSE を `tools\notices\licenses\dotnet-runtime-LICENSE.TXT` に置いて、そこから取っています。
  各パッケージに付属の `THIRD-PARTY-NOTICES.TXT` は .NET のものと同じ内容のため、付録 B で兼ねています。

## アイコンとパッケージ画像の作り直し

exe とウィンドウのアイコン（`WallpaperChanger.UI\Resources\AppIcon.ico`）と、
ストア版のパッケージ画像（`WallpaperChanger.Package\Images` の 67 枚）は、
`tools\IconGenerator\generate-icons.cs` が図形だけで描いています（外部の画像や素材は使っていません）。
画像の種類と大きさは、StoreReleaseKit の `build-package-images.ps1` と同じです。

1. アイコンを作ります。出力先には、作業用のフォルダーを指定します。

   ```powershell
   dotnet run tools/IconGenerator/generate-icons.cs -- $env:TEMP\AuroraIcon
   ```

   `D-combined.ico` を `WallpaperChanger.UI\Resources\AppIcon.ico` に上書きコピーします。
2. パッケージ画像を作ります。`WallpaperChanger.Package\Images` の 67 枚が上書きされます。

   ```powershell
   dotnet run tools/IconGenerator/generate-icons.cs -- --package WallpaperChanger.Package/Images
   ```

   確認用のプレビュー（`%TEMP%\AuroraIcon\preview-package.png`）も作られます。

- 画像の種類や大きさを変えたときは、`Package.appxmanifest` の指定も合わせて確認してください。

## ストア登録情報のスクリーンショット

Partner Center のストア登録情報に載せるスクリーンショット（2560 × 1440 の PNG、日本語と英語で 5 枚ずつ）は、
`tools\StoreScreenshots\make-screenshots.cs` で作ります。
アプリの本物の設定画面とタスクトレイのメニューを、サンプルの設定で動かして描き、
このスクリプトが図形だけで描いた壁紙（オーロラや夕暮れの風景）と重ねます。
写真や個人のパスは写らず、実際の壁紙・設定ファイル・モニターにも触れません。

```powershell
dotnet run tools/StoreScreenshots/make-screenshots.cs -- artifacts/store-screenshots
```

`artifacts\store-screenshots\ja-JP` と `en-US` に、次の 5 枚が作られます。
日本語の登録情報には `ja-JP`、英語の登録情報には `en-US` の画像を、番号の順に上げます。

| ファイル | 写っているもの |
| --- | --- |
| `01-settings.png` | 設定画面（1 枚表示のフォルダと表示方法、プレビュー） |
| `02-grid.png` | 4 分割表示の壁紙と、マスごとのフォルダの設定 |
| `03-tray-menu.png` | 16 分割表示の壁紙と、タスクトレイのメニュー（「この画像を表示しない」を開いたところ） |
| `04-schedule.png` | 切り替え間隔、全画面表示・バッテリーのときに切り替えない設定、画像の選び方 |
| `05-options.png` | 表示しない画像、Windows の起動時の自動開始、表示言語、設定の書き出し・読み込み |

- 画面はダークテーマで撮ります。ライトテーマにするときは、最後に `--theme light` を付けます。
- トレイのメニューを描くために、画面の隅にメニューが一瞬だけ開きます。
- 設定画面の見た目や文言を変えたときは、作り直してストアの画像も差し替えます。
