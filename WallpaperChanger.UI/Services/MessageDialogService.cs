using System.Windows;

namespace WallpaperChanger.UI.Services;

/// <summary>
/// <see cref="MessageBox"/> でメッセージを表示する。設定画面などの作業中のウィンドウを持ち主にして、その手前に表示する。
/// </summary>
internal sealed class MessageDialogService : IMessageDialogService
{
    public void ShowMessage(string title, string message)
    {
        Window? owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (owner is null)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK);
        }
        else
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK);
        }
    }
}
