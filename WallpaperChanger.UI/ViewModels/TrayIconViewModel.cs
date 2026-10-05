using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.UI.Services;

namespace WallpaperChanger.UI.ViewModels;

/// <summary>
/// タスクトレイアイコンとその右クリックメニューの ViewModel。
/// </summary>
public sealed partial class TrayIconViewModel : ObservableObject, IDisposable
{
    private const string AppName = "壁紙チェンジャー";

    private readonly IWallpaperRotationService _rotationService;
    private readonly IWallpaperScheduler _scheduler;
    private readonly ISettingsWindowService _settingsWindowService;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<TrayIconViewModel> _logger;

    public TrayIconViewModel(
        IWallpaperRotationService rotationService,
        IWallpaperScheduler scheduler,
        ISettingsWindowService settingsWindowService,
        IHostApplicationLifetime applicationLifetime,
        IUiDispatcher uiDispatcher,
        ILogger<TrayIconViewModel> logger)
    {
        _rotationService = rotationService ?? throw new ArgumentNullException(nameof(rotationService));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _settingsWindowService = settingsWindowService ?? throw new ArgumentNullException(nameof(settingsWindowService));
        _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        IsPaused = _scheduler.IsPaused;
        _scheduler.StateChanged += OnSchedulerStateChanged;
    }

    /// <summary>バルーン通知の表示要求。</summary>
    public event EventHandler<TrayNotificationEventArgs>? NotificationRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseMenuHeader))]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    public partial bool IsPaused { get; set; }

    public string PauseMenuHeader => IsPaused ? "再開" : "一時停止";

    public string ToolTipText => IsPaused
        ? $"{AppName}（一時停止中）"
        : $"{AppName}（{IntervalUnitOption.Format(_scheduler.Interval)}ごとに切り替え）";

    public void Dispose() => _scheduler.StateChanged -= OnSchedulerStateChanged;

    [RelayCommand]
    private Task NextWallpaperAsync() =>
        ExecuteSafelyAsync("次の壁紙への切り替え", () => _scheduler.ChangeNowAsync());

    [RelayCommand]
    private Task PreviousWallpaperAsync() =>
        ExecuteSafelyAsync("前の壁紙への切り替え", () => _rotationService.PreviousAsync());

    [RelayCommand]
    private Task TogglePauseAsync() =>
        ExecuteSafelyAsync(
            IsPaused ? "自動切り替えの再開" : "自動切り替えの一時停止",
            () => IsPaused ? _scheduler.ResumeAsync() : _scheduler.PauseAsync());

    [RelayCommand]
    private void ShowSettings() => _settingsWindowService.Show();

    [RelayCommand]
    private void Exit() => _applicationLifetime.StopApplication();

    private async Task ExecuteSafelyAsync(string operation, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            // メニュー操作の失敗でアプリを終了させず、ログとバルーン通知でユーザーに伝える
            LogOperationFailed(ex, operation);
            NotificationRequested?.Invoke(
                this,
                new TrayNotificationEventArgs(AppName, $"{operation}に失敗しました。{ErrorMessages.Describe(ex)}", isError: true));
        }
    }

    private async void OnSchedulerStateChanged(object? sender, EventArgs e) =>
        await _uiDispatcher.InvokeAsync(() =>
        {
            IsPaused = _scheduler.IsPaused;
            OnPropertyChanged(nameof(ToolTipText));
        });

    [LoggerMessage(Level = LogLevel.Error, Message = "{Operation}に失敗しました")]
    private partial void LogOperationFailed(Exception exception, string operation);
}
