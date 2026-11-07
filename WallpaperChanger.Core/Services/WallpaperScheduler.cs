using Microsoft.Extensions.Logging;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Services;

/// <summary>
/// PeriodicTimer を用いて壁紙を定期的に切り替える。
/// 間隔・一時停止状態は <see cref="ISettingsService"/> を正とし、変更を購読して追従する。
/// 切り替えの時刻に <see cref="SkipConditions"/>（全画面アプリの実行中・バッテリー駆動中）に当てはまれば、
/// 当てはまらなくなるまで待ってから切り替える。
/// </summary>
public sealed partial class WallpaperScheduler : IWallpaperScheduler, IDisposable
{
    /// <summary>切り替えを待っている間に、条件に当てはまらなくなったかを調べる間隔。</summary>
    internal static readonly TimeSpan HoldCheckInterval = TimeSpan.FromMinutes(1);

    private readonly IWallpaperRotationService _rotationService;
    private readonly ISettingsService _settingsService;
    private readonly ISystemStateProvider _systemState;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WallpaperScheduler> _logger;
    private readonly Lock _gate = new();

    private CancellationTokenSource? _stopCts;
    private CancellationTokenSource _resetCts = new();
    private Task? _loopTask;
    private TimeSpan _interval;
    private volatile bool _isPaused;
    private volatile AutoChangeHold _hold;
    private bool _disposed;

    public WallpaperScheduler(
        IWallpaperRotationService rotationService,
        ISettingsService settingsService,
        ISystemStateProvider systemState,
        TimeProvider timeProvider,
        ILogger<WallpaperScheduler> logger)
    {
        _rotationService = rotationService ?? throw new ArgumentNullException(nameof(rotationService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _systemState = systemState ?? throw new ArgumentNullException(nameof(systemState));
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

    public AutoChangeHold Hold => _hold;

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

    public Task<WallpaperChangeResult> RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        ResetTimer();
        return _rotationService.RefreshAsync(cancellationToken: cancellationToken);
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

                        HoldResult hold = await WaitWhileHeldAsync(linkedCts.Token).ConfigureAwait(false);
                        if (hold == HoldResult.Paused)
                        {
                            continue;
                        }

                        await ChangeOnScheduleAsync(stopToken).ConfigureAwait(false);

                        if (hold == HoldResult.Waited)
                        {
                            // 待っている間に過ぎた切り替えの時刻で続けて切り替えないよう、ここから 1 間隔待つ
                            ResetTimer();
                        }
                    }
                }
                catch (OperationCanceledException) when (!stopToken.IsCancellationRequested)
                {
                    // 間隔変更・再開・即時変更によるタイマーのリセット。新しい間隔で待ち直す。
                    // 切り替えを待っている間なら、待つのもやめる（即時変更で切り替えた後なので、改めて 1 間隔待つ）。
                    LogTimerReset();
                }
                finally
                {
                    SetHold(AutoChangeHold.None);
                }
            }
        }

        stopToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// <see cref="SkipConditions"/> に当てはまる間、<see cref="HoldCheckInterval"/> ごとに調べ直して待つ。
    /// 待っている間の <see cref="Hold"/> は待っている理由になる。
    /// </summary>
    private async Task<HoldResult> WaitWhileHeldAsync(CancellationToken cancellationToken)
    {
        AutoChangeHold hold = GetHold();
        if (hold == AutoChangeHold.None)
        {
            return HoldResult.NotHeld;
        }

        LogHeld(hold);
        while (hold != AutoChangeHold.None)
        {
            SetHold(hold);
            await Task.Delay(HoldCheckInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
            if (_isPaused)
            {
                // 一時停止したら待つのをやめる。再開したときは、そこから 1 間隔待つ
                SetHold(AutoChangeHold.None);
                return HoldResult.Paused;
            }

            hold = GetHold();
        }

        SetHold(AutoChangeHold.None);
        LogHoldReleased();
        return HoldResult.Waited;
    }

    private AutoChangeHold GetHold()
    {
        SkipConditions conditions = _settingsService.Current.SkipConditions;
        if (conditions.WhenFullScreen && _systemState.IsFullScreenAppRunning())
        {
            return AutoChangeHold.FullScreen;
        }

        if (conditions.OnBattery && _systemState.IsOnBatteryPower())
        {
            return AutoChangeHold.OnBattery;
        }

        return AutoChangeHold.None;
    }

    private void SetHold(AutoChangeHold hold)
    {
        if (_hold == hold)
        {
            return;
        }

        _hold = hold;
        StateChanged?.Invoke(this, EventArgs.Empty);
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

        // ループ側のリンク済みトークンへ伝播させる。
        // previous にリンクするトークンは差し替えと同じロックの内側でしか作られないため、差し替え後に新たにリンクされることはなく、
        // Cancel が終われば登録済みの通知も実行済みなので、ここで破棄してよい（リンク側の登録解除は破棄後でも安全）。
        previous.Cancel();
        previous.Dispose();
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

    [LoggerMessage(Level = LogLevel.Information, Message = "自動切り替えの条件に当てはまるため、切り替えを待ちます (理由: {Hold})")]
    private partial void LogHeld(AutoChangeHold hold);

    [LoggerMessage(Level = LogLevel.Information, Message = "自動切り替えの条件に当てはまらなくなったため、待っていた切り替えをします")]
    private partial void LogHoldReleased();

    /// <summary>切り替えの前に、条件に当てはまって待ったかどうか。</summary>
    private enum HoldResult
    {
        /// <summary>条件に当てはまらず、待たなかった。</summary>
        NotHeld,

        /// <summary>条件に当てはまって待ち、当てはまらなくなった。</summary>
        Waited,

        /// <summary>待っている間に一時停止した（切り替えない）。</summary>
        Paused,
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "定期実行による壁紙の変更に失敗しました")]
    private partial void LogScheduledChangeFailed(Exception exception);
}
