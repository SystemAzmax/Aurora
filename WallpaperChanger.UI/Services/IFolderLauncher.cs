namespace WallpaperChanger.UI.Services;

/// <summary>
/// フォルダをエクスプローラーで開く。ファイルを既定のアプリで開く。
/// </summary>
public interface IFolderLauncher
{
    /// <summary>フォルダを開く。存在しない場合は作成してから開く。</summary>
    void OpenFolder(string path);

    /// <summary>ファイルのあるフォルダを、そのファイルを選択した状態で開く。ファイルが無い場合は <see cref="System.IO.FileNotFoundException"/>。</summary>
    void ShowFileInFolder(string filePath);

    /// <summary>ファイルを、種類に関連付けられた既定のアプリ（テキストファイルならメモ帳など）で開く。</summary>
    void OpenFile(string filePath);
}
