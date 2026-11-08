namespace WallpaperChanger.UI.Services;

/// <summary>
/// メッセージや確認のダイアログを表示する。
/// </summary>
public interface IMessageDialogService
{
    /// <summary>OK ボタンだけのダイアログを表示し、閉じるまで待つ。作業中のウィンドウがあれば、その手前に表示する。</summary>
    void ShowMessage(string title, string message);

    /// <summary>
    /// OK とキャンセルのボタンがある確認のダイアログを表示し、閉じるまで待つ。作業中のウィンドウがあれば、その手前に表示する。
    /// 元に戻せない操作の確認に使うため、はじめはキャンセルを選んだ状態にする。
    /// </summary>
    /// <returns>OK を押した場合は true。</returns>
    bool Confirm(string title, string message);
}
