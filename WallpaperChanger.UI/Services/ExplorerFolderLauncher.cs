using System.Diagnostics;
using System.IO;

namespace WallpaperChanger.UI.Services;

internal sealed class ExplorerFolderLauncher : IFolderLauncher
{
    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Directory.CreateDirectory(path);
        using Process? process = Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }
}
