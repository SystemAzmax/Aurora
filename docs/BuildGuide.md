# ビルド方法

ビルド・実行・テストのコマンドと、exe 版の発行は [README.md](../README.md) の「ビルド・実行・テスト」「配布 (Self-contained)」を参照してください。
ここには、リリースのときに行う作業をまとめます。

## バージョンの上げ方

「このアプリについて」に表示するバージョンは、版によって読み取る場所が違います。
リリースのときは、次の 2 か所を同じバージョンにそろえてください。

| | 読み取る場所 | 書き方の例 |
| --- | --- | --- |
| ストア版 | パッケージのプロジェクトの `Package.appxmanifest` の `Identity` の `Version`（パッケージのプロジェクトは、ストア版のパッケージを作るときに追加する） | `1.1.0.0` |
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
   （`dotnet publish WallpaperChanger.UI -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -o <出力先>`）。
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
