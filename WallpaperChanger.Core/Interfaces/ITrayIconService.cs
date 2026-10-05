namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// タスクトレイアイコンの表示を管理する。
/// </summary>
public interface ITrayIconService : IDisposable
{
    /// <summary>タスクトレイにアイコンを表示する。UI スレッドから呼び出すこと。</summary>
    void Show();

    /// <summary>バルーン通知を表示する。</summary>
    void ShowNotification(string title, string message);
}
