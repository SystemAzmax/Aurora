using System.Windows.Threading;

namespace WallpaperChanger.UI.Services;

internal sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    private readonly Dispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        // UI スレッドが終了処理に入った後は、依頼しても実行されない。
        // その時点でウィンドウは WPF が閉じており、トレイアイコンは DI コンテナの破棄時に UI スレッドで破棄される。
        if (_dispatcher.HasShutdownStarted)
        {
            return Task.CompletedTask;
        }

        return InvokeCoreAsync(action);
    }

    private async Task InvokeCoreAsync(Action action)
    {
        try
        {
            await _dispatcher.InvokeAsync(action);
        }
        catch (TaskCanceledException) when (_dispatcher.HasShutdownStarted)
        {
            // 依頼した直後に UI スレッドが終了し、処理が取り消された。更新する画面はもう無いため完了として扱う。
        }
    }
}
