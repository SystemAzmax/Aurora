using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using WallpaperChanger.UI;

namespace WallpaperChanger.Tests.Fakes;

/// <summary>
/// 本番と同じ App（App.xaml のテーマ・スタイル・トレイ定義）を専用の STA スレッドで 1 度だけ起動し、
/// テストの処理をその UI スレッドで実行する。WPF の Application はプロセスに 1 つしか作れないため共有する。
/// </summary>
internal static class WpfTestApplication
{
    private static readonly Lazy<Dispatcher> UiDispatcher = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    public static Task RunAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return UiDispatcher.Value.InvokeAsync(action).Task.Unwrap();
    }

    /// <summary>UI スレッドで、レイアウト・描画を含む保留中の処理をすべて実行させる。</summary>
    public static async Task FlushAsync() =>
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

    private static Dispatcher Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                ready.SetResult(app.Dispatcher);
            }
            catch (Exception ex)
            {
                ready.SetException(ex);
                return;
            }

            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF test UI thread",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }
}

/// <summary>
/// WPF のバインディングエラー（PresentationTraceSources.DataBindingSource の Warning 以上）を収集する。
/// </summary>
internal sealed class BindingErrorCollector : TraceListener
{
    private readonly List<string> _errors = [];
    private readonly Lock _gate = new();

    private BindingErrorCollector()
    {
    }

    public IReadOnlyList<string> Errors
    {
        get
        {
            lock (_gate)
            {
                return [.. _errors];
            }
        }
    }

    public static BindingErrorCollector Start()
    {
        var collector = new BindingErrorCollector();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        PresentationTraceSources.DataBindingSource.Listeners.Add(collector);
        return collector;
    }

    public override void Write(string? message)
    {
    }

    public override void WriteLine(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (_gate)
        {
            _errors.Add(message);
        }
    }

    protected override void Dispose(bool disposing)
    {
        PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
        base.Dispose(disposing);
    }
}
