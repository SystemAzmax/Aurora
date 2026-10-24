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
        var rotation = Substitute.For<IWallpaperRotationService>();
        rotation.GetCurrentWallpapersAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new CurrentWallpaper(Left, WallpaperSelection.ForSingleImage(@"C:\w\sunset_01.jpg")),
            new CurrentWallpaper(Primary, new WallpaperSelection(WallpaperLayout.Grid2x2, [@"C:\a\1.jpg", null, null, @"C:\d\4.jpg"])),
        ]);
        using var viewModel = new TrayIconViewModel(
            rotation, scheduler, new InMemorySettingsService(), Substitute.For<ISettingsWindowService>(), Substitute.For<IFolderLauncher>(),
            Substitute.For<IHostApplicationLifetime>(), new InlineUiDispatcher(), TimeProvider.System, NullLogger<TrayIconViewModel>.Instance);

        using (var tray = new TrayIconService(viewModel))
        {
            tray.Show();
            await WpfTestApplication.FlushAsync();

            // 切り替え中と切り替え後の表示（メニューの進み具合と、トレイの近くに出す進み具合）も確かめる
            var popup = (FrameworkElement)Application.Current.FindResource("TrayProgressPopup");
            popup.DataContext = viewModel;
            rotation.ProgressChanged += Raise.EventWith(new WallpaperChangeProgressEventArgs(0, 2, Primary));
            await WpfTestApplication.FlushAsync();
            rotation.ProgressChanged += Raise.EventWith(new WallpaperChangeProgressEventArgs(2, 2, null));
            await WpfTestApplication.FlushAsync();

            // 表示中の画像の子メニュー（モニター → マスの 2 段）を開き、項目を実際に作らせる
            await viewModel.RefreshShownImagesCommand.ExecuteAsync(null);
            var menu = (System.Windows.Controls.ContextMenu)Application.Current.FindResource("TrayContextMenu");
            menu.IsOpen = true;
            await WpfTestApplication.FlushAsync();
            var leaves = new List<System.Windows.Controls.MenuItem>();
            foreach (System.Windows.Controls.MenuItem item in menu.Items.OfType<System.Windows.Controls.MenuItem>().Where(i => i.ItemsSource is not null))
            {
                await OpenSubmenuAsync(item, leaves);
            }

            menu.IsOpen = false;
            await WpfTestApplication.FlushAsync();

            // 2 つのメニューそれぞれに、1 枚表示の 1 枚と 4 分割の 2 マス分の画像の項目ができる
            Assert.Equal(6, leaves.Count);
            Assert.Equal(3, leaves.Count(l => ReferenceEquals(l.Command, viewModel.ExcludeImageCommand)));
            Assert.Equal(3, leaves.Count(l => ReferenceEquals(l.Command, viewModel.ShowImageInFolderCommand)));
            Assert.Contains(leaves, l => Equals(l.CommandParameter, @"C:\d\4.jpg"));
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

            // 1 枚表示 → 4 分割（マス選択）→ 別のモニター → 16 分割（区画選択）→ 1 枚表示、と画面の状態を一通り切り替える
            viewModel.IsGridLayout = true;
            await WpfTestApplication.FlushAsync();
            viewModel.SelectedTile = viewModel.Tiles[3];
            await WpfTestApplication.FlushAsync();
            viewModel.SelectedMonitor = viewModel.Monitors[0];
            await WpfTestApplication.FlushAsync();
            viewModel.IsGrid4x4Layout = true;
            await WpfTestApplication.FlushAsync();
            viewModel.SelectedTile = viewModel.Tiles[2];
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

    /// <summary>子メニューを開いて項目を作らせ、子を持たない項目（画像の項目）を集める。</summary>
    private static async Task OpenSubmenuAsync(System.Windows.Controls.MenuItem item, List<System.Windows.Controls.MenuItem> leaves)
    {
        item.IsSubmenuOpen = true;
        await WpfTestApplication.FlushAsync();
        foreach (System.Windows.Controls.MenuItem child in item.Items.Cast<object>()
                     .Select(i => item.ItemContainerGenerator.ContainerFromItem(i))
                     .OfType<System.Windows.Controls.MenuItem>())
        {
            if (child.HasItems)
            {
                await OpenSubmenuAsync(child, leaves);
            }
            else
            {
                leaves.Add(child);
            }
        }

        item.IsSubmenuOpen = false;
    }

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
            .WithMonitor(MonitorSettings.CreateDefault("MON-P").AddFolder(@"C:\Wallpapers").AddTileFolder(3, @"C:\Tile"))
            .AddExcludedImage(@"C:\Wallpapers\hidden.jpg"));

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
