using System.IO;
using System.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WallpaperChanger.Core.Interfaces;

namespace WallpaperChanger.UI.Services;

/// <summary>
/// アプリケーションの起動・終了手順をまとめる。
/// 起動: 設定読み込み → 自動起動の登録パス更新 → トレイ表示（設定ファイルを読めなかった場合は通知）
///       → 自動切り替え・モニター構成の監視開始（フォルダ未登録なら設定画面を表示）
///       → 2 つ目に起動されたときの要求の受け付け開始（設定画面を前面に出す）
/// 終了: 要求の受け付け停止 → モニター構成の監視・自動切り替え停止 → 設定画面とトレイアイコンの破棄
/// </summary>
/// <remarks>
/// モニター構成の監視は、始められるまで間隔を空けて再試行し、その間も起動処理を進める。
/// サインイン直後の自動起動では、エクスプローラーの準備が間に合わず、モニターの取得が時間切れになることがあるため。
/// </remarks>
internal sealed partial class ApplicationHostedService(
    ISettingsService settingsService,
    IWallpaperScheduler scheduler,
    ITrayIconService trayIconService,
    ISettingsWindowService settingsWindowService,
    IStartupRegistrationService startupRegistration,
    IMonitorConfigurationWatcher monitorWatcher,
    IActivationRequestListener activationRequestListener,
    IUiDispatcher uiDispatcher,
    TimeProvider timeProvider,
    ILogger<ApplicationHostedService> logger) : IHostedService, IDisposable
{
    private const string AppName = "壁紙チェンジャー";

    /// <summary>モニター構成の監視を始められなかったときに、最初に再試行するまでの待ち時間。再試行のたびに倍にする。</summary>
    internal static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(10);

    /// <summary>再試行するまでの待ち時間の上限。</summary>
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    private readonly ISettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private readonly IWallpaperScheduler _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    private readonly ITrayIconService _trayIconService = trayIconService ?? throw new ArgumentNullException(nameof(trayIconService));
    private readonly ISettingsWindowService _settingsWindowService = settingsWindowService ?? throw new ArgumentNullException(nameof(settingsWindowService));
    private readonly IStartupRegistrationService _startupRegistration = startupRegistration ?? throw new ArgumentNullException(nameof(startupRegistration));
    private readonly IMonitorConfigurationWatcher _monitorWatcher = monitorWatcher ?? throw new ArgumentNullException(nameof(monitorWatcher));
    private readonly IActivationRequestListener _activationRequestListener = activationRequestListener ?? throw new ArgumentNullException(nameof(activationRequestListener));
    private readonly IUiDispatcher _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<ApplicationHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Lock _stopGate = new();
    private readonly CancellationTokenSource _stoppingCts = new();
    private Task _monitorWatcherStart = Task.CompletedTask;
    private Task? _stopTask;
    private volatile bool _isStopping;

    /// <summary>モニター構成の監視を始める処理（再試行を含む）。テストで完了を待つために使う。</summary>
    internal Task MonitorWatcherStart => _monitorWatcherStart;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        UpdateStartupRegistration();
        await _uiDispatcher.InvokeAsync(_trayIconService.Show).ConfigureAwait(false);

        // 既定値で起動した理由を伝える（フォルダ未登録として設定画面が開いても、設定が消えたと誤解されないように）
        if (_settingsService.LoadWarning is { } warning)
        {
            await _uiDispatcher.InvokeAsync(() => _trayIconService.ShowNotification(AppName, warning)).ConfigureAwait(false);
        }

        _scheduler.Start();

        // エクスプローラーの準備が間に合わない場合は始められるまで再試行するため、完了を待たずに起動処理を進める
        _monitorWatcherStart = StartMonitorWatcherAsync(_stoppingCts.Token);

        // 4 分割のマス専用フォルダも含めて判定する（現在のレイアウトで表示に使うフォルダがあるか）
        bool hasAnyFolder = _settingsService.Current.Monitors.Any(m => m.HasAnyFolder());
        if (!hasAnyFolder)
        {
            // 初回起動時など、壁紙フォルダが未登録なら設定画面を開く
            await _uiDispatcher.InvokeAsync(_settingsWindowService.Show).ConfigureAwait(false);
        }

        // 起動処理の途中に届いた要求も、ここで受け付ける（設定の読み込み前に設定画面を開かないように）
        _activationRequestListener.StartListening(OnActivationRequested);

        LogStarted();
    }

    /// <summary>
    /// 終了処理。トレイの「終了」とサインアウト時の両方から呼ばれうるため、何度呼ばれても 1 回だけ実行し、
    /// 2 回目以降は同じ処理の完了を待つ。
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_stopGate)
        {
            _isStopping = true;
            return _stopTask ??= StopCoreAsync();
        }
    }

    public void Dispose() => _stoppingCts.Dispose();

    private async Task StopCoreAsync()
    {
        _activationRequestListener.StopListening();

        // 監視を始められずに再試行している場合は、やめさせてから監視を止める
        await _stoppingCts.CancelAsync().ConfigureAwait(false);
        await _monitorWatcherStart.ConfigureAwait(false);
        await _monitorWatcher.StopAsync().ConfigureAwait(false);
        await _scheduler.StopAsync().ConfigureAwait(false);
        await _uiDispatcher.InvokeAsync(() =>
        {
            _settingsWindowService.Close();
            _trayIconService.Dispose();
        }).ConfigureAwait(false);

        LogStopped();
    }

    /// <summary>
    /// モニター構成の監視を始める。始められなければ、終了するまで間隔を空けて再試行する（待ち時間は倍にしていき、上限で止める）。
    /// 監視を始められなくても、壁紙の自動切り替えは続く（切り替えのたびにモニターを取得し直すため）。
    /// </summary>
    private async Task StartMonitorWatcherAsync(CancellationToken stoppingToken)
    {
        TimeSpan delay = FirstRetryDelay;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                // エクスプローラーが応答しない間の呼び出しは、キャンセルしても時間切れまで終わらないため、
                // 終了するときはその完了を待たずに抜ける（終了処理を待たせない）
                await _monitorWatcher.StartAsync(stoppingToken).WaitAsync(stoppingToken).ConfigureAwait(false);
                if (attempt > 1)
                {
                    LogMonitorWatcherStarted(attempt);
                }

                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogMonitorWatcherStartFailed(ex, attempt, delay);
            }

            try
            {
                await Task.Delay(delay, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
        }
    }

    /// <summary>2 つ目に起動されたときは、起動中のこちらの設定画面を前面に出す（トレイアイコンを探さなくて済むように）。</summary>
    private async void OnActivationRequested()
    {
        try
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                // 受け付けを止める前に届いた要求で、終了処理が閉じた後の設定画面を開き直さない
                if (!_isStopping)
                {
                    _settingsWindowService.Show();
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogActivationFailed(ex);
        }
    }

    /// <summary>アプリを移動・更新した後も自動起動が働くよう、登録済みのパスを現在の実行ファイルに合わせる。</summary>
    private void UpdateStartupRegistration()
    {
        try
        {
            _startupRegistration.UpdateRegisteredPathIfNeeded();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            // 自動起動の補助処理のため、失敗してもアプリの起動は続ける
            LogStartupRegistrationUpdateFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "壁紙チェンジャーを起動しました")]
    private partial void LogStarted();

    [LoggerMessage(Level = LogLevel.Information, Message = "壁紙チェンジャーを終了しました")]
    private partial void LogStopped();

    [LoggerMessage(Level = LogLevel.Warning, Message = "モニター構成の監視を始められませんでした（{Attempt} 回目）。{Delay} 後に再試行します")]
    private partial void LogMonitorWatcherStartFailed(Exception exception, int attempt, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "モニター構成の監視を始めました（{Attempt} 回目で成功）")]
    private partial void LogMonitorWatcherStarted(int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "自動起動の登録パスを更新できませんでした")]
    private partial void LogStartupRegistrationUpdateFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "2 つ目の起動に合わせて設定画面を表示できませんでした")]
    private partial void LogActivationFailed(Exception exception);
}
