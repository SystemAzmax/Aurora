namespace WallpaperChanger.UI.Services;

/// <summary>
/// UI スレッドへの処理の受け渡しを抽象化する（ViewModel を WPF の Dispatcher から切り離してテスト可能にする）。
/// </summary>
public interface IUiDispatcher
{
    /// <summary>UI スレッドで処理を実行する。既に UI スレッド上なら即時に実行する。</summary>
    Task InvokeAsync(Action action);
}
