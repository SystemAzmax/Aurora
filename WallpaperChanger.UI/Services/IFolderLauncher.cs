namespace WallpaperChanger.UI.Services;

/// <summary>
/// フォルダをエクスプローラーで開く。
/// </summary>
public interface IFolderLauncher
{
    /// <summary>フォルダを開く。存在しない場合は作成してから開く。</summary>
    void OpenFolder(string path);
}
