using WallpaperChanger.Tests.Fakes;
using WallpaperChanger.UI.Services;

namespace WallpaperChanger.Tests.UI;

public sealed class ExplorerFolderLauncherTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void 無いファイルの場所はエクスプローラーを開かずに例外にする()
    {
        string missing = Path.Combine(_temp.Path, "gone.jpg");

        var ex = Assert.Throws<FileNotFoundException>(() => new ExplorerFolderLauncher().ShowFileInFolder(missing));

        Assert.Equal(missing, ex.FileName);
        Assert.Contains(missing, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\notepad.exe")]
    [InlineData("file:///C:/Windows/System32/notepad.exe")]
    [InlineData("ms-settings:startupapps")]
    [InlineData("docs/UserGuide.md")]
    public void Webページ以外のURLはブラウザーで開かずに例外にする(string url)
    {
        var ex = Assert.Throws<ArgumentException>(() => new ExplorerFolderLauncher().OpenWebPage(url));

        Assert.Equal("url", ex.ParamName);
    }
}
