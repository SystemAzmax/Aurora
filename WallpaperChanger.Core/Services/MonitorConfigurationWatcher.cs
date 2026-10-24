using Microsoft.Extensions.Logging;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Services;

/// <summary>
/// ディスプレイ構成の変更通知を受け、落ち着いた時点（<see cref="SettleDelay"/> 後）で 1 回だけ構成を比較して壁紙を合わせる。
/// 接続先を変えたモニターには、壁紙を表示する前に前の設定を引き継ぐ（開始時にも、電源を切っている間の変更に備えて引き継ぐ）。
/// 直前の構成を保持するため、アプリケーション内で 1 インスタンスとして登録する。
/// </summary>
public sealed partial class MonitorConfigurationWatcher : IMonitorConfigurationWatcher, IDisposable
{
    /// <summary>
    /// 最後の通知からこの時間だけ通知が無ければ処理する。モニターの抜き差しでは通知が短時間に何度も届くため。
    /// </summary>
    public static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);

    private readonly IDisplayChangeNotifier _notifier;
    private readonly IMonitorService _monitorService;
    private readonly IWallpaperRotationService _rotationService;
    private readonly IMonitorSettingsInheritor _settingsInheritor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MonitorConfigurationWatcher> _logger;
    private readonly Lock _gate = new();

    private Dictionary<string, MonitorInfo> _snapshot = new(StringComparer.OrdinalIgnoreCase);
    private ITimer? _settleTimer;
    private Task _processing = Task.CompletedTask;
    private bool _isWatching;

    public MonitorConfigurationWatcher(
        IDisplayChangeNotifier notifier,
        IMonitorService monitorService,
        IWallpaperRotationService rotationService,
        IMonitorSettingsInheritor settingsInheritor,
        TimeProvider timeProvider,
        ILogger<MonitorConfigurationWatcher> logger)
    {
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _rotationService = rotationService ?? throw new ArgumentNullException(nameof(rotationService));
        _settingsInheritor = settingsInheritor ?? throw new ArgumentNullException(nameof(settingsInheritor));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event EventHandler<MonitorConfigurationChangedEventArgs>? ConfigurationChanged;

    /// <summary>処理中（または最後に処理した）構成更新。テストで完了を待つために使う。</summary>
    internal Task ProcessingTask
    {
        get
        {
            lock (_gate)
            {
                return _processing;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MonitorInfo> monitors = await _monitorService.GetMonitorsAsync(cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            if (_isWatching)
            {
                return;
            }

            _snapshot = ToMap(monitors);
            _isWatching = true;
        }

        _notifier.DisplaysChanged += OnDisplaysChanged;
        LogStarted(monitors.Count);

        // アプリを起動していない間に接続先を変えたモニターにも、前の設定を引き継ぐ
        await InheritSettingsAsync(monitors).ConfigureAwait(false);
    }

    public Task StopAsync()
    {
        _notifier.DisplaysChanged -= OnDisplaysChanged;

        lock (_gate)
        {
            _isWatching = false;
            _settleTimer?.Dispose();
            _settleTimer = null;
            return _processing;
        }
    }

    public void Dispose()
    {
        _notifier.DisplaysChanged -= OnDisplaysChanged;
        lock (_gate)
        {
            _isWatching = false;
            _settleTimer?.Dispose();
            _settleTimer = null;
        }
    }

    private void OnDisplaysChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (!_isWatching)
            {
                return;
            }

            // 通知が続く間は待ち直し、最後の通知から SettleDelay 後に 1 回だけ処理する
            _settleTimer?.Dispose();
            _settleTimer = _timeProvider.CreateTimer(_ => OnSettled(), null, SettleDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnSettled()
    {
        lock (_gate)
        {
            if (!_isWatching)
            {
                return;
            }

            // 前回の処理が終わってから順に処理する
            _processing = _processing.ContinueWith(_ => ProcessChangeAsync(), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task ProcessChangeAsync()
    {
        IReadOnlyList<MonitorInfo> current;
        try
        {
            current = await _monitorService.GetMonitorsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // OS からの通知を起点とするバックグラウンド処理のため、失敗はログに残して次の通知を待つ
            LogProcessingFailed(ex);
            return;
        }

        Dictionary<string, MonitorInfo> previous = _snapshot;
        _snapshot = ToMap(current);

        List<MonitorInfo> added = [.. current.Where(m => !previous.ContainsKey(m.Id))];
        List<MonitorInfo> removed = [.. previous.Values.Where(m => !_snapshot.ContainsKey(m.Id))];
        List<MonitorInfo> resized = [.. current.Where(m =>
            previous.TryGetValue(m.Id, out MonitorInfo? old) && (old.Width != m.Width || old.Height != m.Height))];
        bool moved = current.Any(m => previous.TryGetValue(m.Id, out MonitorInfo? old) && old.Bounds != m.Bounds);

        if (added.Count == 0 && removed.Count == 0 && resized.Count == 0 && !moved)
        {
            LogNoChange();
            return;
        }

        LogChanged(added.Count, removed.Count, resized.Count, moved);

        // 接続先を変えたモニターは新しく接続されたモニターとして通知されるため、壁紙を表示する前に前の設定を引き継ぐ
        IReadOnlyList<MonitorInfo> inherited = added.Count > 0 ? await InheritSettingsAsync(current).ConfigureAwait(false) : [];
        ConfigurationChanged?.Invoke(this, new MonitorConfigurationChangedEventArgs(added, removed, resized, current, inherited));

        foreach (MonitorInfo monitor in added)
        {
            await UpdateWallpaperAsync(monitor, () => _rotationService.NextAsync(monitor.Id)).ConfigureAwait(false);
        }

        foreach (MonitorInfo monitor in resized)
        {
            await UpdateWallpaperAsync(monitor, () => _rotationService.ReapplyAsync(monitor.Id)).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<MonitorInfo>> InheritSettingsAsync(IReadOnlyList<MonitorInfo> monitors)
    {
        try
        {
            return await _settingsInheritor.InheritAsync(monitors).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 引き継げなくても、壁紙の表示と監視は続ける（設定画面からコピーできる）
            LogInheritFailed(ex);
            return [];
        }
    }

    private async Task UpdateWallpaperAsync(MonitorInfo monitor, Func<Task> update)
    {
        try
        {
            await update().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 1 台の失敗で他のモニターの更新を止めない
            LogUpdateFailed(ex, monitor.DisplayName);
        }
    }

    private static Dictionary<string, MonitorInfo> ToMap(IReadOnlyList<MonitorInfo> monitors) =>
        monitors.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);

    [LoggerMessage(Level = LogLevel.Information, Message = "モニター構成の監視を開始しました ({Count} 台)")]
    private partial void LogStarted(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ディスプレイ設定の変更通知を受けましたが、モニター構成は変わっていません")]
    private partial void LogNoChange();

    [LoggerMessage(Level = LogLevel.Information, Message = "モニター構成が変わりました (接続: {Added}, 切断: {Removed}, 解像度変更: {Resized}, 配置変更: {Moved})")]
    private partial void LogChanged(int added, int removed, int resized, bool moved);

    [LoggerMessage(Level = LogLevel.Error, Message = "接続先が変わったモニターに設定を引き継げませんでした")]
    private partial void LogInheritFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "モニター構成の変更を処理できませんでした")]
    private partial void LogProcessingFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "モニター構成の変更に合わせた壁紙の更新に失敗しました: {Monitor}")]
    private partial void LogUpdateFailed(Exception exception, string monitor);
}
