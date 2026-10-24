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

    public void ShowFileInFolder(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // 無いファイルを指定すると、エクスプローラーは何も言わずに別のフォルダ（ドキュメントなど）を開くため、先に確かめる
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"画像が見つかりません: {filePath}", filePath);
        }

        // アプリのフォルダなどに置かれた同名の実行ファイルを起動しないよう、Windows フォルダのものを使う
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        using Process? process = Process.Start(explorer, $"/select,\"{filePath}\"");
    }
}
