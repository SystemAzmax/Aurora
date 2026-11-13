namespace WallpaperChanger.UI.Services;

/// <summary>
/// フォルダをエクスプローラーで開く。ファイルを既定のアプリで、Web ページを既定のブラウザーで開く。
/// </summary>
public interface IFolderLauncher
{
    /// <summary>Web ページを既定のブラウザーで開く。http・https 以外の URL は <see cref="ArgumentException"/>。</summary>
    void OpenWebPage(string url);

    /// <summary>フォルダを開く。存在しない場合は作成してから開く。</summary>
    void OpenFolder(string path);

    /// <summary>ファイルのあるフォルダを、そのファイルを選択した状態で開く。ファイルが無い場合は <see cref="System.IO.FileNotFoundException"/>。</summary>
    void ShowFileInFolder(string filePath);

    /// <summary>ファイルを、種類に関連付けられた既定のアプリ（テキストファイルならメモ帳など）で開く。</summary>
    void OpenFile(string filePath);
}
