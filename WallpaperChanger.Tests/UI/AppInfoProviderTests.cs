using System.IO;
using System.Reflection;
using WallpaperChanger.UI.Services;

namespace WallpaperChanger.Tests.UI;

/// <summary>
/// <see cref="AppInfoProvider"/> のテスト。
/// テストはパッケージなしで実行されるため、exe 版と同じ動きになることを検証します（StoreReleaseKit のひな形のテストをもとにしている）。
/// </summary>
public class AppInfoProviderTests
{
    /// <summary>
    /// パッケージなしで実行した場合は、例外にならず、
    /// アセンブリのバージョンと exe 版であることを返すことを検証します。
    /// </summary>
    [Fact]
    public void GetVersion_パッケージなしで実行した場合_アセンブリのバージョンを返す()
    {
        // Act
        var (version, isPackaged) = AppInfoProvider.GetVersion();

        // Assert
        Assert.False(isPackaged);
        Assert.Equal(typeof(AppInfoProvider).Assembly.GetName().Version, version);
    }

    /// <summary>
    /// 本文に、表示言語（テストでは日本語）のアプリ名と、csproj で設定した著作権表記が入ることを検証します。
    /// </summary>
    [Fact]
    public void CreateAboutMessage_パッケージなしで実行した場合_exe版としてアプリ名と著作権表記を含む()
    {
        // Arrange
        var copyright = typeof(AppInfoProvider).Assembly
            .GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

        // Act
        var message = AppInfoProvider.CreateAboutMessage();

        // Assert
        Assert.Equal("© 2026 SystemAzmax", copyright);
        Assert.StartsWith("オーロラ壁紙チェンジャー\nバージョン 1.0.0", message, StringComparison.Ordinal);
        Assert.Contains("（exe 版）", message, StringComparison.Ordinal);
        Assert.EndsWith($"\n\n{copyright}", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// ライセンス表示のファイルが、アプリと同じフォルダーに出力されていることを検証します。
    /// </summary>
    [Fact]
    public void ThirdPartyNoticesPath_ビルド出力_ライセンス表示のファイルがある()
    {
        // Act
        var path = AppInfoProvider.ThirdPartyNoticesPath;

        // Assert
        Assert.True(File.Exists(path), $"ファイルがありません: {path}");
        Assert.Contains(
            "オーロラ壁紙チェンジャー サードパーティ ソフトウェアのライセンス情報",
            File.ReadAllText(path),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// 使い方ガイドの URL が、表示言語（テストでは日本語）の、このリポジトリの既定のブランチのガイドを指すことを検証します。
    /// 配布済みのアプリもこの URL を開くため、ガイドのファイルの名前と場所を変えるとこのテストが失敗します。
    /// </summary>
    [Fact]
    public void UserGuideUrl_表示言語が日本語の場合_日本語版のガイドを指す()
    {
        // Act
        var url = AppInfoProvider.UserGuideUrl;

        // Assert
        Assert.Equal("https://github.com/SystemAzmax/Aurora/blob/master/docs/UserGuide_ja.md", url);
    }

    /// <summary>
    /// URL が指すガイドのファイルが、リポジトリの docs フォルダーにあることを検証します。
    /// </summary>
    /// <param name="fileName">ガイドのファイル名。</param>
    [Theory]
    [InlineData("UserGuide.md")]
    [InlineData("UserGuide_ja.md")]
    public void UserGuideUrl_リポジトリ_ガイドのファイルがある(string fileName)
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Assert
        Assert.True(
            File.Exists(Path.Combine(repositoryRoot, "docs", fileName)),
            $"docs/{fileName} がありません");
    }

    /// <summary>テストの出力フォルダーから上にたどって、リポジトリのフォルダー（Aurora.slnx のあるフォルダー）を探します。</summary>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aurora.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Aurora.slnx のあるフォルダーが見つかりません。");
    }
}
