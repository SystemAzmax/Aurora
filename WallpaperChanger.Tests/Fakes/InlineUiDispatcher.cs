using WallpaperChanger.UI.Services;

namespace WallpaperChanger.Tests.Fakes;

/// <summary>
/// 呼び出し元スレッドでそのまま実行する IUiDispatcher。
/// </summary>
internal sealed class InlineUiDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
