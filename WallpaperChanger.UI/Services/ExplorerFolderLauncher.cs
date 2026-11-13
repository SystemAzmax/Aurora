using System.Diagnostics;
using System.IO;
using WallpaperChanger.UI.Resources;

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
            throw new FileNotFoundException(Localizer.Format(Strings.ImageNotFoundFormat, filePath), filePath);
        }

        // アプリのフォルダなどに置かれた同名の実行ファイルを起動しないよう、Windows フォルダのものを使う
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        using Process? process = Process.Start(explorer, $"/select,\"{filePath}\"");
    }

    public void OpenFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using Process? process = Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
    }

    public void OpenWebPage(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        // シェルに渡すと、URL でない文字列はファイルやプログラムとして開かれるため、Web ページの URL だけを受け付ける
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException($"Web ページの URL ではありません: {url}", nameof(url));
        }

        using Process? process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
