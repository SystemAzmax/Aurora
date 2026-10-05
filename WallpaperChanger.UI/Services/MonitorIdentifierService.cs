using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using WallpaperChanger.Core.Models;
using WallpaperChanger.UI.Interop;
using WallpaperChanger.UI.ViewModels;
using WallpaperChanger.UI.Views;

namespace WallpaperChanger.UI.Services;

/// <summary>
/// 各モニターの左上に番号ウィンドウを表示する。
/// 設定画面と同じ DI スコープで生成し、設定画面を閉じたとき（Dispose）に表示中の番号も閉じる。
/// </summary>
internal sealed partial class MonitorIdentifierService(TimeProvider timeProvider, ILogger<MonitorIdentifierService> logger)
    : IMonitorIdentifierService, IDisposable
{
    /// <summary>モニターの端からの距離（物理ピクセル）。</summary>
    private const int MarginFromEdge = 48;

    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<MonitorIdentifierService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly List<Window> _windows = [];
    private CancellationTokenSource? _displayCts;

    public TimeSpan DisplayDuration { get; } = TimeSpan.FromSeconds(3);

    public async Task ShowAsync(IReadOnlyList<MonitorIdentification> monitors, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        // 表示中なら閉じてから表示し直す（前回の待機はキャンセルされる）
        CloseAll();

        var displayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = displayCts.Token;
        _displayCts = displayCts;

        foreach (MonitorIdentification monitor in monitors)
        {
            _windows.Add(ShowNumberWindow(monitor));
        }

        try
        {
            await Task.Delay(DisplayDuration, _timeProvider, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 再表示・設定画面を閉じたことによるキャンセル。番号ウィンドウは CloseAll で閉じ済み。
            return;
        }

        CloseAll();
    }

    public void Dispose() => CloseAll();

    private MonitorNumberWindow ShowNumberWindow(MonitorIdentification identification)
    {
        MonitorInfo monitor = identification.Monitor;
        var window = new MonitorNumberWindow();
        window.InitializeComponent();
        window.DataContext = new MonitorNumberViewModel(
            identification.Number, monitor.DisplayName, monitor.ResolutionText, identification.IsSelected);

        // 表示前（ウィンドウハンドル作成直後）に、物理ピクセル座標で対象モニターへ移動する。
        // WPF の Left/Top は DIP 単位で、モニターごとに DPI が異なると位置がずれるため使わない。
        window.SourceInitialized += (_, _) => MoveToMonitor(window, monitor.Bounds);
        window.Show();
        return window;
    }

    private void MoveToMonitor(Window window, MonitorBounds bounds)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        bool moved = NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HwndTopmost,
            bounds.Left + MarginFromEdge,
            bounds.Top + MarginFromEdge,
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);

        if (!moved)
        {
            // 番号が画面外に出るだけで機能への影響は小さいため、記録して処理を続ける
            LogMoveFailed(new Win32Exception(Marshal.GetLastWin32Error()), bounds.Left, bounds.Top);
        }
    }

    private void CloseAll()
    {
        _displayCts?.Cancel();
        _displayCts?.Dispose();
        _displayCts = null;

        foreach (Window window in _windows)
        {
            window.Close();
        }

        _windows.Clear();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "モニター番号の表示位置を設定できませんでした ({Left}, {Top})")]
    private partial void LogMoveFailed(Exception exception, int left, int top);
}
