namespace WallpaperChanger.UI.ViewModels;

/// <summary>
/// タスクトレイのバルーン通知の表示要求。
/// </summary>
public sealed class TrayNotificationEventArgs(string title, string message, bool isError) : EventArgs
{
    public string Title { get; } = title;

    public string Message { get; } = message;

    public bool IsError { get; } = isError;
}
