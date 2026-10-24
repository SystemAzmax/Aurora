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
}
