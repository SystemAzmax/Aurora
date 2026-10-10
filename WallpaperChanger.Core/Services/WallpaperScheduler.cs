using Microsoft.Extensions.Logging;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Services;

/// <summary>
/// PeriodicTimer を用いて壁紙を定期的に切り替える。
/// 間隔・一時停止状態は <see cref="ISettingsService"/> を正とし、変更を購読して追従する。
/// </summary>
public sealed partial class WallpaperScheduler : IWallpaperScheduler, IDisposable
{
    private readonly IWallpaperRotationService _rotationService;
    private readonly ISettingsService _settingsService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WallpaperScheduler> _logger;
    private readonly Lock _gate = new();

    private CancellationTokenSource? _stopCts;
    private CancellationTokenSource _resetCts = new();
    private Task? _loopTask;
    private TimeSpan _interval;
    private volatile bool _isPaused;
    private bool _disposed;

    public WallpaperScheduler(
        IWallpaperRotationService rotationService,
        ISettingsService settingsService,
        TimeProvider timeProvider,
        ILogger<WallpaperScheduler> logger)
    {
        _rotationService = rotationService ?? throw new ArgumentNullException(nameof(rotationService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _interval = settingsService.Current.Interval;
        _isPaused = settingsService.Current.IsPaused;
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    public event EventHandler? StateChanged;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _loopTask is not null;
            }
        }
    }

    public bool IsPaused => _isPaused;

    public TimeSpan Interval
    {
        get
        {
            lock (_gate)
            {
                return _interval;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_loopTask is not null)
            {
                return;
            }

            AppSettings settings = _settingsService.Current;
            _interval = settings.Interval;
            _isPaused = settings.IsPaused;
            _stopCts = new CancellationTokenSource();
            CancellationToken stopToken = _stopCts.Token;
            _loopTask = Task.Run(() => RunLoopAsync(stopToken), CancellationToken.None);
        }

        LogStarted(_interval, _isPaused);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? stopCts;
        Task? loopTask;
        lock (_gate)
        {
            stopCts = _stopCts;
            loopTask = _loopTask;
            _stopCts = null;
            _loopTask = null;
        }

        if (stopCts is null || loopTask is null)
        {
            return;
        }

        try
        {
            await stopCts.CancelAsync().ConfigureAwait(false);
            await loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopCts.IsCancellationRequested)
        {
            // 停止要求による正常なキャンセル
        }
        finally
        {
            stopCts.Dispose();
        }

        LogStopped();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task PauseAsync(CancellationToken cancellationToken = default) =>
        _settingsService.UpdateAsync(s => s with { IsPaused = true }, cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken = default) =>
        _settingsService.UpdateAsync(s => s with { IsPaused = false }, cancellationToken);

    public Task<WallpaperChangeResult> ChangeNowAsync(CancellationToken cancellationToken = default)
    {
        ResetTimer();
        return _rotationService.NextAsync(cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _settingsService.SettingsChanged -= OnSettingsChanged;
        await StopAsync().ConfigureAwait(false);
        _resetCts.Dispose();
    }

    /// <summary>
    /// DI コンテナの同期破棄（IHost.Dispose）用。内部の待機はすべて ConfigureAwait(false) のため、
    /// UI スレッドから呼ばれてもデッドロックしない。
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task RunLoopAsync(CancellationToken stopToken)
    {
        while (!stopToken.IsCancellationRequested)
        {
            CancellationTokenSource linkedCts;
            TimeSpan interval;
            lock (_gate)
            {
                interval = _interval;
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stopToken, _resetCts.Token);
            }

            using (linkedCts)
            using (var timer = new PeriodicTimer(interval, _timeProvider))
            {
                try
                {
                    while (await timer.WaitForNextTickAsync(linkedCts.Token).ConfigureAwait(false))
                    {
                        if (_isPaused)
                        {
                            continue;
                        }

                        await ChangeOnScheduleAsync(stopToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (!stopToken.IsCancellationRequested)
                {
                    // 間隔変更・再開・即時変更によるタイマーのリセット。新しい間隔で待ち直す。
                    LogTimerReset();
                }
            }
        }

        stopToken.ThrowIfCancellationRequested();
    }

    private async Task ChangeOnScheduleAsync(CancellationToken stopToken)
    {
        try
        {
            await _rotationService.NextAsync(cancellationToken: stopToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 定期実行の 1 回分の失敗でスケジューラ自体を止めない。失敗はログに記録して次回に再試行する。
            LogScheduledChangeFailed(ex);
        }
    }

    private void ResetTimer()
    {
        CancellationTokenSource previous;
        lock (_gate)
        {
            previous = _resetCts;
            _resetCts = new CancellationTokenSource();
        }

        // ループ側のリンク済みトークンへ伝播させる。リンク解除前の破棄を避けるため previous は Dispose しない。
        previous.Cancel();
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        bool intervalChanged;
        bool pauseChanged;
        lock (_gate)
        {
            intervalChanged = _interval != settings.Interval;
            pauseChanged = _isPaused != settings.IsPaused;
            _interval = settings.Interval;
            _isPaused = settings.IsPaused;
        }

        if (!intervalChanged && !pauseChanged)
        {
            return;
        }

        // 間隔変更時と再開時は、そこから 1 間隔待つ
        if (intervalChanged || !settings.IsPaused)
        {
            ResetTimer();
        }

        LogStateChanged(settings.Interval, settings.IsPaused);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "自動切り替えを開始しました (間隔: {Interval}, 一時停止: {IsPaused})")]
    private partial void LogStarted(TimeSpan interval, bool isPaused);

    [LoggerMessage(Level = LogLevel.Information, Message = "自動切り替えを停止しました")]
    private partial void LogStopped();

    [LoggerMessage(Level = LogLevel.Information, Message = "自動切り替えの状態を変更しました (間隔: {Interval}, 一時停止: {IsPaused})")]
    private partial void LogStateChanged(TimeSpan interval, bool isPaused);

    [LoggerMessage(Level = LogLevel.Debug, Message = "タイマーをリセットしました")]
    private partial void LogTimerReset();

    [LoggerMessage(Level = LogLevel.Error, Message = "定期実行による壁紙の変更に失敗しました")]
    private partial void LogScheduledChangeFailed(Exception exception);
}
