using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.UI.Services;

namespace WallpaperChanger.UI.ViewModels;

/// <summary>
/// 設定画面の ViewModel。左側でモニターを選び、右側でそのモニターの壁紙設定を編集する。
/// 変更は即座に設定ファイルへ保存される。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private const int PreviewDecodePixelWidth = 640;

    private readonly IMonitorService _monitorService;
    private readonly ISettingsService _settingsService;
    private readonly IWallpaperService _wallpaperService;
    private readonly IWallpaperRotationService _rotationService;
    private readonly IWallpaperScheduler _scheduler;
    private readonly IFolderPickerService _folderPicker;
    private readonly IImagePreviewLoader _previewLoader;
    private readonly IMonitorIdentifierService _monitorIdentifier;
    private readonly IStartupRegistrationService _startupRegistration;
    private readonly IMonitorConfigurationWatcher _monitorWatcher;
    private readonly ILogDirectoryProvider _logDirectory;
    private readonly IFolderLauncher _folderLauncher;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<SettingsViewModel> _logger;

    private CancellationTokenSource? _previewCts;
    private bool _isLoading;
    private bool _disposed;

    public SettingsViewModel(
        IMonitorService monitorService,
        ISettingsService settingsService,
        IWallpaperService wallpaperService,
        IWallpaperRotationService rotationService,
        IWallpaperScheduler scheduler,
        IFolderPickerService folderPicker,
        IImagePreviewLoader previewLoader,
        IMonitorIdentifierService monitorIdentifier,
        IStartupRegistrationService startupRegistration,
        IMonitorConfigurationWatcher monitorWatcher,
        ILogDirectoryProvider logDirectory,
        IFolderLauncher folderLauncher,
        IUiDispatcher uiDispatcher,
        ILogger<SettingsViewModel> logger)
    {
        _logDirectory = logDirectory ?? throw new ArgumentNullException(nameof(logDirectory));
        _folderLauncher = folderLauncher ?? throw new ArgumentNullException(nameof(folderLauncher));
        _monitorIdentifier = monitorIdentifier ?? throw new ArgumentNullException(nameof(monitorIdentifier));
        _startupRegistration = startupRegistration ?? throw new ArgumentNullException(nameof(startupRegistration));
        _monitorWatcher = monitorWatcher ?? throw new ArgumentNullException(nameof(monitorWatcher));
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _wallpaperService = wallpaperService ?? throw new ArgumentNullException(nameof(wallpaperService));
        _rotationService = rotationService ?? throw new ArgumentNullException(nameof(rotationService));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        _previewLoader = previewLoader ?? throw new ArgumentNullException(nameof(previewLoader));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 初期値の代入で変更ハンドラが保存処理を走らせないよう、読み込み中として扱う
        _isLoading = true;
        SelectedIntervalUnit = IntervalUnitOption.Minutes;
        IntervalValue = AppSettings.DefaultIntervalMinutes;
        IncludeSubfolders = true;
        Tiles = [.. Enumerable.Range(0, WallpaperLayoutExtensions.QuadrantCount).Select(i => new TileItemViewModel(i))];
        SelectedTile = Tiles[0];
        _isLoading = false;

        _rotationService.WallpaperChanged += OnWallpaperChanged;
        _scheduler.StateChanged += OnSchedulerStateChanged;
        _monitorWatcher.ConfigurationChanged += OnMonitorConfigurationChanged;
    }

    public ObservableCollection<MonitorItemViewModel> Monitors { get; } = [];

    public ObservableCollection<string> Folders { get; } = [];

    /// <summary>
    /// 分割表示でフォルダを設定する単位（左上・右上・左下・右下）。
    /// 4 分割では各マス、16 分割では 4 マスずつの各区画を表し、両方のレイアウトで設定を共有する。
    /// </summary>
    public IReadOnlyList<TileItemViewModel> Tiles { get; }

    public IReadOnlyList<IntervalUnitOption> IntervalUnits { get; } = IntervalUnitOption.All;

    // ---- モニター ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedMonitor))]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeSelectedMonitorNowCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousSelectedMonitorCommand))]
    public partial MonitorItemViewModel? SelectedMonitor { get; set; }

    public bool HasSelectedMonitor => SelectedMonitor is not null;

    // ---- 壁紙フォルダ ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveFolderCommand))]
    public partial string? SelectedFolder { get; set; }

    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; }

    // ---- プレビュー ----

    [ObservableProperty]
    public partial ImageSource? PreviewImage { get; set; }

    [ObservableProperty]
    public partial string? CurrentWallpaperPath { get; set; }

    // ---- ランダム設定 ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRandomSelection))]
    [NotifyPropertyChangedFor(nameof(IsSequentialSelection))]
    public partial ImageSelectionMode SelectionMode { get; set; }

    public bool IsRandomSelection
    {
        get => SelectionMode == ImageSelectionMode.Random;
        set
        {
            if (value)
            {
                SelectionMode = ImageSelectionMode.Random;
            }
        }
    }

    public bool IsSequentialSelection
    {
        get => SelectionMode == ImageSelectionMode.Sequential;
        set
        {
            if (value)
            {
                SelectionMode = ImageSelectionMode.Sequential;
            }
        }
    }

    // ---- 表示レイアウト ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSingleImageLayout))]
    [NotifyPropertyChangedFor(nameof(IsGridLayout))]
    [NotifyPropertyChangedFor(nameof(IsGrid4x4Layout))]
    [NotifyPropertyChangedFor(nameof(IsSplitLayout))]
    [NotifyPropertyChangedFor(nameof(FolderCaption))]
    [NotifyPropertyChangedFor(nameof(EditingFolders))]
    [NotifyPropertyChangedFor(nameof(EditingTargetText))]
    [NotifyPropertyChangedFor(nameof(EmptyFoldersMessage))]
    public partial WallpaperLayout Layout { get; set; }

    /// <summary>壁紙フォルダ欄の説明。</summary>
    public string FolderCaption => Layout switch
    {
        WallpaperLayout.Grid2x2 => "マスを選んでフォルダを設定します。どのマスも未設定なら 1 枚表示のフォルダから 4 枚を選び、1 つでも設定すると未設定のマスは黒になります。",
        WallpaperLayout.Grid4x4 => "区画（4 マスずつ）を選んでフォルダを設定します。どの区画も未設定なら 1 枚表示のフォルダから 16 枚を選び、1 つでも設定すると未設定の区画は黒になります。4 分割のマスの設定と共通です。",
        _ => "このモニターに表示する画像のフォルダ（JPG / JPEG / PNG / BMP / WEBP）",
    };

    /// <summary>いずれかのマス（区画）に専用フォルダがあるか（あれば未設定のマス（区画）は黒になる）。</summary>
    private bool AnyTileHasFolders => Tiles.Any(t => t.HasOwnFolders);

    /// <summary>画面の文言で使う、フォルダを設定する単位の呼び方。</summary>
    private string TileUnit => Layout == WallpaperLayout.Grid4x4 ? "区画" : "マス";

    /// <summary>分割表示で編集中のマス（区画）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditingFolders))]
    [NotifyPropertyChangedFor(nameof(EditingTargetText))]
    public partial TileItemViewModel? SelectedTile { get; set; }

    /// <summary>
    /// 壁紙フォルダ欄に表示・編集するフォルダ一覧。
    /// 1 枚表示ではモニターのフォルダ、分割表示では選択中のマス（区画）のフォルダ。
    /// </summary>
    public ObservableCollection<string>? EditingFolders => IsSplitLayout ? SelectedTile?.Folders : Folders;

    /// <summary>分割表示で編集対象のマス（区画）を示す見出し。</summary>
    public string? EditingTargetText => IsSplitLayout && SelectedTile is { } tile ? $"{tile.Position} の{TileUnit}のフォルダ" : null;

    public string EmptyFoldersMessage => (IsSplitLayout, AnyTileHasFolders) switch
    {
        (true, true) => $"未設定（この{TileUnit}は黒で表示）",
        (true, false) => "未設定（1 枚表示のフォルダから選ぶ）",
        _ => "フォルダが登録されていません。「追加」から登録してください。",
    };

    public bool IsSingleImageLayout
    {
        get => Layout == WallpaperLayout.SingleImage;
        set
        {
            if (value)
            {
                Layout = WallpaperLayout.SingleImage;
            }
        }
    }

    /// <summary>4 分割表示か。</summary>
    public bool IsGridLayout
    {
        get => Layout == WallpaperLayout.Grid2x2;
        set
        {
            if (value)
            {
                Layout = WallpaperLayout.Grid2x2;
            }
        }
    }

    /// <summary>16 分割表示か（4 マスずつの区画ごとにフォルダを設定できる）。</summary>
    public bool IsGrid4x4Layout
    {
        get => Layout == WallpaperLayout.Grid4x4;
        set
        {
            if (value)
            {
                Layout = WallpaperLayout.Grid4x4;
            }
        }
    }

    /// <summary>複数の画像を合成する分割表示（4 分割・16 分割）か。</summary>
    public bool IsSplitLayout => Layout != WallpaperLayout.SingleImage;

    // ---- 切替設定 ----

    [ObservableProperty]
    public partial int IntervalValue { get; set; }

    [ObservableProperty]
    public partial IntervalUnitOption SelectedIntervalUnit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIntervalError))]
    public partial string? IntervalError { get; set; }

    public bool HasIntervalError => IntervalError is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    [NotifyPropertyChangedFor(nameof(ScheduleStatusText))]
    public partial bool IsPaused { get; set; }

    public string PauseButtonText => IsPaused ? "再開" : "一時停止";

    public string ScheduleStatusText => IsPaused
        ? "自動切り替えは一時停止中です"
        : $"{IntervalUnitOption.Format(_scheduler.Interval)}ごとに自動で切り替えます";

    // ---- 起動 ----

    /// <summary>Windows へのサインイン時に自動起動するか（レジストリの登録状態を表示・変更する）。</summary>
    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    // ---- ステータス ----

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsStatusError { get; set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _rotationService.WallpaperChanged -= OnWallpaperChanged;
        _scheduler.StateChanged -= OnSchedulerStateChanged;
        _monitorWatcher.ConfigurationChanged -= OnMonitorConfigurationChanged;
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
    }

    // ---- コマンド ----

    [RelayCommand]
    private Task InitializeAsync() => ExecuteSafelyAsync("設定の読み込み", async () =>
    {
        LoadGlobalSettings();
        await LoadMonitorsAsync();
    });

    [RelayCommand]
    private Task RefreshMonitorsAsync() => ExecuteSafelyAsync("モニターの再検出", async () =>
    {
        await LoadMonitorsAsync();
        ShowInfo($"{Monitors.Count} 台のモニターを検出しました。");
    });

    /// <summary>ログファイルの保存先（画面に表示する）。</summary>
    public string LogDirectory => _logDirectory.LogDirectory;

    [RelayCommand]
    private Task OpenLogFolderAsync() => ExecuteSafelyAsync("ログフォルダを開く処理", () =>
    {
        _folderLauncher.OpenFolder(_logDirectory.LogDirectory);
        return Task.CompletedTask;
    });

    /// <summary>各モニターの画面に、一覧と同じ番号を一定時間表示する。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task IdentifyMonitorsAsync() => ExecuteSafelyAsync("モニターの識別", async () =>
    {
        MonitorIdentification[] monitors =
        [
            .. Monitors.Select(m => new MonitorIdentification(m.Number, m.Monitor, ReferenceEquals(m, SelectedMonitor))),
        ];

        if (monitors.Length == 0)
        {
            return;
        }

        await _monitorIdentifier.ShowAsync(monitors);
    });

    /// <summary>
    /// 編集中の一覧にフォルダを追加する。1 枚表示ではモニターのフォルダ、分割表示では選択中のマス（区画）のフォルダが対象。
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedMonitor))]
    private Task AddFolderAsync() => ExecuteSafelyAsync("フォルダの追加", async () =>
    {
        if (SelectedMonitor is not { } monitor)
        {
            return;
        }

        if (IsSplitLayout)
        {
            if (SelectedTile is not { } tile)
            {
                return;
            }

            IReadOnlyList<string> tileFolders = _folderPicker.PickFolders($"{tile.Position}の{TileUnit}に表示するフォルダを選択");
            if (tileFolders.Count == 0)
            {
                return;
            }

            await UpdateMonitorSettingsAsync(monitor, m => tileFolders.Aggregate(m, (current, folder) => current.AddTileFolder(tile.Index, folder)));
            ShowInfo($"{tile.Position}の{TileUnit}に {tileFolders.Count} 件のフォルダを追加しました。");

            // 設定したフォルダの画像をすぐに確認できるよう表示し直す
            await _rotationService.NextAsync(monitor.Id);
            return;
        }

        IReadOnlyList<string> folders = _folderPicker.PickFolders("壁紙フォルダを選択");
        if (folders.Count == 0)
        {
            return;
        }

        bool wasEmpty = Folders.Count == 0;
        await UpdateMonitorSettingsAsync(monitor, m => folders.Aggregate(m, (current, folder) => current.AddFolder(folder)));
        ShowInfo($"{folders.Count} 件のフォルダを追加しました。");

        // 初めてフォルダを登録したモニターは、次の定期実行を待たずに壁紙を反映する
        if (wasEmpty)
        {
            await _rotationService.NextAsync(monitor.Id);
        }
    });

    /// <summary>
    /// 編集中の一覧からフォルダを削除する。1 枚表示ではモニターのフォルダ、分割表示では選択中のマス（区画）のフォルダが対象。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveFolder))]
    private Task RemoveFolderAsync() => ExecuteSafelyAsync("フォルダの削除", async () =>
    {
        if (SelectedMonitor is not { } monitor || SelectedFolder is not { } folder)
        {
            return;
        }

        if (IsSplitLayout)
        {
            if (SelectedTile is not { } tile)
            {
                return;
            }

            await UpdateMonitorSettingsAsync(monitor, m => m.RemoveTileFolder(tile.Index, folder));
            ShowInfo((tile.HasOwnFolders, AnyTileHasFolders) switch
            {
                (true, _) => $"{tile.Position}の{TileUnit}からフォルダを削除しました。",
                (false, true) => $"{tile.Position}の{TileUnit}は未設定になり、黒で表示します。",
                _ => $"すべての{TileUnit}が未設定になったため、1 枚表示のフォルダから {Layout.GetTileCount()} 枚を選びます。",
            });

            // マスの表示内容が変わるため、すぐに表示し直す
            if (_settingsService.Current.GetMonitor(monitor.Id).HasAnyFolder())
            {
                await _rotationService.NextAsync(monitor.Id);
            }

            return;
        }

        await UpdateMonitorSettingsAsync(monitor, m => m.RemoveFolder(folder));
        ShowInfo("フォルダを削除しました。");
    });

    private bool CanRemoveFolder() => SelectedMonitor is not null && SelectedFolder is not null;

    [RelayCommand(CanExecute = nameof(HasSelectedMonitor))]
    private Task ChangeSelectedMonitorNowAsync() => ExecuteSafelyAsync("壁紙の変更", async () =>
    {
        if (SelectedMonitor is { } monitor)
        {
            await _rotationService.NextAsync(monitor.Id);
        }
    });

    [RelayCommand(CanExecute = nameof(HasSelectedMonitor))]
    private Task PreviousSelectedMonitorAsync() => ExecuteSafelyAsync("前の壁紙への切り替え", async () =>
    {
        if (SelectedMonitor is { } monitor)
        {
            await _rotationService.PreviousAsync(monitor.Id);
        }
    });

    [RelayCommand]
    private Task ChangeAllNowAsync() => ExecuteSafelyAsync("全モニターの壁紙の変更", async () =>
    {
        await _scheduler.ChangeNowAsync();
        ShowInfo("全モニターの壁紙を変更しました。");
    });

    [RelayCommand]
    private Task TogglePauseAsync() => ExecuteSafelyAsync("一時停止状態の変更", async () =>
    {
        if (IsPaused)
        {
            await _scheduler.ResumeAsync();
        }
        else
        {
            await _scheduler.PauseAsync();
        }
    });

    // ---- プロパティ変更時の保存 ----

    // 編集対象の一覧が切り替わったら、前の一覧での選択は解除する
    partial void OnSelectedTileChanged(TileItemViewModel? value) => SelectedFolder = null;

    partial void OnSelectedMonitorChanged(MonitorItemViewModel? value)
    {
        LoadMonitorSettings(value);
        RemoveFolderCommand.NotifyCanExecuteChanged();
        _ = ExecuteSafelyAsync("プレビューの更新", RefreshPreviewAsync);
    }

    partial void OnIncludeSubfoldersChanged(bool value)
    {
        if (!_isLoading && SelectedMonitor is { } monitor)
        {
            _ = ExecuteSafelyAsync("サブフォルダ設定の保存",
                () => UpdateMonitorSettingsAsync(monitor, m => m with { IncludeSubfolders = value }));
        }
    }

    partial void OnSelectionModeChanged(ImageSelectionMode value)
    {
        if (!_isLoading && SelectedMonitor is { } monitor)
        {
            _ = ExecuteSafelyAsync("選択方法の保存",
                () => UpdateMonitorSettingsAsync(monitor, m => m with { SelectionMode = value }));
        }
    }

    partial void OnLayoutChanged(WallpaperLayout value)
    {
        // 表示する一覧（モニター／マス）が切り替わるため、前の一覧での選択は解除する
        SelectedFolder = null;

        if (_isLoading || SelectedMonitor is not { } monitor)
        {
            return;
        }

        _ = ExecuteSafelyAsync("表示レイアウトの変更", async () =>
        {
            await UpdateMonitorSettingsAsync(monitor, m => m with { Layout = value });

            // 次の定期実行を待たず、新しいレイアウトで表示し直す
            if (_settingsService.Current.GetMonitor(monitor.Id).HasAnyFolder())
            {
                await _rotationService.NextAsync(monitor.Id);
            }

            ShowInfo(value switch
            {
                WallpaperLayout.Grid2x2 => "4 分割表示に切り替えました。",
                WallpaperLayout.Grid4x4 => "16 分割表示に切り替えました。",
                _ => "1 枚表示に切り替えました。",
            });
        });
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        try
        {
            if (value)
            {
                _startupRegistration.Enable();
                ShowInfo("Windows の起動時に自動的に開始するようにしました。");
            }
            else
            {
                _startupRegistration.Disable();
                ShowInfo("Windows の起動時に自動的に開始しないようにしました。");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            LogOperationFailed(ex, "自動起動の設定");
            ShowError($"自動起動の設定に失敗しました。{ErrorMessages.Describe(ex)}");

            // チェックの表示を実際の登録状態に戻す
            LoadStartupState();
        }
    }

    partial void OnIntervalValueChanged(int value) => ApplyInterval();

    partial void OnSelectedIntervalUnitChanged(IntervalUnitOption value) => ApplyInterval();

    private void ApplyInterval()
    {
        if (_isLoading || SelectedIntervalUnit is null)
        {
            return;
        }

        int minutes = IntervalValue * SelectedIntervalUnit.MinutesPerUnit;
        if (!AppSettings.IsValidInterval(minutes))
        {
            IntervalError = "切り替え間隔は 1 分～24 時間の範囲で指定してください。";
            return;
        }

        IntervalError = null;
        if (minutes == _settingsService.Current.IntervalMinutes)
        {
            return;
        }

        _ = ExecuteSafelyAsync("切り替え間隔の保存", async () =>
        {
            await _settingsService.UpdateAsync(s => s.WithInterval(minutes));
            OnPropertyChanged(nameof(ScheduleStatusText));
            ShowInfo($"切り替え間隔を {IntervalUnitOption.Format(TimeSpan.FromMinutes(minutes))} に変更しました。");
        });
    }

    // ---- 内部処理 ----

    private void LoadGlobalSettings()
    {
        _isLoading = true;
        try
        {
            (int value, IntervalUnitOption unit) = IntervalUnitOption.FromMinutes(_settingsService.Current.IntervalMinutes);
            SelectedIntervalUnit = unit;
            IntervalValue = value;
            IntervalError = null;
            IsPaused = _scheduler.IsPaused;
        }
        finally
        {
            _isLoading = false;
        }

        LoadStartupState();
    }

    private void LoadStartupState()
    {
        bool isEnabled;
        try
        {
            isEnabled = _startupRegistration.IsEnabled();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            // 読み取れない場合も他の設定は使えるよう、未登録として表示してエラーを伝える
            LogOperationFailed(ex, "自動起動の状態の取得");
            ShowError($"自動起動の状態を取得できませんでした。{ErrorMessages.Describe(ex)}");
            isEnabled = false;
        }

        _isLoading = true;
        try
        {
            StartWithWindows = isEnabled;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task LoadMonitorsAsync()
    {
        string? selectedId = SelectedMonitor?.Id;
        IReadOnlyList<MonitorInfo> monitors = await _monitorService.GetMonitorsAsync();

        Monitors.Clear();
        for (int i = 0; i < monitors.Count; i++)
        {
            Monitors.Add(new MonitorItemViewModel(monitors[i], i + 1));
        }

        SelectedMonitor =
            Monitors.FirstOrDefault(m => string.Equals(m.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            ?? Monitors.FirstOrDefault(m => m.IsPrimary)
            ?? Monitors.FirstOrDefault();
    }

    private void LoadMonitorSettings(MonitorItemViewModel? monitor)
    {
        _isLoading = true;
        try
        {
            Folders.Clear();
            SelectedFolder = null;
            if (monitor is null)
            {
                foreach (TileItemViewModel tile in Tiles)
                {
                    tile.SetFolders([], isBlankWhenEmpty: false);
                }

                return;
            }

            MonitorSettings settings = _settingsService.Current.GetMonitor(monitor.Id);
            foreach (string folder in settings.Folders)
            {
                Folders.Add(folder);
            }

            // 1 つでもマスに専用フォルダがあれば、未設定のマスは黒で表示される
            bool anyTileHasFolders = Tiles.Any(t => settings.GetTile(t.Index).Folders.Count > 0);
            foreach (TileItemViewModel tile in Tiles)
            {
                tile.SetFolders(settings.GetTile(tile.Index).Folders, isBlankWhenEmpty: anyTileHasFolders);
            }

            IncludeSubfolders = settings.IncludeSubfolders;
            SelectionMode = settings.SelectionMode;
            Layout = settings.Layout;
        }
        finally
        {
            _isLoading = false;
            OnPropertyChanged(nameof(EmptyFoldersMessage));
        }
    }

    private async Task UpdateMonitorSettingsAsync(MonitorItemViewModel monitor, Func<MonitorSettings, MonitorSettings> update)
    {
        await _settingsService.UpdateAsync(s => s.WithMonitor(update(s.GetMonitor(monitor.Id))));

        if (ReferenceEquals(SelectedMonitor, monitor))
        {
            LoadMonitorSettings(monitor);
        }
    }

    private async Task RefreshPreviewAsync()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;

        if (SelectedMonitor is not { } monitor)
        {
            PreviewImage = null;
            CurrentWallpaperPath = null;
            return;
        }

        var cts = new CancellationTokenSource();
        _previewCts = cts;
        CancellationToken token = cts.Token;

        string? path = await _wallpaperService.GetWallpaperAsync(monitor.Id, token);
        ImageSource? image = path is null
            ? null
            : await _previewLoader.LoadAsync(path, PreviewDecodePixelWidth, token);

        // 読み込み中に別のモニターが選択された場合は結果を捨てる
        if (token.IsCancellationRequested || !ReferenceEquals(SelectedMonitor, monitor))
        {
            return;
        }

        CurrentWallpaperPath = path ?? "（単色の背景）";
        PreviewImage = image;
    }

    private async Task ExecuteSafelyAsync(string operation, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            // 画面を閉じた・選択を切り替えたことによる、この ViewModel 自身が要求したキャンセル
        }
        catch (Exception ex)
        {
            // 画面操作の失敗でアプリを終了させず、ログと画面下部のメッセージでユーザーに伝える
            LogOperationFailed(ex, operation);
            ShowError($"{operation}に失敗しました。{ErrorMessages.Describe(ex)}");
        }
    }

    private void ShowInfo(string message)
    {
        IsStatusError = false;
        StatusMessage = message;
    }

    private void ShowError(string message)
    {
        IsStatusError = true;
        StatusMessage = message;
    }

    private async void OnWallpaperChanged(object? sender, WallpaperChangedEventArgs e) =>
        await _uiDispatcher.InvokeAsync(() =>
        {
            if (!_disposed && string.Equals(SelectedMonitor?.Id, e.MonitorId, StringComparison.OrdinalIgnoreCase))
            {
                _ = ExecuteSafelyAsync("プレビューの更新", RefreshPreviewAsync);
            }
        });

    /// <summary>モニターの接続・切断や配置の変更に合わせて、一覧（番号）を作り直す。選択中のモニターは維持する。</summary>
    private async void OnMonitorConfigurationChanged(object? sender, MonitorConfigurationChangedEventArgs e) =>
        await _uiDispatcher.InvokeAsync(() =>
        {
            if (_disposed)
            {
                return;
            }

            _ = ExecuteSafelyAsync("モニター一覧の更新", async () =>
            {
                await LoadMonitorsAsync();
                ShowInfo(DescribeConfigurationChange(e));
            });
        });

    private static string DescribeConfigurationChange(MonitorConfigurationChangedEventArgs e)
    {
        var parts = new List<string>();
        if (e.Added.Count > 0)
        {
            parts.Add($"接続: {string.Join("、", e.Added.Select(m => m.DisplayName))}");
        }

        if (e.Removed.Count > 0)
        {
            parts.Add($"切断: {string.Join("、", e.Removed.Select(m => m.DisplayName))}");
        }

        if (e.Resized.Count > 0)
        {
            parts.Add($"解像度の変更: {string.Join("、", e.Resized.Select(m => $"{m.DisplayName}（{m.ResolutionText}）"))}");
        }

        return parts.Count > 0
            ? $"モニター構成の変更を検出しました。{string.Join(" / ", parts)}"
            : "モニターの配置の変更を検出しました。";
    }

    private async void OnSchedulerStateChanged(object? sender, EventArgs e) =>
        await _uiDispatcher.InvokeAsync(() =>
        {
            IsPaused = _scheduler.IsPaused;
            OnPropertyChanged(nameof(ScheduleStatusText));
        });

    [LoggerMessage(Level = LogLevel.Error, Message = "{Operation}に失敗しました")]
    private partial void LogOperationFailed(Exception exception, string operation);
}
