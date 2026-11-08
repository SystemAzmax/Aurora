using System.Windows;

namespace WallpaperChanger.UI.Services;

/// <summary>
/// <see cref="MessageBox"/> でメッセージを表示する。設定画面などの作業中のウィンドウを持ち主にして、その手前に表示する。
/// </summary>
internal sealed class MessageDialogService : IMessageDialogService
{
    public void ShowMessage(string title, string message) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.OK);

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;

    private static MessageBoxResult Show(
        string message, string title, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
    {
        Window? owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        return owner is null
            ? MessageBox.Show(message, title, button, icon, defaultResult)
            : MessageBox.Show(owner, message, title, button, icon, defaultResult);
    }
}
