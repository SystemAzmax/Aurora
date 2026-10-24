namespace WallpaperChanger.UI.Services;

/// <summary>
/// フォルダをエクスプローラーで開く。
/// </summary>
public interface IFolderLauncher
{
    /// <summary>フォルダを開く。存在しない場合は作成してから開く。</summary>
    void OpenFolder(string path);

    /// <summary>ファイルのあるフォルダを、そのファイルを選択した状態で開く。ファイルが無い場合は <see cref="System.IO.FileNotFoundException"/>。</summary>
    void ShowFileInFolder(string filePath);
}
