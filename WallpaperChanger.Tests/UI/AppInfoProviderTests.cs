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
}
