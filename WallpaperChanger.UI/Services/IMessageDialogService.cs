namespace WallpaperChanger.UI.Services;

/// <summary>
/// OK ボタンだけのメッセージのダイアログを表示する。
/// </summary>
public interface IMessageDialogService
{
    /// <summary>ダイアログを表示し、閉じるまで待つ。作業中のウィンドウがあれば、その手前に表示する。</summary>
    void ShowMessage(string title, string message);
}
