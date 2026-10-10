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
/// 終了: モニター構成の監視・自動切り替え停止 → 設定画面とトレイアイコンの破棄
/// </summary>
internal sealed partial class ApplicationHostedService(
    ISettingsService settingsService,
    IWallpaperScheduler scheduler,
    ITrayIconService trayIconService,
    ISettingsWindowService settingsWindowService,
    IStartupRegistrationService startupRegistration,
    IMonitorConfigurationWatcher monitorWatcher,
    IUiDispatcher uiDispatcher,
    ILogger<ApplicationHostedService> logger) : IHostedService
{
    private const string AppName = "壁紙チェンジャー";

    private readonly ISettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private readonly IWallpaperScheduler _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    private readonly ITrayIconService _trayIconService = trayIconService ?? throw new ArgumentNullException(nameof(trayIconService));
    private readonly ISettingsWindowService _settingsWindowService = settingsWindowService ?? throw new ArgumentNullException(nameof(settingsWindowService));
    private readonly IStartupRegistrationService _startupRegistration = startupRegistration ?? throw new ArgumentNullException(nameof(startupRegistration));
    private readonly IMonitorConfigurationWatcher _monitorWatcher = monitorWatcher ?? throw new ArgumentNullException(nameof(monitorWatcher));
    private readonly IUiDispatcher _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
    private readonly ILogger<ApplicationHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Lock _stopGate = new();
    private Task? _stopTask;

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
        await _monitorWatcher.StartAsync(cancellationToken).ConfigureAwait(false);

        // 4 分割のマス専用フォルダも含めて判定する（現在のレイアウトで表示に使うフォルダがあるか）
        bool hasAnyFolder = _settingsService.Current.Monitors.Any(m => m.HasAnyFolder());
        if (!hasAnyFolder)
        {
            // 初回起動時など、壁紙フォルダが未登録なら設定画面を開く
            await _uiDispatcher.InvokeAsync(_settingsWindowService.Show).ConfigureAwait(false);
        }

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
            return _stopTask ??= StopCoreAsync();
        }
    }

    private async Task StopCoreAsync()
    {
        await _monitorWatcher.StopAsync().ConfigureAwait(false);
        await _scheduler.StopAsync().ConfigureAwait(false);
        await _uiDispatcher.InvokeAsync(() =>
        {
            _settingsWindowService.Close();
            _trayIconService.Dispose();
        }).ConfigureAwait(false);

        LogStopped();
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "自動起動の登録パスを更新できませんでした")]
    private partial void LogStartupRegistrationUpdateFailed(Exception exception);
}
