namespace WallpaperChanger.UI.Services;

/// <summary>
/// フォルダ選択ダイアログを表示する。
/// </summary>
public interface IFolderPickerService
{
    /// <summary>フォルダを選択させる。キャンセル時は空のリストを返す。</summary>
    IReadOnlyList<string> PickFolders(string title);
}
