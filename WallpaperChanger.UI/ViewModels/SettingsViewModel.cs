using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.UI.Resources;
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
    private readonly IMessageDialogService _messageDialog;
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
        IMessageDialogService messageDialog,
        IUiDispatcher uiDispatcher,
        ILogger<SettingsViewModel> logger)
    {
        _logDirectory = logDirectory ?? throw new ArgumentNullException(nameof(logDirectory));
        _folderLauncher = folderLauncher ?? throw new ArgumentNullException(nameof(folderLauncher));
        _messageDialog = messageDialog ?? throw new ArgumentNullException(nameof(messageDialog));
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
        SelectedSingleImageFit = SingleImageFitOption.Windows;
        SelectedLanguage = LanguageOption.From(Localizer.CurrentLanguage);
        TileGap = TileStyle.DefaultGap;
        TileGapColors = TileColorOption.OptionsFor(TileStyle.DefaultGapColor);
        EmptyTileColors = TileColorOption.OptionsFor(TileStyle.DefaultEmptyColor);
        Tiles =[.. Enumerable.Range(0, WallpaperLayoutExtensions.QuadrantCount).Select(i => new TileItemViewModel(i))];
        SelectedTile = Tiles[0];
        _isLoading = false;

        _rotationService.WallpaperChanged += OnWallpaperChanged;
        _rotationService.ProgressChanged += OnProgressChanged;
        _scheduler.StateChanged += OnSchedulerStateChanged;
        _monitorWatcher.ConfigurationChanged += OnMonitorConfigurationChanged;
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    public ObservableCollection<MonitorItemViewModel> Monitors { get; } = [];

    public ObservableCollection<string> Folders { get; } = [];

    /// <summary>スパン（複数モニターにまたがる 1 枚表示）のフォルダ。スパンを選んだモニターで共通。</summary>
    public ObservableCollection<string> SpanFolders { get; } = [];

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
    [NotifyCanExecuteChangedFor(nameof(CopySettingsCommand))]
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
    [NotifyPropertyChangedFor(nameof(IsSpanLayout))]
    [NotifyPropertyChangedFor(nameof(IsSplitLayout))]
    [NotifyPropertyChangedFor(nameof(FolderCaption))]
    [NotifyPropertyChangedFor(nameof(EditingFolders))]
    [NotifyPropertyChangedFor(nameof(EditingTargetText))]
    [NotifyPropertyChangedFor(nameof(EmptyFoldersMessage))]
    public partial WallpaperLayout Layout { get; set; }

    /// <summary>壁紙フォルダ欄の説明。</summary>
    public string FolderCaption => Layout switch
    {
        WallpaperLayout.Grid2x2 => Strings.FolderCaptionGrid2x2,
        WallpaperLayout.Grid4x4 => Strings.FolderCaptionGrid4x4,
        WallpaperLayout.Span => Localizer.Format(Strings.FolderCaptionSpanFormat, SpanMonitorCount),
        _ => Strings.FolderCaptionSingle,
    };

    /// <summary>表示レイアウトにスパンを選んでいる、接続中のモニターの数。</summary>
    private int SpanMonitorCount => Monitors.Count(m => IsSpanMonitor(m.Id));

    /// <summary>いずれかのマス（区画）に専用フォルダがあるか（あれば未設定のマス（区画）は空きマスになる）。</summary>
    private bool AnyTileHasFolders => Tiles.Any(t => t.HasOwnFolders);

    /// <summary>画面の文言で使う、フォルダを設定する単位の呼び方。</summary>
    private string TileUnit => Layout == WallpaperLayout.Grid4x4 ? Strings.TileUnitSection : Strings.TileUnitTile;

    /// <summary>画面の文言で使う、マス（区画）の呼び方（例: 「左上のマス」）。</summary>
    private string DescribeTile(TileItemViewModel tile) => Localizer.Format(Strings.TileTargetFormat, tile.Position, TileUnit);

    /// <summary>分割表示で編集中のマス（区画）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditingFolders))]
    [NotifyPropertyChangedFor(nameof(EditingTargetText))]
    public partial TileItemViewModel? SelectedTile { get; set; }

    /// <summary>
    /// 壁紙フォルダ欄に表示・編集するフォルダ一覧。
    /// 1 枚表示ではモニターのフォルダ、分割表示では選択中のマス（区画）のフォルダ、スパンではスパンのフォルダ。
    /// </summary>
    public ObservableCollection<string>? EditingFolders => IsSplitLayout ? SelectedTile?.Folders : IsSpanLayout ? SpanFolders : Folders;

    /// <summary>分割表示で編集対象のマス（区画）を示す見出し。</summary>
    public string? EditingTargetText => IsSplitLayout && SelectedTile is { } tile ? Localizer.Format(Strings.EditingTargetFormat, tile.Position, TileUnit) : null;

    public string EmptyFoldersMessage => (IsSplitLayout, AnyTileHasFolders) switch
    {
        (true, true) => Localizer.Format(Strings.EmptyFoldersBlankFormat, TileUnit),
        (true, false) => Strings.EmptyFoldersUseSingle,
        _ => Strings.EmptyFolders,
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

    /// <summary>複数モニターにまたがる 1 枚表示（スパン）か。</summary>
    public bool IsSpanLayout
    {
        get => Layout == WallpaperLayout.Span;
        set
        {
            if (value)
            {
                Layout = WallpaperLayout.Span;
            }
        }
    }

    /// <summary>複数の画像を合成する分割表示（4 分割・16 分割）か。</summary>
    public bool IsSplitLayout => Layout.IsSplit();

    /// <summary>スパンで、画像をモニター全体を囲む範囲に収める方法。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpanFill))]
    [NotifyPropertyChangedFor(nameof(IsSpanFitWhole))]
    public partial ImageFit SpanFit { get; set; }

    /// <summary>スパンで、モニター全体を覆うように拡大し、はみ出した部分を切り抜くか。</summary>
    public bool IsSpanFill
    {
        get => SpanFit == ImageFit.Fill;
        set
        {
            if (value)
            {
                SpanFit = ImageFit.Fill;
            }
        }
    }

    /// <summary>スパンで、画像全体をモニター全体に収め、余白を残すか。</summary>
    public bool IsSpanFitWhole
    {
        get => SpanFit == ImageFit.Fit;
        set
        {
            if (value)
            {
                SpanFit = ImageFit.Fit;
            }
        }
    }

    public IReadOnlyList<SingleImageFitOption> SingleImageFits { get; } = SingleImageFitOption.All;

    /// <summary>1 枚表示の表示方法（拡大・全体表示・中央など）。</summary>
    [ObservableProperty]
    public partial SingleImageFitOption SelectedSingleImageFit { get; set; }

    // ---- 分割表示の見た目 ----

    /// <summary>分割表示で、各画像をマスに収める方法。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTileFill))]
    [NotifyPropertyChangedFor(nameof(IsTileFitWhole))]
    public partial ImageFit TileFit { get; set; }

    /// <summary>マスを覆うように拡大し、はみ出した部分を切り抜くか。</summary>
    public bool IsTileFill
    {
        get => TileFit == ImageFit.Fill;
        set
        {
            if (value)
            {
                TileFit = ImageFit.Fill;
            }
        }
    }

    /// <summary>画像全体をマスに収め、余白を残すか。</summary>
    public bool IsTileFitWhole
    {
        get => TileFit == ImageFit.Fit;
        set
        {
            if (value)
            {
                TileFit = ImageFit.Fit;
            }
        }
    }

    /// <summary>間隔のスライダーの上限（Slider.Maximum が double のため double で返す）。</summary>
    public static double MaxTileGap => TileStyle.MaxGap;

    /// <summary>マスの間隔（ピクセル）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TileGapText))]
    public partial int TileGap { get; set; }

    public string TileGapText => $"{TileGap} px";

    /// <summary>マスの間隔の色の選択肢。</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TileColorOption> TileGapColors { get; set; }

    [ObservableProperty]
    public partial TileColorOption? SelectedTileGapColor { get; set; }

    /// <summary>空きマスと余白の色の選択肢。</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TileColorOption> EmptyTileColors { get; set; }

    [ObservableProperty]
    public partial TileColorOption? SelectedEmptyTileColor { get; set; }

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

    public string PauseButtonText => IsPaused ? Strings.Resume : Strings.Pause;

    public string ScheduleStatusText => IsPaused
        ? Strings.SchedulePaused
        : _scheduler.Hold switch
        {
            AutoChangeHold.FullScreen => Strings.ScheduleHeldFullScreen,
            AutoChangeHold.OnBattery => Strings.ScheduleHeldOnBattery,
            _ => Localizer.Format(Strings.ScheduleIntervalFormat, IntervalUnitOption.Format(_scheduler.Interval)),
        };

    /// <summary>全画面表示のアプリやプレゼンテーションの実行中は、自動で切り替えないか。</summary>
    [ObservableProperty]
    public partial bool SkipWhenFullScreen { get; set; }

    /// <summary>バッテリーで動作している間は、自動で切り替えないか。</summary>
    [ObservableProperty]
    public partial bool SkipOnBattery { get; set; }

    // ---- 起動 ----

    /// <summary>Windows へのサインイン時に自動起動するか（レジストリの登録状態を表示・変更する）。</summary>
    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    // ---- 表示言語 ----

    /// <summary>表示言語の選択肢。</summary>
    public IReadOnlyList<LanguageOption> Languages { get; } = LanguageOption.All;

    /// <summary>設定の表示言語。変更はアプリの再起動後に反映する。</summary>
    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    // ---- 表示しない画像 ----

    /// <summary>表示しない画像（全モニター共通）。トレイのメニューの「この画像を表示しない」で加える。</summary>
    public ObservableCollection<string> ExcludedImages { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreExcludedImageCommand), nameof(ShowExcludedImageInFolderCommand))]
    public partial string? SelectedExcludedImage { get; set; }

    // ---- ほかのモニターの設定のコピー ----

    /// <summary>
    /// 選択中のモニターに設定をコピーできる、ほかのモニター（壁紙フォルダを設定したもの）。
    /// 買い替えたモニターなどに前のモニターの設定を使えるよう、接続していないモニターも含める。
    /// </summary>
    public ObservableCollection<MonitorCopySourceViewModel> CopySources { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopySettingsCommand))]
    public partial MonitorCopySourceViewModel? SelectedCopySource { get; set; }

    // ---- ステータス ----

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsStatusError { get; set; }

    /// <summary>切り替え中（自動切り替えを含む）の進み具合。切り替え中でなければ null。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChangingWallpaper))]
    [NotifyCanExecuteChangedFor(
        nameof(ChangeSelectedMonitorNowCommand), nameof(PreviousSelectedMonitorCommand), nameof(RefreshAllWallpapersCommand), nameof(CopySettingsCommand))]
    public partial string? ChangeProgressText { get; set; }

    /// <summary>切り替え中か。切り替え中は切り替えのボタンを押せない（順番待ちになり、さらに待たされないように）。</summary>
    public bool IsChangingWallpaper => ChangeProgressText is not null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _rotationService.WallpaperChanged -= OnWallpaperChanged;
        _rotationService.ProgressChanged -= OnProgressChanged;
        _scheduler.StateChanged -= OnSchedulerStateChanged;
        _monitorWatcher.ConfigurationChanged -= OnMonitorConfigurationChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
    }

    // ---- コマンド ----

    [RelayCommand]
    private Task InitializeAsync() => ExecuteSafelyAsync(Strings.OperationLoadSettings, async () =>
    {
        LoadGlobalSettings();
        await LoadMonitorsAsync();
    });

    [RelayCommand]
    private Task RefreshMonitorsAsync() => ExecuteSafelyAsync(Strings.OperationDetectMonitors, async () =>
    {
        await LoadMonitorsAsync();
        ShowInfo(Localizer.Format(Strings.MonitorsDetectedFormat, Monitors.Count));
    });

    /// <summary>ログファイルの保存先（画面に表示する）。</summary>
    public string LogDirectory => _logDirectory.LogDirectory;

    [RelayCommand]
    private Task OpenLogFolderAsync() => ExecuteSafelyAsync(Strings.OperationOpenLogFolder, () =>
    {
        _folderLauncher.OpenFolder(_logDirectory.LogDirectory);
        return Task.CompletedTask;
    });

    /// <summary>
    /// 「このアプリについて」。アプリ名・バージョン・著作権表記をダイアログで表示します。
    /// </summary>
    [RelayCommand]
    private void ShowAbout() =>
        _messageDialog.ShowMessage(
            Localizer.Format(Strings.AboutTitleFormat, AppInfoProvider.GetProductName()),
            AppInfoProvider.CreateAboutMessage());

    /// <summary>
    /// 「ライセンス情報」。使用しているソフトウェアのライセンス表示のファイルを、既定のアプリで開きます。
    /// </summary>
    [RelayCommand]
    private void ShowThirdPartyNotices()
    {
        string path = AppInfoProvider.ThirdPartyNoticesPath;
        try
        {
            _folderLauncher.OpenFile(path);
        }
        catch (Exception ex)
        {
            LogOperationFailed(ex, Strings.OperationOpenThirdPartyNotices);
            _messageDialog.ShowMessage(
                Strings.ThirdPartyNotices,
                Localizer.Format(Strings.ThirdPartyNoticesOpenFailedFormat, path, ex.Message));
        }
    }

    /// <summary>選択中の画像を表示しない画像から外し、再び壁紙の候補にする。</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedExcludedImage))]
    private Task RestoreExcludedImageAsync() => ExecuteSafelyAsync(Strings.OperationRestoreExcludedImage, async () =>
    {
        if (SelectedExcludedImage is not { } image)
        {
            return;
        }

        await _settingsService.UpdateAsync(s => s.RemoveExcludedImage(image));
        ShowInfo(Localizer.Format(Strings.ImageRestoredFormat, System.IO.Path.GetFileName(image)));
    });

    [RelayCommand(CanExecute = nameof(HasSelectedExcludedImage))]
    private Task ShowExcludedImageInFolderAsync() => ExecuteSafelyAsync(Strings.OperationShowImageInFolder, () =>
    {
        if (SelectedExcludedImage is { } image)
        {
            _folderLauncher.ShowFileInFolder(image);
        }

        return Task.CompletedTask;
    });

    private bool HasSelectedExcludedImage() => SelectedExcludedImage is not null;

    /// <summary>各モニターの画面に、一覧と同じ番号を一定時間表示する。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task IdentifyMonitorsAsync() => ExecuteSafelyAsync(Strings.OperationIdentifyMonitors, async () =>
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
    /// 編集中の一覧にフォルダを追加する。1 枚表示ではモニターのフォルダ、分割表示では選択中のマス（区画）のフォルダ、
    /// スパンではスパンのフォルダが対象。
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedMonitor))]
    private Task AddFolderAsync() => ExecuteSafelyAsync(Strings.OperationAddFolder, async () =>
    {
        if (SelectedMonitor is not { } monitor)
        {
            return;
        }

        if (IsSpanLayout)
        {
            IReadOnlyList<string> spanFolders = _folderPicker.PickFolders(Strings.PickSpanFolders);
            if (spanFolders.Count == 0)
            {
                return;
            }

            bool wasSpanEmpty = SpanFolders.Count == 0;
            await UpdateSpanSettingsAsync(s => spanFolders.Aggregate(s, (current, folder) => current.AddFolder(folder)));
            string addedSpan = Localizer.Format(Strings.SpanFoldersAddedFormat, spanFolders.Count);
            ShowInfo(addedSpan);

            // 初めてフォルダを登録したときは、次の定期実行を待たずに壁紙を反映する
            if (wasSpanEmpty)
            {
                ReportProblems(await _rotationService.NextAsync(monitor.Id), addedSpan);
            }

            return;
        }

        if (IsSplitLayout)
        {
            if (SelectedTile is not { } tile)
            {
                return;
            }

            IReadOnlyList<string> tileFolders = _folderPicker.PickFolders(Localizer.Format(Strings.PickTileFoldersFormat, DescribeTile(tile)));
            if (tileFolders.Count == 0)
            {
                return;
            }

            await UpdateMonitorSettingsAsync(monitor, m => tileFolders.Aggregate(m, (current, folder) => current.AddTileFolder(tile.Index, folder)));
            string added = Localizer.Format(Strings.TileFoldersAddedFormat, DescribeTile(tile), tileFolders.Count);
            ShowInfo(added);

            // 設定したフォルダの画像をすぐに確認できるよう表示し直す
            ReportProblems(await _rotationService.NextAsync(monitor.Id), added);
            return;
        }

        IReadOnlyList<string> folders = _folderPicker.PickFolders(Strings.PickWallpaperFolders);
        if (folders.Count == 0)
        {
            return;
        }

        bool wasEmpty = Folders.Count == 0;
        await UpdateMonitorSettingsAsync(monitor, m => folders.Aggregate(m, (current, folder) => current.AddFolder(folder)));
        string addedFolders = Localizer.Format(Strings.FoldersAddedFormat, folders.Count);
        ShowInfo(addedFolders);

        // 初めてフォルダを登録したモニターは、次の定期実行を待たずに壁紙を反映する
        if (wasEmpty)
        {
            ReportProblems(await _rotationService.NextAsync(monitor.Id), addedFolders);
        }
    });

    /// <summary>
    /// 編集中の一覧からフォルダを削除する。1 枚表示ではモニターのフォルダ、分割表示では選択中のマス（区画）のフォルダ、
    /// スパンではスパンのフォルダが対象。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveFolder))]
    private Task RemoveFolderAsync() => ExecuteSafelyAsync(Strings.OperationRemoveFolder, async () =>
    {
        if (SelectedMonitor is not { } monitor || SelectedFolder is not { } folder)
        {
            return;
        }

        if (IsSpanLayout)
        {
            await UpdateSpanSettingsAsync(s => s.RemoveFolder(folder));
            ShowInfo(Strings.SpanFolderRemoved);
            return;
        }

        if (IsSplitLayout)
        {
            if (SelectedTile is not { } tile)
            {
                return;
            }

            await UpdateMonitorSettingsAsync(monitor, m => m.RemoveTileFolder(tile.Index, folder));
            string removed = (tile.HasOwnFolders, AnyTileHasFolders) switch
            {
                (true, _) => Localizer.Format(Strings.TileFolderRemovedFormat, DescribeTile(tile)),
                (false, true) => Localizer.Format(Strings.TileBecameBlankFormat, DescribeTile(tile)),
                _ => Localizer.Format(Strings.AllTilesUnsetFormat, TileUnit, Layout.GetTileCount()),
            };
            ShowInfo(removed);

            // マスの表示内容が変わるため、すぐに表示し直す
            if (_settingsService.Current.HasFoldersFor(monitor.Id))
            {
                ReportProblems(await _rotationService.NextAsync(monitor.Id), removed);
            }

            return;
        }

        await UpdateMonitorSettingsAsync(monitor, m => m.RemoveFolder(folder));
        ShowInfo(Strings.FolderRemoved);
    });

    private bool CanRemoveFolder() => SelectedMonitor is not null && SelectedFolder is not null;

    [RelayCommand(CanExecute = nameof(CanChangeSelectedMonitor))]
    private Task ChangeSelectedMonitorNowAsync() => ExecuteSafelyAsync(Strings.OperationChangeWallpaper, async () =>
    {
        if (SelectedMonitor is { } monitor && !ReportProblems(await _rotationService.NextAsync(monitor.Id)))
        {
            ShowInfo(Localizer.Format(Strings.WallpaperChangedForFormat, DescribeTarget(monitor)));
        }
    });

    [RelayCommand(CanExecute = nameof(CanChangeSelectedMonitor))]
    private Task PreviousSelectedMonitorAsync() => ExecuteSafelyAsync(Strings.OperationPreviousWallpaper, async () =>
    {
        if (SelectedMonitor is { } monitor && !ReportProblems(await _rotationService.PreviousAsync(monitor.Id)))
        {
            ShowInfo(Localizer.Format(Strings.WallpaperRevertedForFormat, DescribeTarget(monitor)));
        }
    });

    private bool CanChangeSelectedMonitor() => HasSelectedMonitor && !IsChangingWallpaper;

    /// <summary>コピー元のモニターの設定（フォルダ・表示レイアウト・選び方など）を、選択中のモニターにコピーして表示し直す。</summary>
    [RelayCommand(CanExecute = nameof(CanCopySettings))]
    private Task CopySettingsAsync() => ExecuteSafelyAsync(Strings.OperationCopySettings, async () =>
    {
        if (SelectedMonitor is not { } monitor || SelectedCopySource is not { } source)
        {
            return;
        }

        WallpaperLayout oldLayout = _settingsService.Current.GetMonitor(monitor.Id).Layout;
        MonitorSettings from = _settingsService.Current.GetMonitor(source.MonitorId);
        await UpdateMonitorSettingsAsync(monitor, m => m.WithSettingsFrom(from));
        string copied = Localizer.Format(Strings.SettingsCopiedFormat, source.Name, monitor.DisplayName);
        ShowInfo(copied);

        // コピーした設定の画像を、次の定期実行を待たずに表示する
        await RedisplayAfterLayoutChangeAsync(monitor, oldLayout, copied, redisplayAlways: true);
    });

    private bool CanCopySettings() => CanChangeSelectedMonitor() && SelectedCopySource is not null;

    private bool CanChangeWallpaper() => !IsChangingWallpaper;

    /// <summary>
    /// 壁紙フォルダを読み込み直して（列挙結果のキャッシュを使わずに）、全モニターの壁紙を選び直す。トレイの「全壁紙を更新」と同じ。
    /// 変更を監視できないネットワーク共有のフォルダでも、追加した画像をすぐに候補に入れられるようにする。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChangeWallpaper))]
    private Task RefreshAllWallpapersAsync() => ExecuteSafelyAsync(Strings.OperationRefreshAllWallpapers, async () =>
    {
        WallpaperChangeResult result = await _scheduler.RefreshNowAsync();
        if (!ReportProblems(result))
        {
            ShowInfo(Strings.AllWallpapersRefreshed);
        }
    });

    [RelayCommand]
    private Task TogglePauseAsync() => ExecuteSafelyAsync(Strings.OperationTogglePause, async () =>
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
        _ = ExecuteSafelyAsync(Strings.OperationRefreshPreview, RefreshPreviewAsync);
    }

    partial void OnIncludeSubfoldersChanged(bool value)
    {
        if (!_isLoading && SelectedMonitor is { } monitor)
        {
            _ = ExecuteSafelyAsync(Strings.OperationSaveIncludeSubfolders, () => IsSpanMonitor(monitor.Id)
                ? UpdateSpanSettingsAsync(s => s with { IncludeSubfolders = value })
                : UpdateMonitorSettingsAsync(monitor, m => m with { IncludeSubfolders = value }));
        }
    }

    partial void OnSelectionModeChanged(ImageSelectionMode value)
    {
        if (!_isLoading && SelectedMonitor is { } monitor)
        {
            _ = ExecuteSafelyAsync(Strings.OperationSaveSelectionMode, () => IsSpanMonitor(monitor.Id)
                ? UpdateSpanSettingsAsync(s => s with { SelectionMode = value })
                : UpdateMonitorSettingsAsync(monitor, m => m with { SelectionMode = value }));
        }
    }

    partial void OnLayoutChanged(WallpaperLayout oldValue, WallpaperLayout newValue)
    {
        // 表示する一覧（モニター／マス／スパン）が切り替わるため、前の一覧での選択は解除する
        SelectedFolder = null;

        if (_isLoading || SelectedMonitor is not { } monitor)
        {
            return;
        }

        _ = ExecuteSafelyAsync(Strings.OperationChangeLayout, async () =>
        {
            await UpdateMonitorSettingsAsync(monitor, m => m with { Layout = newValue });
            string switched = newValue switch
            {
                WallpaperLayout.Grid2x2 => Strings.LayoutChangedGrid2x2,
                WallpaperLayout.Grid4x4 => Strings.LayoutChangedGrid4x4,
                WallpaperLayout.Span => Localizer.Format(Strings.LayoutChangedSpanFormat, SpanMonitorCount),
                _ => Strings.LayoutChangedSingle,
            };
            ShowInfo(switched);

            // 次の定期実行を待たず、新しいレイアウトで表示し直す
            await RedisplayAfterLayoutChangeAsync(monitor, oldValue, switched, redisplayAlways: false);
        });
    }

    /// <summary>
    /// 表示レイアウトが変わったモニターを表示し直す。
    /// スパンに加わった場合は今のスパンの画像のまま作り直し、スパンから外れた場合は残りのスパンのモニターも作り直す。
    /// </summary>
    /// <param name="redisplayAlways">スパンに加わったままの場合（設定のコピーなど）も表示し直すか。</param>
    private async Task RedisplayAfterLayoutChangeAsync(
        MonitorItemViewModel monitor, WallpaperLayout oldLayout, string precedingMessage, bool redisplayAlways)
    {
        AppSettings settings = _settingsService.Current;
        WallpaperLayout newLayout = settings.GetMonitor(monitor.Id).Layout;
        if (settings.HasFoldersFor(monitor.Id) && (redisplayAlways || newLayout != oldLayout))
        {
            // スパンに加わったときは、ほかのスパンのモニターの画像を変えないよう、同じ画像で作り直す
            ReportProblems(
                newLayout == WallpaperLayout.Span
                    ? await _rotationService.ReapplyAsync(monitor.Id)
                    : await _rotationService.NextAsync(monitor.Id),
                precedingMessage);
        }

        // スパンから外れたモニターの分、残りのスパンのモニターに映る範囲が変わる
        if (oldLayout == WallpaperLayout.Span && newLayout != WallpaperLayout.Span && settings.Span.Folders.Count > 0
            && Monitors.FirstOrDefault(m => m.Id != monitor.Id && IsSpanMonitor(m.Id)) is { } remaining)
        {
            ReportProblems(await _rotationService.ReapplyAsync(remaining.Id), precedingMessage);
        }
    }

    partial void OnSelectedSingleImageFitChanged(SingleImageFitOption value)
    {
        if (value is not null)
        {
            ChangeAppearance(
                Strings.OperationChangeSingleImageFit, m => m with { SingleImageFit = value.Fit },
                Localizer.Format(Strings.SingleImageFitChangedFormat, value.DisplayName), l => l == WallpaperLayout.SingleImage);
        }
    }

    partial void OnTileFitChanged(ImageFit value) => ChangeAppearance(
        Strings.OperationChangeTileFit, m => m with { TileStyle = m.TileStyle with { Fit = value } },
        value == ImageFit.Fit ? Strings.TileFitChangedFit : Strings.TileFitChangedFill,
        l => l.IsSplit());

    partial void OnTileGapChanged(int value) => ChangeAppearance(
        Strings.OperationChangeTileGap, m => m with { TileStyle = m.TileStyle with { Gap = Math.Clamp(value, 0, TileStyle.MaxGap) } },
        Localizer.Format(Strings.TileGapChangedFormat, value), l => l.IsSplit());

    partial void OnSelectedTileGapColorChanged(TileColorOption? value)
    {
        if (value is not null)
        {
            ChangeAppearance(
                Strings.OperationChangeGapColor, m => m with { TileStyle = m.TileStyle with { GapColor = value.Rgb } },
                Localizer.Format(Strings.GapColorChangedFormat, value.DisplayName), l => l.IsSplit());
        }
    }

    partial void OnSelectedEmptyTileColorChanged(TileColorOption? value)
    {
        if (value is not null)
        {
            ChangeAppearance(
                Strings.OperationChangeEmptyColor, m => m with { TileStyle = m.TileStyle with { EmptyColor = value.Rgb } },
                Localizer.Format(Strings.EmptyColorChangedFormat, value.DisplayName), l => l.IsSplit());
        }
    }

    partial void OnSpanFitChanged(ImageFit value)
    {
        if (_isLoading || SelectedMonitor is not { } monitor)
        {
            return;
        }

        _ = ExecuteSafelyAsync(Strings.OperationChangeSpanFit, async () =>
        {
            await UpdateSpanSettingsAsync(s => s with { Fit = value });
            string changed = value == ImageFit.Fit
                ? Strings.SpanFitChangedFit
                : Strings.SpanFitChangedFill;
            ShowInfo(changed);

            // 今の画像のまま、新しい収め方で作り直す
            if (IsSpanMonitor(monitor.Id) && _settingsService.Current.HasFoldersFor(monitor.Id))
            {
                ReportProblems(await _rotationService.ReapplyAsync(monitor.Id), changed);
            }
        });
    }

    /// <summary>
    /// 表示の見た目（1 枚表示の表示方法、分割表示の収め方・間隔・色）の変更を保存し、今の画像のまま表示し直す。
    /// </summary>
    /// <param name="isTargetLayout">変える設定を使うレイアウトか。今のレイアウトで使う設定のときだけ表示し直す。</param>
    private void ChangeAppearance(
        string operation, Func<MonitorSettings, MonitorSettings> update, string message, Func<WallpaperLayout, bool> isTargetLayout)
    {
        if (_isLoading || SelectedMonitor is not { } monitor)
        {
            return;
        }

        _ = ExecuteSafelyAsync(operation, async () =>
        {
            await UpdateMonitorSettingsAsync(monitor, update);
            ShowInfo(message);

            // 次の定期実行を待たず、今の画像を新しい見た目で表示し直す
            AppSettings settings = _settingsService.Current;
            if (isTargetLayout(settings.GetMonitor(monitor.Id).Layout) && settings.HasFoldersFor(monitor.Id))
            {
                ReportProblems(await _rotationService.ReapplyAsync(monitor.Id), message);
            }
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
                ShowInfo(Strings.StartupEnabled);
            }
            else
            {
                _startupRegistration.Disable();
                ShowInfo(Strings.StartupDisabled);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException
                                       or InvalidOperationException)
        {
            LogOperationFailed(ex, Strings.OperationStartupRegistration);
            ShowError(Localizer.Format(Strings.StartupRegistrationFailedFormat, ErrorMessages.Describe(ex)));

            // チェックの表示を実際の登録状態に戻す
            LoadStartupState();
        }
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (_isLoading || value is null)
        {
            return;
        }

        _ = ExecuteSafelyAsync(Strings.OperationSaveLanguage, async () =>
        {
            await _settingsService.UpdateAsync(s => s with { Language = value.Language });

            // 選んだ言語が読める人に伝わるよう、変更した先の言語で案内する
            string format = value.Language == Localizer.CurrentLanguage
                ? Localizer.Get(nameof(Strings.LanguageChangedBackFormat), value.Language)
                : Localizer.Get(nameof(Strings.LanguageChangedRestartFormat), value.Language);
            ShowInfo(Localizer.Format(format, value.DisplayName));
        });
    }

    partial void OnSkipWhenFullScreenChanged(bool value) => UpdateSkipConditions(
        c => c with { WhenFullScreen = value },
        value ? Strings.SkipWhenFullScreenEnabled : Strings.SkipWhenFullScreenDisabled);

    partial void OnSkipOnBatteryChanged(bool value) => UpdateSkipConditions(
        c => c with { OnBattery = value },
        value ? Strings.SkipOnBatteryEnabled : Strings.SkipOnBatteryDisabled);

    private void UpdateSkipConditions(Func<SkipConditions, SkipConditions> update, string message)
    {
        if (_isLoading)
        {
            return;
        }

        _ = ExecuteSafelyAsync(Strings.OperationSaveSkipConditions, async () =>
        {
            await _settingsService.UpdateAsync(s => s with { SkipConditions = update(s.SkipConditions) });
            ShowInfo(message);
        });
    }

    partial void OnIntervalValueChanged(int value) => ApplyInterval();

    partial void OnSelectedIntervalUnitChanged(IntervalUnitOption value) => ApplyInterval();

    private void ApplyInterval()
    {
        if (_isLoading || SelectedIntervalUnit is null)
        {
            return;
        }

        // int のまま掛けると大きな値で桁あふれし、範囲内の値に化けることがあるため long で計算する
        long totalMinutes = (long)IntervalValue * SelectedIntervalUnit.MinutesPerUnit;
        if (totalMinutes is < AppSettings.MinIntervalMinutes or > AppSettings.MaxIntervalMinutes)
        {
            IntervalError = Strings.IntervalOutOfRange;
            return;
        }

        int minutes = (int)totalMinutes;

        IntervalError = null;
        if (minutes == _settingsService.Current.IntervalMinutes)
        {
            return;
        }

        _ = ExecuteSafelyAsync(Strings.OperationSaveInterval, async () =>
        {
            await _settingsService.UpdateAsync(s => s.WithInterval(minutes));
            OnPropertyChanged(nameof(ScheduleStatusText));
            ShowInfo(Localizer.Format(Strings.IntervalChangedFormat, IntervalUnitOption.Format(TimeSpan.FromMinutes(minutes))));
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
            SkipWhenFullScreen = _settingsService.Current.SkipConditions.WhenFullScreen;
            SkipOnBattery = _settingsService.Current.SkipConditions.OnBattery;
            SelectedLanguage = LanguageOption.From(_settingsService.Current.Language);
        }
        finally
        {
            _isLoading = false;
        }

        LoadStartupState();
        LoadExcludedImages();
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
            LogOperationFailed(ex, Strings.OperationStartupRegistration);
            ShowError(Localizer.Format(Strings.StartupStateUnavailableFormat, ErrorMessages.Describe(ex)));
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

    private void LoadExcludedImages()
    {
        IReadOnlyList<string> images = _settingsService.Current.ExcludedImages;
        if (ExcludedImages.SequenceEqual(images))
        {
            return;
        }

        string? selected = SelectedExcludedImage;
        ExcludedImages.Clear();
        foreach (string image in images)
        {
            ExcludedImages.Add(image);
        }

        // 外した画像の選択は解除し、残っている画像の選択は保つ
        SelectedExcludedImage = selected is not null && ExcludedImages.Contains(selected) ? selected : null;
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
            SpanSettings span = _settingsService.Current.Span;
            if (!SpanFolders.SequenceEqual(span.Folders))
            {
                SpanFolders.Clear();
                foreach (string folder in span.Folders)
                {
                    SpanFolders.Add(folder);
                }
            }

            SpanFit = span.Fit;
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

            // 1 つでもマスに専用フォルダがあれば、未設定のマスは空きマスとして表示される
            bool anyTileHasFolders = Tiles.Any(t => settings.GetTile(t.Index).Folders.Count > 0);
            foreach (TileItemViewModel tile in Tiles)
            {
                tile.SetFolders(settings.GetTile(tile.Index).Folders, isBlankWhenEmpty: anyTileHasFolders);
            }

            // スパンでは、サブフォルダの検索と選び方もスパンで共通の設定を表示する
            bool isSpan = settings.Layout == WallpaperLayout.Span;
            IncludeSubfolders = isSpan ? span.IncludeSubfolders : settings.IncludeSubfolders;
            SelectionMode = isSpan ? span.SelectionMode : settings.SelectionMode;
            Layout = settings.Layout;
            SelectedSingleImageFit = SingleImageFitOption.From(settings.SingleImageFit);
            TileStyle style = settings.TileStyle;
            TileFit = style.Fit;
            TileGap = style.Gap;
            TileGapColors = TileColorOption.OptionsFor(style.GapColor);
            SelectedTileGapColor = TileGapColors.First(c => c.Rgb == style.GapColor);
            EmptyTileColors = TileColorOption.OptionsFor(style.EmptyColor);
            SelectedEmptyTileColor = EmptyTileColors.First(c => c.Rgb == style.EmptyColor);
        }
        finally
        {
            _isLoading = false;
            OnPropertyChanged(nameof(EmptyFoldersMessage));
            OnPropertyChanged(nameof(FolderCaption));
            LoadCopySources();
        }
    }

    private void LoadCopySources()
    {
        string? selectedId = SelectedCopySource?.MonitorId;
        CopySources.Clear();
        if (SelectedMonitor is { } target)
        {
            AppSettings settings = _settingsService.Current;
            foreach (MonitorItemViewModel monitor in Monitors.Where(m => !ReferenceEquals(m, target)))
            {
                if (settings.GetMonitor(monitor.Id).HasFolderSettings)
                {
                    CopySources.Add(new MonitorCopySourceViewModel(monitor.Id, $"{monitor.Number}. {monitor.DisplayName}", IsConnected: true));
                }
            }

            foreach (MonitorSettings saved in settings.Monitors.Where(s => s.HasFolderSettings && !IsConnected(s.MonitorId)))
            {
                string name = saved.DisplayName ?? MonitorHardwareId.FromMonitorId(saved.MonitorId) ?? Strings.UnknownMonitorName;
                CopySources.Add(new MonitorCopySourceViewModel(saved.MonitorId, name, IsConnected: false));
            }
        }

        SelectedCopySource = CopySources.FirstOrDefault(c => string.Equals(c.MonitorId, selectedId, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsConnected(string monitorId) =>
        Monitors.Any(m => string.Equals(m.Id, monitorId, StringComparison.OrdinalIgnoreCase));

    /// <summary>スパンの設定（スパンを選んだモニターで共通）を変えて、画面に反映する。</summary>
    private async Task UpdateSpanSettingsAsync(Func<SpanSettings, SpanSettings> update)
    {
        await _settingsService.UpdateAsync(s => s with { Span = update(s.Span) });
        LoadMonitorSettings(SelectedMonitor);
    }

    /// <summary>表示レイアウトにスパンを選んでいるモニターか（保存した設定で判定する）。</summary>
    private bool IsSpanMonitor(string monitorId) =>
        _settingsService.Current.GetMonitor(monitorId).Layout == WallpaperLayout.Span;

    /// <summary>切り替えの結果の文言で使う、切り替えた対象の呼び方。スパンでは参加するモニターすべてが変わる。</summary>
    private string DescribeTarget(MonitorItemViewModel monitor) =>
        IsSpanMonitor(monitor.Id) ? Strings.SpanTargetName : monitor.DisplayName;

    private async Task UpdateMonitorSettingsAsync(MonitorItemViewModel monitor, Func<MonitorSettings, MonitorSettings> update)
    {
        // 接続していないときにも設定画面でどのモニターか分かるよう、名前も記録する
        await _settingsService.UpdateAsync(s => s.WithMonitor(update(s.GetMonitor(monitor.Id)) with { DisplayName = monitor.DisplayName }));

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

        CurrentWallpaperPath = path ?? Strings.SolidColorBackground;
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
            ShowError(Localizer.Format(Strings.OperationFailed, operation, ErrorMessages.Describe(ex)));
        }
    }

    /// <summary>
    /// 切り替えで期待どおりに変更されなかったモニターがあれば、その理由をエラーとして表示する。
    /// </summary>
    /// <param name="precedingMessage">直前に表示した操作結果（理由の前に続けて表示する）。</param>
    /// <returns>理由を表示した場合は true。</returns>
    private bool ReportProblems(WallpaperChangeResult result, string? precedingMessage = null)
    {
        if (WallpaperChangeMessages.DescribeProblems(result) is not { } problem)
        {
            return false;
        }

        ShowError(precedingMessage is null ? problem : precedingMessage + Strings.ProblemSentenceSeparator + problem);
        return true;
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
                _ = ExecuteSafelyAsync(Strings.OperationRefreshPreview, RefreshPreviewAsync);
            }
        });

    private async void OnProgressChanged(object? sender, WallpaperChangeProgressEventArgs e) =>
        await _uiDispatcher.InvokeAsync(() =>
        {
            if (!_disposed)
            {
                ChangeProgressText = WallpaperChangeMessages.DescribeProgress(e);
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

            _ = ExecuteSafelyAsync(Strings.OperationUpdateMonitors, async () =>
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
            parts.Add(Localizer.Format(Strings.MonitorsAddedFormat, JoinNames(e.Added.Select(m => m.DisplayName))));
        }

        if (e.Inherited.Count > 0)
        {
            parts.Add(Localizer.Format(Strings.MonitorsInheritedFormat, JoinNames(e.Inherited.Select(m => m.DisplayName))));
        }

        if (e.Removed.Count > 0)
        {
            parts.Add(Localizer.Format(Strings.MonitorsRemovedFormat, JoinNames(e.Removed.Select(m => m.DisplayName))));
        }

        if (e.Resized.Count > 0)
        {
            parts.Add(Localizer.Format(Strings.MonitorsResizedFormat, JoinNames(e.Resized.Select(
                m => Localizer.Format(Strings.MonitorWithResolutionFormat, m.DisplayName, m.ResolutionText)))));
        }

        return parts.Count > 0
            ? Localizer.Format(Strings.MonitorConfigurationChangedFormat, string.Join(Strings.ReasonSeparator, parts))
            : Strings.MonitorPositionsChanged;
    }

    private static string JoinNames(IEnumerable<string> names) => string.Join(Strings.ListSeparator, names);

    /// <summary>トレイのメニューから表示しない画像を加えた場合や、接続先を変えたモニターに設定を引き継いだ場合も、一覧に反映する。</summary>
    private async void OnSettingsChanged(object? sender, AppSettings settings) =>
        await _uiDispatcher.InvokeAsync(() =>
        {
            if (!_disposed)
            {
                LoadExcludedImages();
                LoadCopySources();
            }
        });

    private async void OnSchedulerStateChanged(object? sender, EventArgs e) =>
        await _uiDispatcher.InvokeAsync(() =>
        {
            IsPaused = _scheduler.IsPaused;
            OnPropertyChanged(nameof(ScheduleStatusText));
        });

    [LoggerMessage(Level = LogLevel.Error, Message = "{Operation}に失敗しました")]
    private partial void LogOperationFailed(Exception exception, string operation);
}
