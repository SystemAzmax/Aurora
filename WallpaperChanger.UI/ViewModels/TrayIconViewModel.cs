using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.UI.Services;

namespace WallpaperChanger.UI.ViewModels;

/// <summary>
/// タスクトレイアイコンとその右クリックメニューの ViewModel。
/// </summary>
public sealed partial class TrayIconViewModel : ObservableObject, IDisposable
{
    private const string AppName = "壁紙チェンジャー";
    private const string ChangedPopupText = "壁紙を切り替えました";
    private static readonly string[] QuadrantNames = ["左上", "右上", "左下", "右下"];

    /// <summary>メニューから始めた切り替えが終わった後、終わったことを伝える表示を残す時間。</summary>
    internal static readonly TimeSpan CompletedPopupDuration = TimeSpan.FromSeconds(2);

    private readonly IWallpaperRotationService _rotationService;
    private readonly IWallpaperScheduler _scheduler;
    private readonly ISettingsService _settingsService;
    private readonly ISettingsWindowService _settingsWindowService;
    private readonly IFolderLauncher _folderLauncher;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TrayIconViewModel> _logger;

    /// <summary>進み具合の表示を開いた回数。閉じる予約が、その後に開き直した表示を閉じないようにする。</summary>
    private int _progressPopupVersion;

    /// <summary>メニューから始めた切り替えが終わったときに、トレイの近くに表示する文言。</summary>
    private string _completedPopupText = ChangedPopupText;

    public TrayIconViewModel(
        IWallpaperRotationService rotationService,
        IWallpaperScheduler scheduler,
        ISettingsService settingsService,
        ISettingsWindowService settingsWindowService,
        IFolderLauncher folderLauncher,
        IHostApplicationLifetime applicationLifetime,
        IUiDispatcher uiDispatcher,
        TimeProvider timeProvider,
        ILogger<TrayIconViewModel> logger)
    {
        _rotationService = rotationService ?? throw new ArgumentNullException(nameof(rotationService));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _settingsWindowService = settingsWindowService ?? throw new ArgumentNullException(nameof(settingsWindowService));
        _folderLauncher = folderLauncher ?? throw new ArgumentNullException(nameof(folderLauncher));
        _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        IsPaused = _scheduler.IsPaused;
        ShownImages = [];
        _scheduler.StateChanged += OnSchedulerStateChanged;
        _rotationService.ProgressChanged += OnProgressChanged;
    }

    /// <summary>バルーン通知の表示要求。</summary>
    public event EventHandler<TrayNotificationEventArgs>? NotificationRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseMenuHeader))]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    public partial bool IsPaused { get; set; }

    public string PauseMenuHeader => IsPaused ? "再開" : "一時停止";

    public string ToolTipText => ChangeProgressText is { } progress
        ? $"{AppName}\n{progress}"
        : IsPaused
            ? $"{AppName}（一時停止中）"
            : $"{AppName}（{IntervalUnitOption.Format(_scheduler.Interval)}ごとに切り替え）";

    // ---- 切り替えの進み具合 ----

    /// <summary>切り替え中（自動切り替えを含む）の進み具合。切り替え中でなければ null。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChangingWallpaper))]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    [NotifyPropertyChangedFor(nameof(ProgressPopupText))]
    [NotifyCanExecuteChangedFor(
        nameof(NextWallpaperCommand), nameof(PreviousWallpaperCommand), nameof(RefreshAllWallpapersCommand), nameof(ExcludeImageCommand))]
    public partial string? ChangeProgressText { get; set; }

    /// <summary>メニューから始めた切り替えを実行中か。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChangingWallpaper))]
    [NotifyPropertyChangedFor(nameof(ProgressPopupText))]
    [NotifyCanExecuteChangedFor(
        nameof(NextWallpaperCommand), nameof(PreviousWallpaperCommand), nameof(RefreshAllWallpapersCommand), nameof(ExcludeImageCommand))]
    public partial bool IsMenuChangeRunning { get; set; }

    /// <summary>切り替え中か。切り替え中はメニューから切り替えられない（順番待ちになり、さらに待たされないように）。</summary>
    public bool IsChangingWallpaper => IsMenuChangeRunning || ChangeProgressText is not null;

    /// <summary>メニューから始めた切り替えの進み具合を、トレイの近くに表示するか。</summary>
    [ObservableProperty]
    public partial bool IsProgressPopupOpen { get; set; }

    public string ProgressPopupText => IsMenuChangeRunning
        ? ChangeProgressText ?? "壁紙を切り替えています…"
        : _completedPopupText;

    // ---- 表示中の画像 ----

    /// <summary>
    /// メニューの「この画像を表示しない」「画像の場所を開く」の下に並べる、表示中の画像。
    /// モニターが複数ならモニターごとに並べ、分割表示のモニターはその下にマスごとに並べる。メニューを開くたびに作り直す。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShownImages))]
    public partial IReadOnlyList<ShownImageItemViewModel> ShownImages { get; private set; }

    public bool HasShownImages => ShownImages.Count > 0;

    public void Dispose()
    {
        _scheduler.StateChanged -= OnSchedulerStateChanged;
        _rotationService.ProgressChanged -= OnProgressChanged;
    }

    [RelayCommand(CanExecute = nameof(CanChangeWallpaper))]
    private Task NextWallpaperAsync() => ChangeFromMenuAsync("次の壁紙への切り替え", () => _scheduler.ChangeNowAsync());

    [RelayCommand(CanExecute = nameof(CanChangeWallpaper))]
    private Task PreviousWallpaperAsync() => ChangeFromMenuAsync("前の壁紙への切り替え", () => _rotationService.PreviousAsync());

    [RelayCommand(CanExecute = nameof(CanChangeWallpaper))]
    private Task RefreshAllWallpapersAsync() => ChangeFromMenuAsync("全壁紙の更新", () => _scheduler.RefreshNowAsync());

    private bool CanChangeWallpaper() => !IsChangingWallpaper;

    /// <summary>画像を表示しない画像に加え、その画像を表示しているマス（1 枚表示ならモニター）を別の画像に差し替える。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeWallpaper))]
    private Task ExcludeImageAsync(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return Task.CompletedTask;
        }

        return ChangeFromMenuAsync(
            "表示しない画像の設定",
            async () =>
            {
                await _settingsService.UpdateAsync(s => s.AddExcludedImage(imagePath));
                return await _rotationService.ReplaceExcludedImagesAsync();
            },
            $"{Path.GetFileName(imagePath)} を表示しないようにしました（設定画面で戻せます）");
    }

    [RelayCommand]
    private Task ShowImageInFolderAsync(string? imagePath) => ExecuteSafelyAsync("画像の場所を開く処理", () =>
    {
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            _folderLauncher.ShowFileInFolder(imagePath);
        }

        return Task.CompletedTask;
    });

    /// <summary>表示中の画像の一覧を作り直す。メニューを開いたときに呼ぶ。</summary>
    [RelayCommand]
    private async Task RefreshShownImagesAsync()
    {
        try
        {
            ShownImages = CreateShownImageItems(await _rotationService.GetCurrentWallpapersAsync());
        }
        catch (Exception ex)
        {
            // メニューを開くたびに通知が出ないよう、ログにだけ残す（前回の一覧はそのまま使う）
            LogOperationFailed(ex, "表示中の画像の取得");
        }
    }

    private List<ShownImageItemViewModel> CreateShownImageItems(IReadOnlyList<CurrentWallpaper> wallpapers)
    {
        // モニターが 1 台なら、モニターの階層を省く
        if (wallpapers is [{ Selection: { } onlySelection }])
        {
            return CreateImageItems(onlySelection);
        }

        var items = new List<ShownImageItemViewModel>();
        for (int i = 0; i < wallpapers.Count; i++)
        {
            if (wallpapers[i].Selection is not { } selection)
            {
                continue;
            }

            // 番号は設定画面のモニター一覧と同じ（画面配置の順）
            string monitorName = $"{i + 1}. {wallpapers[i].Monitor.DisplayName}";
            items.Add(selection.IsSingleImage
                ? CreateImageItem($"{monitorName}: {Path.GetFileName(selection.Images[0])}", selection.Images[0]!)
                : ShownImageItemViewModel.ForGroup(monitorName, CreateImageItems(selection)));
        }

        return items;
    }

    private List<ShownImageItemViewModel> CreateImageItems(WallpaperSelection selection)
    {
        if (selection.IsSingleImage)
        {
            return [CreateImageItem(Path.GetFileName(selection.Images[0])!, selection.Images[0]!)];
        }

        var items = new List<ShownImageItemViewModel>();
        for (int i = 0; i < selection.Images.Count; i++)
        {
            if (selection.Images[i] is { } image)
            {
                items.Add(CreateImageItem($"{GetTileName(selection.Layout, i)}: {Path.GetFileName(image)}", image));
            }
        }

        return items;
    }

    private ShownImageItemViewModel CreateImageItem(string header, string imagePath) =>
        ShownImageItemViewModel.ForImage(header, imagePath, ExcludeImageCommand, ShowImageInFolderCommand);

    /// <summary>マスの呼び方。4 分割は位置、16 分割は行と列で示す。</summary>
    private static string GetTileName(WallpaperLayout layout, int index)
    {
        if (layout == WallpaperLayout.Grid2x2)
        {
            return QuadrantNames[index];
        }

        (int columns, _) = layout.GetGridSize();
        return $"{(index / columns) + 1} 行 {(index % columns) + 1} 列";
    }

    /// <summary>
    /// メニューから切り替える。メニューを閉じると何も見えなくなるため、終わるまで進み具合をトレイの近くに表示し、
    /// 終わったこともしばらく表示する。変わらなかったモニターがあれば、表示を閉じて理由を通知する。
    /// </summary>
    /// <param name="completedText">終わったときに表示する文言。</param>
    private async Task ChangeFromMenuAsync(
        string operation, Func<Task<WallpaperChangeResult>> change, string completedText = ChangedPopupText)
    {
        int popupVersion = ++_progressPopupVersion;
        _completedPopupText = completedText;
        IsMenuChangeRunning = true;
        IsProgressPopupOpen = true;

        bool succeeded = false;
        await ExecuteSafelyAsync(operation, async () =>
        {
            succeeded = !NotifyProblems(await change());
        });

        IsMenuChangeRunning = false;
        if (succeeded)
        {
            _ = CloseProgressPopupLaterAsync(popupVersion);
        }
        else
        {
            // 理由はバルーン通知で伝える
            IsProgressPopupOpen = false;
        }
    }

    private async Task CloseProgressPopupLaterAsync(int popupVersion)
    {
        await Task.Delay(CompletedPopupDuration, _timeProvider);
        await _uiDispatcher.InvokeAsync(() =>
        {
            // 表示している間に次の切り替えを始めていれば、その表示は閉じない
            if (popupVersion == _progressPopupVersion)
            {
                IsProgressPopupOpen = false;
            }
        });
    }

    /// <summary>メニューから切り替えても変わらなかったモニターがあれば、理由を通知する（何も起きないように見えないように）。</summary>
    /// <returns>通知した場合は true。</returns>
    private bool NotifyProblems(WallpaperChangeResult result)
    {
        if (WallpaperChangeMessages.DescribeProblems(result) is not { } problem)
        {
            return false;
        }

        NotificationRequested?.Invoke(this, new TrayNotificationEventArgs(AppName, problem, isError: false));
        return true;
    }

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

    private async void OnProgressChanged(object? sender, WallpaperChangeProgressEventArgs e) =>
        await _uiDispatcher.InvokeAsync(() => ChangeProgressText = WallpaperChangeMessages.DescribeProgress(e));

    [LoggerMessage(Level = LogLevel.Error, Message = "{Operation}に失敗しました")]
    private partial void LogOperationFailed(Exception exception, string operation);
}
