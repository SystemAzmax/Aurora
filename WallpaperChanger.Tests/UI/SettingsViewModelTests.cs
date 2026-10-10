using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Tests.Fakes;
using WallpaperChanger.UI.Services;
using WallpaperChanger.UI.ViewModels;

namespace WallpaperChanger.Tests.UI;

public sealed class SettingsViewModelTests : IDisposable
{
    private static readonly MonitorInfo Left = new("MON-L", 1, "Left", new MonitorBounds(-1920, 0, 0, 1080), 1920, 1080);
    private static readonly MonitorInfo Primary = new("MON-P", 0, "Primary", new MonitorBounds(0, 0, 2560, 1440), 2560, 1440);

    private readonly IMonitorService _monitors = Substitute.For<IMonitorService>();
    private readonly IWallpaperService _wallpaper = Substitute.For<IWallpaperService>();
    private readonly IWallpaperRotationService _rotation = Substitute.For<IWallpaperRotationService>();
    private readonly IWallpaperScheduler _scheduler = Substitute.For<IWallpaperScheduler>();
    private readonly IFolderPickerService _folderPicker = Substitute.For<IFolderPickerService>();
    private readonly IImagePreviewLoader _previewLoader = Substitute.For<IImagePreviewLoader>();
    private readonly IMonitorIdentifierService _identifier = Substitute.For<IMonitorIdentifierService>();
    private readonly IStartupRegistrationService _startup = Substitute.For<IStartupRegistrationService>();
    private readonly IMonitorConfigurationWatcher _watcher = Substitute.For<IMonitorConfigurationWatcher>();
    private readonly ILogDirectoryProvider _logDirectory = Substitute.For<ILogDirectoryProvider>();
    private readonly IFolderLauncher _folderLauncher = Substitute.For<IFolderLauncher>();
    private readonly InMemorySettingsService _settings;
    private readonly SettingsViewModel _sut;

    public SettingsViewModelTests()
    {
        _monitors.GetMonitorsAsync(Arg.Any<CancellationToken>()).Returns([Left, Primary]);
        _scheduler.Interval.Returns(TimeSpan.FromMinutes(120));

        _settings = new InMemorySettingsService(new AppSettings { IntervalMinutes = 120 }
            .WithMonitor(MonitorSettings.CreateDefault("MON-P").AddFolder(@"C:\Wallpapers") with
            {
                IncludeSubfolders = false,
                SelectionMode = ImageSelectionMode.Sequential,
            }));

        _sut = new SettingsViewModel(
            _monitors, _settings, _wallpaper, _rotation, _scheduler, _folderPicker, _previewLoader, _identifier, _startup, _watcher,
            _logDirectory, _folderLauncher,
            new InlineUiDispatcher(), NullLogger<SettingsViewModel>.Instance);
    }

    public void Dispose() => _sut.Dispose();

    [Fact]
    public async Task 初期化でモニター一覧と設定を読み込みメインモニターを選択する()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(["Left", "Primary"], _sut.Monitors.Select(m => m.DisplayName));
        Assert.Equal([1, 2], _sut.Monitors.Select(m => m.Number));
        Assert.Equal("MON-P", _sut.SelectedMonitor?.Id);
        Assert.Equal([@"C:\Wallpapers"], _sut.Folders);
        Assert.False(_sut.IncludeSubfolders);
        Assert.True(_sut.IsSequentialSelection);
        Assert.Equal(2, _sut.IntervalValue);
        Assert.Equal(IntervalUnitOption.Hours, _sut.SelectedIntervalUnit);
        Assert.Equal(0, _settings.UpdateCount);
    }

    [Fact]
    public async Task モニターを切り替えるとそのモニターの設定を表示する()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);

        _sut.SelectedMonitor = _sut.Monitors[0];

        Assert.Empty(_sut.Folders);
        Assert.True(_sut.IncludeSubfolders);
        Assert.True(_sut.IsRandomSelection);
        Assert.Equal(0, _settings.UpdateCount);
    }

    [Fact]
    public async Task フォルダ追加で保存し初回登録時は壁紙を即時変更する()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);
        _sut.SelectedMonitor = _sut.Monitors[0];
        _folderPicker.PickFolders(Arg.Any<string>()).Returns([@"D:\Photos"]);

        await _sut.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal([@"D:\Photos"], _settings.Current.GetMonitor("MON-L").Folders);
        Assert.Equal([@"D:\Photos"], _sut.Folders);
        await _rotation.Received(1).NextAsync("MON-L", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task フォルダ削除で保存する()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);
        _sut.SelectedFolder = @"C:\Wallpapers";

        await _sut.RemoveFolderCommand.ExecuteAsync(null);

        Assert.Empty(_settings.Current.GetMonitor("MON-P").Folders);
        Assert.Empty(_sut.Folders);
    }

    [Fact]
    public async Task 選択方法とサブフォルダ設定の変更を保存する()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);

        _sut.IsRandomSelection = true;
        _sut.IncludeSubfolders = true;

        MonitorSettings saved = _settings.Current.GetMonitor("MON-P");
        Assert.Equal(ImageSelectionMode.Random, saved.SelectionMode);
        Assert.True(saved.IncludeSubfolders);
    }

    [Fact]
    public async Task 切り替え間隔を保存し範囲外はエラーを表示する()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);

        _sut.SelectedIntervalUnit = IntervalUnitOption.Minutes;
        _sut.IntervalValue = 15;
        Assert.Equal(15, _settings.Current.IntervalMinutes);
        Assert.False(_sut.HasIntervalError);

        _sut.SelectedIntervalUnit = IntervalUnitOption.Hours;
        Assert.Equal(15 * 60, _settings.Current.IntervalMinutes);

        _sut.IntervalValue = 25;
        Assert.True(_sut.HasIntervalError);
        Assert.Equal(15 * 60, _settings.Current.IntervalMinutes);

        _sut.IntervalValue = 24;
        Assert.False(_sut.HasIntervalError);
        Assert.Equal(AppSettings.MaxIntervalMinutes, _settings.Current.IntervalMinutes);
    }

    [Theory]
    [InlineData(71_582_789)] // × 60 が int で桁あふれすると 44 分になる値
    [InlineData(int.MaxValue)]
    [InlineData(-71_582_788)] // × 60 が int で桁あふれすると正の値になる値
    [InlineData(0)]
    public async Task 時間単位で大きすぎる値は桁あふれせず範囲外のエラーになる(int hours)
    {
        await _sut.InitializeCommand.ExecuteAsync(null);
        _sut.SelectedIntervalUnit = IntervalUnitOption.Hours;

        _sut.IntervalValue = hours;

        Assert.True(_sut.HasIntervalError);
        Assert.Equal(120, _settings.Current.IntervalMinutes);
    }

    [Fact]
    public async Task 操作の失敗は画面下部にエラーとして表示する()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);
        _rotation.NextAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("ファイルにアクセスできません")));

        await _sut.ChangeSelectedMonitorNowCommand.ExecuteAsync(null);

        Assert.True(_sut.IsStatusError);
        Assert.Contains("ファイルにアクセスできません", _sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 表示レイアウトを切り替えると保存して即時に表示し直す()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);
        Assert.True(_sut.IsSingleImageLayout);

        _sut.IsGridLayout = true;

        Assert.Equal(WallpaperLayout.Grid2x2, _settings.Current.GetMonitor("MON-P").Layout);
        await _rotation.Received(1).NextAsync("MON-P", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 十六分割に切り替えると4分割と共通の区画のフォルダを編集対象にする()
    {
        await _settings.UpdateAsync(s => s.WithMonitor(s.GetMonitor("MON-P").AddTileFolder(2, @"D:\Birds")),
            TestContext.Current.CancellationToken);
        await _sut.InitializeCommand.ExecuteAsync(null);
        _rotation.ClearReceivedCalls();

        _sut.IsGrid4x4Layout = true;

        Assert.Equal(WallpaperLayout.Grid4x4, _settings.Current.GetMonitor("MON-P").Layout);
        Assert.False(_sut.IsGridLayout);
        Assert.True(_sut.IsSplitLayout);
        Assert.Same(_sut.Tiles[0].Folders, _sut.EditingFolders);
        Assert.Equal("左上 の区画のフォルダ", _sut.EditingTargetText);
        Assert.Equal("未設定（この区画は黒で表示）", _sut.EmptyFoldersMessage);
        Assert.Equal("16 分割表示に切り替えました。", _sut.StatusMessage);
        await _rotation.Received(1).NextAsync("MON-P", Arg.Any<CancellationToken>());

        _sut.SelectedTile = _sut.Tiles[2];
        Assert.Equal([@"D:\Birds"], _sut.EditingFolders);
    }

    [Fact]
    public async Task 十六分割では追加ボタンで選択中の区画にフォルダを追加する()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);
        _sut.IsGrid4x4Layout = true;
        _rotation.ClearReceivedCalls();
        _sut.SelectedTile = _sut.Tiles[1];
        _folderPicker.PickFolders(Arg.Any<string>()).Returns([@"D:\Cats"]);

        await _sut.AddFolderCommand.ExecuteAsync(null);

        MonitorSettings saved = _settings.Current.GetMonitor("MON-P");
        Assert.Equal([@"D:\Cats"], saved.GetTile(1).Folders);
        Assert.Equal([@"D:\Cats"], saved.GetEffectiveTileFolders(2));
        Assert.Equal("右上の区画に 1 件のフォルダを追加しました。", _sut.StatusMessage);
        _folderPicker.Received(1).PickFolders(Arg.Is<string>(t => t.Contains("右上の区画", StringComparison.Ordinal)));
        await _rotation.Received(1).NextAsync("MON-P", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 十六分割で全区画が未設定になると1枚表示のフォルダから16枚を選ぶと案内する()
    {
        await _settings.UpdateAsync(s => s.WithMonitor(s.GetMonitor("MON-P").AddTileFolder(0, @"D:\A") with
        {
            Layout = WallpaperLayout.Grid4x4,
        }), TestContext.Current.CancellationToken);
        await _sut.InitializeCommand.ExecuteAsync(null);
        _sut.SelectedTile = _sut.Tiles[0];
        _sut.SelectedFolder = @"D:\A";

        await _sut.RemoveFolderCommand.ExecuteAsync(null);

        Assert.Equal("すべての区画が未設定になったため、1 枚表示のフォルダから 16 枚を選びます。", _sut.StatusMessage);
    }

    [Fact]
    public async Task 保存済みの表示レイアウトを読み込む()
    {
        await _settings.UpdateAsync(s => s.WithMonitor(s.GetMonitor("MON-L") with { Layout = WallpaperLayout.Grid2x2 }),
            TestContext.Current.CancellationToken);
        await _sut.InitializeCommand.ExecuteAsync(null);

        _sut.SelectedMonitor = _sut.Monitors[0];

        Assert.True(_sut.IsGridLayout);
        await _rotation.DidNotReceive().NextAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 編集中のフォルダ一覧は表示レイアウトと選択中のマスで切り替わる()
    {
        await _settings.UpdateAsync(s => s.WithMonitor(s.GetMonitor("MON-P").AddTileFolder(2, @"D:\Birds")),
            TestContext.Current.CancellationToken);
        await _sut.InitializeCommand.ExecuteAsync(null);

        Assert.Same(_sut.Folders, _sut.EditingFolders);
        Assert.Null(_sut.EditingTargetText);

        _sut.IsGridLayout = true;
        Assert.Same(_sut.Tiles[0].Folders, _sut.EditingFolders);
        Assert.Equal("左上 のマスのフォルダ", _sut.EditingTargetText);

        _sut.SelectedTile = _sut.Tiles[2];
        Assert.Equal([@"D:\Birds"], _sut.EditingFolders);

        _sut.IsSingleImageLayout = true;
        Assert.Equal([@"C:\Wallpapers"], _sut.EditingFolders);
    }

    [Fact]
    public async Task 四分割では追加ボタンで選択中のマスにフォルダを追加し表示し直す()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);
        _sut.IsGridLayout = true;
        _rotation.ClearReceivedCalls();
        _sut.SelectedTile = _sut.Tiles[3];
        _folderPicker.PickFolders(Arg.Any<string>()).Returns([@"D:\Cats"]);

        await _sut.AddFolderCommand.ExecuteAsync(null);

        MonitorSettings saved = _settings.Current.GetMonitor("MON-P");
        Assert.Equal([@"D:\Cats"], saved.GetTile(3).Folders);
        Assert.Equal([@"C:\Wallpapers"], saved.Folders);
        Assert.Equal([@"D:\Cats"], _sut.EditingFolders);
        Assert.Equal("Cats", _sut.Tiles[3].Summary);
        // 1 つのマスに専用フォルダができたので、他の未設定のマスは黒で表示される
        Assert.Equal("未設定（黒）", _sut.Tiles[0].Summary);
        Assert.True(_sut.Tiles[0].IsBlank);
        Assert.Same(_sut.Tiles[3], _sut.SelectedTile);
        _folderPicker.Received(1).PickFolders(Arg.Is<string>(t => t.Contains("右下", StringComparison.Ordinal)));
        await _rotation.Received(1).NextAsync("MON-P", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 四分割では削除ボタンで選択中のマスのフォルダを削除する()
    {
        await _settings.UpdateAsync(s => s.WithMonitor(s.GetMonitor("MON-P").AddTileFolder(1, @"D:\Dogs") with
        {
            Layout = WallpaperLayout.Grid2x2,
        }), TestContext.Current.CancellationToken);
        await _sut.InitializeCommand.ExecuteAsync(null);
        _sut.SelectedTile = _sut.Tiles[1];
        _sut.SelectedFolder = @"D:\Dogs";

        await _sut.RemoveFolderCommand.ExecuteAsync(null);

        MonitorSettings saved = _settings.Current.GetMonitor("MON-P");
        Assert.Empty(saved.GetTile(1).Folders);
        Assert.Equal([@"C:\Wallpapers"], saved.Folders);
        Assert.False(_sut.Tiles[1].HasOwnFolders);
        Assert.Contains("1 枚表示のフォルダ", _sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 未設定のマスの案内は他のマスの設定有無で変わる()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);
        _sut.IsGridLayout = true;

        Assert.Equal("未設定（1 枚表示のフォルダから選ぶ）", _sut.EmptyFoldersMessage);
        Assert.All(_sut.Tiles, t => Assert.Equal("未設定", t.Summary));

        _sut.SelectedTile = _sut.Tiles[0];
        _folderPicker.PickFolders(Arg.Any<string>()).Returns([@"D:\Cats"]);
        await _sut.AddFolderCommand.ExecuteAsync(null);
        _sut.SelectedTile = _sut.Tiles[1];

        Assert.Equal("未設定（このマスは黒で表示）", _sut.EmptyFoldersMessage);
    }

    [Fact]
    public async Task 四分割でマスのフォルダを削除すると表示し直す()
    {
        await _settings.UpdateAsync(s => s.WithMonitor(s.GetMonitor("MON-P")
            .AddTileFolder(0, @"D:\A")
            .AddTileFolder(1, @"D:\B") with { Layout = WallpaperLayout.Grid2x2 }), TestContext.Current.CancellationToken);
        await _sut.InitializeCommand.ExecuteAsync(null);
        _sut.SelectedTile = _sut.Tiles[1];
        _sut.SelectedFolder = @"D:\B";

        await _sut.RemoveFolderCommand.ExecuteAsync(null);

        Assert.Equal("未設定（黒）", _sut.Tiles[1].Summary);
        Assert.Contains("黒で表示", _sut.StatusMessage, StringComparison.Ordinal);
        await _rotation.Received(1).NextAsync("MON-P", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task マスやレイアウトを切り替えるとフォルダの選択を解除する()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);
        _sut.SelectedFolder = @"C:\Wallpapers";

        _sut.IsGridLayout = true;
        Assert.Null(_sut.SelectedFolder);
        Assert.False(_sut.RemoveFolderCommand.CanExecute(null));
    }

    [Fact]
    public async Task モニターを切り替えるとマスのフォルダも切り替わる()
    {
        await _settings.UpdateAsync(s => s.WithMonitor(s.GetMonitor("MON-P").AddTileFolder(0, @"D:\P0")),
            TestContext.Current.CancellationToken);
        await _sut.InitializeCommand.ExecuteAsync(null);
        Assert.Equal([@"D:\P0"], _sut.Tiles[0].Folders);

        _sut.SelectedMonitor = _sut.Monitors[0];

        Assert.All(_sut.Tiles, t => Assert.Empty(t.Folders));
    }

    [Fact]
    public async Task 識別では一覧と同じ番号で全モニターに表示し選択中のモニターを強調する()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);
        IReadOnlyList<MonitorIdentification>? shown = null;
        await _identifier.ShowAsync(Arg.Do<IReadOnlyList<MonitorIdentification>>(m => shown = m), Arg.Any<CancellationToken>());

        await _sut.IdentifyMonitorsCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.Equal([(1, "MON-L", false), (2, "MON-P", true)], shown.Select(m => (m.Number, m.Monitor.Id, m.IsSelected)));
    }

    [Fact]
    public async Task 識別の表示に失敗したら画面下部にエラーを表示する()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);
        _identifier.ShowAsync(Arg.Any<IReadOnlyList<MonitorIdentification>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("表示できません")));

        await _sut.IdentifyMonitorsCommand.ExecuteAsync(null);

        Assert.True(_sut.IsStatusError);
        Assert.Contains("表示できません", _sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 自動起動のチェックは登録状態を表示し読み込み時には登録を変更しない()
    {
        _startup.IsEnabled().Returns(true);

        await _sut.InitializeCommand.ExecuteAsync(null);

        Assert.True(_sut.StartWithWindows);
        _startup.DidNotReceive().Enable();
        _startup.DidNotReceive().Disable();
    }

    [Fact]
    public async Task 自動起動のチェックを切り替えると登録と解除を行う()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);

        _sut.StartWithWindows = true;
        _startup.Received(1).Enable();

        _sut.StartWithWindows = false;
        _startup.Received(1).Disable();
        Assert.False(_sut.IsStatusError);
    }

    [Fact]
    public async Task 自動起動の登録に失敗したらチェックを実際の状態に戻してエラーを表示する()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);
        _startup.When(s => s.Enable()).Do(_ => throw new UnauthorizedAccessException("アクセスが拒否されました"));
        _startup.IsEnabled().Returns(false);

        _sut.StartWithWindows = true;

        Assert.False(_sut.StartWithWindows);
        Assert.True(_sut.IsStatusError);
        Assert.Contains("アクセスが拒否されました", _sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 一時フォルダからの実行で自動起動を登録できない場合は理由を表示する()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);
        _startup.When(s => s.Enable()).Do(_ => throw new InvalidOperationException("一時フォルダから実行しているため"));
        _startup.IsEnabled().Returns(false);

        _sut.StartWithWindows = true;

        Assert.False(_sut.StartWithWindows);
        Assert.True(_sut.IsStatusError);
        Assert.Contains("一時フォルダから実行しているため", _sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task モニター構成が変わったら選択を保ったまま一覧を更新する()
    {
        await _sut.InitializeCommand.ExecuteAsync(null);
        MonitorInfo added = new("MON-R", 2, "Right", new MonitorBounds(2560, 0, 4480, 1080), 1920, 1080);
        _monitors.GetMonitorsAsync(Arg.Any<CancellationToken>()).Returns([Left, Primary, added]);

        _watcher.ConfigurationChanged += Raise.EventWith(_watcher,
            new MonitorConfigurationChangedEventArgs([added], [], [], [Left, Primary, added]));
        await WaitUntilAsync(() => _sut.Monitors.Count == 3);

        Assert.Equal(["Left", "Primary", "Right"], _sut.Monitors.Select(m => m.DisplayName));
        Assert.Equal("MON-P", _sut.SelectedMonitor?.Id);
        Assert.Contains("接続: Right", _sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ログフォルダを開く()
    {
        _logDirectory.LogDirectory.Returns(@"C:\Logs\WallpaperChanger");

        await _sut.OpenLogFolderCommand.ExecuteAsync(null);

        _folderLauncher.Received(1).OpenFolder(@"C:\Logs\WallpaperChanger");
        Assert.Equal(@"C:\Logs\WallpaperChanger", _sut.LogDirectory);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    [Fact]
    public void IntervalUnitOptionは分数を時間と分に分解する()
    {
        Assert.Equal((90, IntervalUnitOption.Minutes), IntervalUnitOption.FromMinutes(90));
        Assert.Equal((3, IntervalUnitOption.Hours), IntervalUnitOption.FromMinutes(180));
        Assert.Equal("1 時間 30 分", IntervalUnitOption.Format(TimeSpan.FromMinutes(90)));
        Assert.Equal("24 時間", IntervalUnitOption.Format(TimeSpan.FromMinutes(1440)));
        Assert.Equal("5 分", IntervalUnitOption.Format(TimeSpan.FromMinutes(5)));
    }
}
