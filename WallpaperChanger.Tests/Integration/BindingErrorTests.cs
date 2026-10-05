using System.Windows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Tests.Fakes;
using WallpaperChanger.UI.Services;
using WallpaperChanger.UI.ViewModels;
using WallpaperChanger.UI.Views;

namespace WallpaperChanger.Tests.Integration;

/// <summary>
/// 本番と同じ手順で画面を生成し、WPF のバインディングエラーが 1 件も記録されないことを確認する。
/// 実際にウィンドウ（画面外に配置）とタスクトレイアイコンを一瞬生成するため、対話セッションで実行すること。
/// </summary>
[Trait("Category", "Integration")]
public sealed class BindingErrorTests
{
    private static readonly MonitorInfo Left = new("MON-L", 1, "Left", new MonitorBounds(-1920, 0, 0, 1080), 1920, 1080);
    private static readonly MonitorInfo Primary = new("MON-P", 0, "Primary", new MonitorBounds(0, 0, 2560, 1440), 2560, 1440);
    private static readonly bool[] SelectionStates = [true, false];

    [Fact]
    public Task 存在しないプロパティへのバインディングを検出できる() => WpfTestApplication.RunAsync(async () =>
    {
        // 検出の仕組み自体が働いていることの確認（働いていないと他のテストが常に成功してしまう）
        using var collector = BindingErrorCollector.Start();
        var text = new System.Windows.Controls.TextBlock();
        text.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding("NoSuchProperty"));
        text.DataContext = new MonitorNumberViewModel(1, "M", "1 × 1", isSelected: false);
        await WpfTestApplication.FlushAsync();

        Assert.Contains(collector.Errors, e => e.Contains("NoSuchProperty", StringComparison.Ordinal));
    });

    [Fact]
    public Task タスクトレイアイコンとメニューにバインディングエラーが無い() => WpfTestApplication.RunAsync(async () =>
    {
        using var collector = BindingErrorCollector.Start();
        var scheduler = Substitute.For<IWallpaperScheduler>();
        scheduler.Interval.Returns(TimeSpan.FromMinutes(30));
        using var viewModel = new TrayIconViewModel(
            Substitute.For<IWallpaperRotationService>(), scheduler, Substitute.For<ISettingsWindowService>(),
            Substitute.For<IHostApplicationLifetime>(), new InlineUiDispatcher(), NullLogger<TrayIconViewModel>.Instance);

        using (var tray = new TrayIconService(viewModel))
        {
            tray.Show();
            await WpfTestApplication.FlushAsync();
        }

        Assert.Empty(collector.Errors);
    });

    [Fact]
    public Task 設定画面の各表示状態でバインディングエラーが無い() => WpfTestApplication.RunAsync(async () =>
    {
        using var collector = BindingErrorCollector.Start();
        using SettingsViewModel viewModel = CreateSettingsViewModel();

        var window = new SettingsWindow();
        window.InitializeComponent();
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowActivated = false;
        window.DataContext = viewModel;
        window.Show();
        try
        {
            await viewModel.InitializeCommand.ExecuteAsync(null);
            await WpfTestApplication.FlushAsync();

            // 1 枚表示 → 4 分割（マス選択）→ 別のモニター → 1 枚表示、と画面の状態を一通り切り替える
            viewModel.IsGridLayout = true;
            await WpfTestApplication.FlushAsync();
            viewModel.SelectedTile = viewModel.Tiles[3];
            await WpfTestApplication.FlushAsync();
            viewModel.SelectedMonitor = viewModel.Monitors[0];
            await WpfTestApplication.FlushAsync();
            viewModel.IsSingleImageLayout = true;
            await WpfTestApplication.FlushAsync();
        }
        finally
        {
            window.Close();
        }

        Assert.Empty(collector.Errors);
    });

    [Fact]
    public Task モニター番号ウィンドウにバインディングエラーが無い() => WpfTestApplication.RunAsync(async () =>
    {
        using var collector = BindingErrorCollector.Start();

        foreach (bool isSelected in SelectionStates)
        {
            var window = new MonitorNumberWindow();
            window.InitializeComponent();
            window.DataContext = new MonitorNumberViewModel(1, "Monitor", "1920 × 1080", isSelected);
            window.Show(); // XAML で画面外（-32000）に配置済み
            await WpfTestApplication.FlushAsync();
            window.Close();
        }

        Assert.Empty(collector.Errors);
    });

    private static ILogDirectoryProvider LogDirectory()
    {
        var provider = Substitute.For<ILogDirectoryProvider>();
        provider.LogDirectory.Returns(@"C:\Logs");
        return provider;
    }

    private static SettingsViewModel CreateSettingsViewModel()
    {
        var monitors = Substitute.For<IMonitorService>();
        monitors.GetMonitorsAsync(Arg.Any<CancellationToken>()).Returns([Left, Primary]);
        var scheduler = Substitute.For<IWallpaperScheduler>();
        scheduler.Interval.Returns(TimeSpan.FromMinutes(30));

        var settings = new InMemorySettingsService(new AppSettings()
            .WithMonitor(MonitorSettings.CreateDefault("MON-P").AddFolder(@"C:\Wallpapers").AddTileFolder(3, @"C:\Tile")));

        return new SettingsViewModel(
            monitors,
            settings,
            Substitute.For<IWallpaperService>(),
            Substitute.For<IWallpaperRotationService>(),
            scheduler,
            Substitute.For<IFolderPickerService>(),
            Substitute.For<IImagePreviewLoader>(),
            Substitute.For<IMonitorIdentifierService>(),
            Substitute.For<IStartupRegistrationService>(),
            Substitute.For<IMonitorConfigurationWatcher>(),
            LogDirectory(),
            Substitute.For<IFolderLauncher>(),
            new InlineUiDispatcher(),
            NullLogger<SettingsViewModel>.Instance);
    }
}
