#:property TargetFramework=net10.0-windows10.0.17763.0
#:property UseWPF=true
#:property PublishAot=false
#:property NoWarn=$(NoWarn);CA1303;CA1305;CA1307;CA1310;CA1849;CA2000;CA2007;CA5394
#:project ../../WallpaperChanger.UI/WallpaperChanger.UI.csproj
#:package NSubstitute

// Microsoft Store の登録情報に載せるスクリーンショット（2560 × 1440 の PNG）を作るスクリプト。
//
// アプリの本物の設定画面（SettingsWindow）とタスクトレイのメニューを、サンプルの設定とフェイクのサービスで動かして描き、
// このスクリプトが図形だけで描いた壁紙（オーロラや夕暮れの風景）と重ねる。
// 実際の壁紙・設定ファイル・モニターには触れない。写真・外部の素材・個人のパスは使わない。
//
// 使い方（.NET 10 SDK のファイルベース実行）:
//   dotnet run tools/StoreScreenshots/make-screenshots.cs -- <出力フォルダ> [--theme dark|light]
//
// 出力: <出力フォルダ>/ja-JP と <出力フォルダ>/en-US に、01～05 の PNG を作る。
//       言語の文言の一部はプロセスの中で 1 回しか読まれないため、言語ごとに自分自身を起動し直して作る。
// トレイのメニューは、描くために一瞬だけ画面の隅に開く。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Core.Services;
using WallpaperChanger.UI;
using WallpaperChanger.UI.Resources;
using WallpaperChanger.UI.Services;
using WallpaperChanger.UI.ViewModels;
using WallpaperChanger.UI.Views;
using Path = System.IO.Path;

string? outDir = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && !IsOptionValue(args, a));
if (outDir is null)
{
    Console.Error.WriteLine("使い方: dotnet run tools/StoreScreenshots/make-screenshots.cs -- <出力フォルダ> [--theme dark|light]");
    return 1;
}

string theme = GetOption(args, "--theme") ?? "dark";
string? lang = GetOption(args, "--lang");
if (lang is null)
{
    // 言語ごとに自分自身を起動し直す（静的な一覧の表示名などが、最初に読んだ言語のままになるため）
    foreach (string l in new[] { "ja", "en" })
    {
        using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        {
            ArgumentList = { outDir, "--theme", theme, "--lang", l },
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            return process.ExitCode;
        }
    }

    return 0;
}

int exitCode = 0;
var thread = new Thread(() =>
{
    AppLanguage language = lang == "en" ? AppLanguage.English : AppLanguage.Japanese;
    Localizer.Apply(language);
    var app = new App();
    app.InitializeComponent();
    // App.xaml の Fluent テーマは Windows の設定に従うため、ライトかダークに固定する
    app.Resources.MergedDictionaries[0] = new ResourceDictionary
    {
        Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{(theme == "light" ? "Light" : "Dark")}.xaml"),
    };

    string dir = Path.Combine(Path.GetFullPath(outDir), language == AppLanguage.English ? "en-US" : "ja-JP");
    Directory.CreateDirectory(dir);
    app.Dispatcher.InvokeAsync(async () =>
    {
        try
        {
            await new ScreenshotMaker(app, dir, theme == "light", language == AppLanguage.English).RunAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            exitCode = 1;
        }
        finally
        {
            app.Shutdown();
        }
    });
    app.Run();
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
return exitCode;

static string? GetOption(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static bool IsOptionValue(string[] args, string value)
{
    int i = Array.IndexOf(args, value);
    return i > 0 && args[i - 1].StartsWith("--", StringComparison.Ordinal);
}

/// <summary>スクリーンショットを 1 枚ずつ組み立てて保存する。</summary>
internal sealed class ScreenshotMaker(App app, string outDir, bool light, bool english)
{
    /// <summary>
    /// 英語の画面で使うフォント。Fluent テーマは Windows のシステムのフォントを使うため、日本語の Windows で撮ると
    /// 英語の画面も Yu Gothic UI になる（「\」が「¥」で表示される）。英語の Windows と同じ Segoe UI にする。
    /// </summary>
    private readonly FontFamily? _uiFont = english ? new FontFamily("Segoe UI") : null;

    private const int CanvasWidth = 2560;
    private const int CanvasHeight = 1440;

    /// <summary>画面の拡大率（2560 × 1440 のモニターを 150% で使っているときの大きさ）。</summary>
    private const double Scale = 1.5;

    private const string Root = @"D:\Pictures\Wallpapers";
    private const string AuroraFolder = Root + @"\Aurora";
    private const string ForestFolder = Root + @"\Forest";
    private const string LakeFolder = Root + @"\Lakes";
    private const string SunsetFolder = Root + @"\Sunset";

    private static readonly MonitorInfo Primary = new("MON-1", 0, "WQHD Monitor", new MonitorBounds(0, 0, 2560, 1440), 2560, 1440);
    private static readonly MonitorInfo Second = new("MON-2", 1, "Full HD Monitor", new MonitorBounds(2560, 180, 4480, 1260), 1920, 1080);

    private readonly Dictionary<string, BitmapSource> _images = new(StringComparer.OrdinalIgnoreCase);

    public async Task RunAsync()
    {
        Console.WriteLine($"{outDir} に作ります");

        // ---- 壁紙（各フォルダに 4 枚ずつ） ----
        string[] aurora = Paths(AuroraFolder, "aurora", 4);
        string[] forest = Paths(ForestFolder, "forest", 4);
        string[] lake = Paths(LakeFolder, "lake", 4);
        string[] sunset = Paths(SunsetFolder, "sunset", 4);
        Generate(aurora, [Scenes.Green(Terrain.Lake, 11), Scenes.Purple(Terrain.Ranges, 12), Scenes.Teal(Terrain.Forest, 13), Scenes.RedTop(Terrain.Lake, 14)]);
        Generate(forest, [Scenes.Green(Terrain.Forest, 21), Scenes.Ice(Terrain.Forest, 22), Scenes.Dusk(Terrain.Forest, 23, warm: false), Scenes.Teal(Terrain.Forest, 24)]);
        Generate(lake, [Scenes.Teal(Terrain.Lake, 31), Scenes.Dusk(Terrain.Lake, 32, warm: true), Scenes.Purple(Terrain.Lake, 33), Scenes.Green(Terrain.Lake, 34)]);
        Generate(sunset, [Scenes.Dusk(Terrain.Ranges, 41, warm: true), Scenes.Dusk(Terrain.Lake, 42, warm: false), Scenes.Dusk(Terrain.Ranges, 43, warm: false), Scenes.Dusk(Terrain.Forest, 44, warm: true)]);

        // ---- 1. 設定画面（1 枚表示） ----
        {
            string current = aurora[0];
            AppSettings settings = BaseSettings()
                .WithMonitor(MonitorSettings.CreateDefault(Primary.Id).AddFolder(AuroraFolder).AddFolder(LakeFolder) with
                {
                    SingleImageFit = ImageFit.Fill,
                });
            var window = await OpenSettingsAsync(settings, new() { [Primary.Id] = current, [Second.Id] = lake[1] });
            Save("01-settings", Desktop(_images[current], WindowFrame(window, Strings.SettingsWindowTitle), 620, 120));
            Close(window);
        }

        // ---- 2. 4 分割表示 ----
        {
            string[] tiles = [aurora[1], forest[0], lake[0], sunset[0]];
            BitmapSource composite = ComposeGrid(tiles, 2, 2);
            string compositePath = @"C:\Users\User\AppData\Local\WallpaperChanger\Composites\MON-1_20261114120000.jpg";
            _images[compositePath] = composite;
            AppSettings settings = BaseSettings()
                .WithMonitor(MonitorSettings.CreateDefault(Primary.Id)
                    .AddTileFolder(0, AuroraFolder).AddTileFolder(1, ForestFolder).AddTileFolder(2, LakeFolder).AddTileFolder(3, SunsetFolder) with
                    {
                        Layout = WallpaperLayout.Grid2x2,
                    });
            var window = await OpenSettingsAsync(settings, new() { [Primary.Id] = compositePath, [Second.Id] = lake[1] }, vm =>
            {
                vm.SelectedTile = vm.Tiles[1];
            });
            Save("02-grid", Desktop(composite, WindowFrame(window, Strings.SettingsWindowTitle), 140, 120));
            Close(window);
        }

        // ---- 3. 16 分割表示とタスクトレイのメニュー ----
        {
            // 4 × 4 のマスは、2 × 2 の区画ごとに左上・右上・左下・右下のフォルダから選ぶ
            string[][] quadrants = [aurora, forest, lake, sunset];
            var tiles = new string?[16];
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    int quadrant = (row / 2 * 2) + (column / 2);
                    int inner = (row % 2 * 2) + (column % 2);
                    tiles[(row * 4) + column] = quadrants[quadrant][inner];
                }
            }

            BitmapSource composite = ComposeGrid(tiles!, 4, 4);
            FrameworkElement menu = await RenderTrayMenuAsync(
            [
                new CurrentWallpaper(Primary, new WallpaperSelection(WallpaperLayout.Grid4x4, tiles)),
                new CurrentWallpaper(Second, WallpaperSelection.ForSingleImage(lake[1])),
            ]);
            Save("03-tray-menu", Desktop(composite, menu, 0, 0, shadow: false));
        }

        // ---- 4. 切替設定とランダム設定 ----
        {
            string current = aurora[3];
            AppSettings settings = BaseSettings()
                .WithMonitor(MonitorSettings.CreateDefault(Primary.Id).AddFolder(AuroraFolder).AddFolder(ForestFolder) with
                {
                    SelectionMode = ImageSelectionMode.Sequential,
                });
            var window = await OpenSettingsAsync(settings, new() { [Primary.Id] = current, [Second.Id] = lake[1] },
                vm => vm.SelectedCopySource = vm.CopySources.FirstOrDefault(), scrollToTitle: Strings.ScheduleTitle);
            Save("04-schedule", Desktop(_images[current], WindowFrame(window, Strings.SettingsWindowTitle), 620, 120));
            Close(window);
        }

        // ---- 5. 表示しない画像・起動・設定の書き出し ----
        {
            string current = sunset[0];
            AppSettings settings = BaseSettings()
                .WithMonitor(MonitorSettings.CreateDefault(Primary.Id).AddFolder(SunsetFolder).AddFolder(LakeFolder))
                .AddExcludedImage(Path.Combine(LakeFolder, "lake-02.jpg"))
                .AddExcludedImage(Path.Combine(SunsetFolder, "sunset-03.jpg"));
            var window = await OpenSettingsAsync(settings, new() { [Primary.Id] = current, [Second.Id] = lake[1] }, scrollToTitle: Strings.ExcludedImagesTitle, startWithWindows: true);
            Save("05-options", Desktop(_images[current], WindowFrame(window, Strings.SettingsWindowTitle), 140, 120));
            Close(window);
        }
    }

    // ================= 設定画面 =================

    private AppSettings BaseSettings() => new AppSettings { Language = english ? AppLanguage.English : AppLanguage.Japanese }
        .WithMonitor(MonitorSettings.CreateDefault(Second.Id).AddFolder(LakeFolder) with { DisplayName = Second.DisplayName })
        .WithMonitor(MonitorSettings.CreateDefault("MON-OLD").AddFolder(AuroraFolder) with { DisplayName = "Previous Monitor" });

    private async Task<SettingsWindow> OpenSettingsAsync(
        AppSettings settings,
        Dictionary<string, string> currentWallpapers,
        Action<SettingsViewModel>? configure = null,
        string? scrollToTitle = null,
        bool startWithWindows = false)
    {
        var monitors = Substitute.For<IMonitorService>();
        monitors.GetMonitorsAsync(Arg.Any<CancellationToken>()).Returns([Primary, Second]);

        var scheduler = Substitute.For<IWallpaperScheduler>();
        scheduler.Interval.Returns(settings.Interval);
        scheduler.IsRunning.Returns(true);

        var wallpaper = Substitute.For<IWallpaperService>();
        wallpaper.GetWallpaperAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => currentWallpapers.GetValueOrDefault(call.ArgAt<string>(0)));

        var preview = Substitute.For<IImagePreviewLoader>();
        preview.LoadAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => (ImageSource?)_images.GetValueOrDefault(call.ArgAt<string>(0)));

        var startup = Substitute.For<IStartupRegistrationService>();
        startup.IsEnabled().ReturnsForAnyArgs(startWithWindows);

        var logDirectory = Substitute.For<ILogDirectoryProvider>();
        logDirectory.LogDirectory.Returns(@"C:\Logs");

        var viewModel = new SettingsViewModel(
            monitors,
            new MemorySettingsService(settings),
            wallpaper,
            Substitute.For<IWallpaperRotationService>(),
            scheduler,
            Substitute.For<IFolderPickerService>(),
            preview,
            Substitute.For<IMonitorIdentifierService>(),
            startup,
            Substitute.For<IMonitorConfigurationWatcher>(),
            logDirectory,
            Substitute.For<IFolderLauncher>(),
            Substitute.For<IMessageDialogService>(),
            Substitute.For<ISettingsFileDialogService>(),
            Substitute.For<IMonitorSettingsInheritor>(),
            new ImmediateDispatcher(),
            NullLogger<SettingsViewModel>.Instance);

        var window = new SettingsWindow();
        window.InitializeComponent();
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.Height = 840;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.DataContext = viewModel;
        if (_uiFont is not null)
        {
            window.FontFamily = _uiFont;
        }
        window.Show();

        await viewModel.InitializeCommand.ExecuteAsync(null);
        viewModel.SelectedMonitor = viewModel.Monitors.First(m => m.Id == Primary.Id);
        await FlushAsync();
        configure?.Invoke(viewModel);
        await Task.Delay(300);
        await FlushAsync();

        // 操作の結果の欄は、画面を開いたばかりのときと同じく空にする
        viewModel.StatusMessage = null;

        if (scrollToTitle is not null)
        {
            var content = (FrameworkElement)window.Content;
            // 右側の設定の欄（モニター一覧などのリストの中の ScrollViewer は除く）
            ScrollViewer viewer = Descendants<ScrollViewer>(content).First(v => v.TemplatedParent is null);
            TextBlock title = Descendants<TextBlock>(viewer).First(t => t.Text == scrollToTitle);
            var card = FindAncestor<Border>(title)!;
            Point top = card.TransformToAncestor((Visual)viewer.Content).Transform(new Point(0, 0));
            viewer.ScrollToVerticalOffset(top.Y - 12);
        }

        await FlushAsync();
        return window;
    }

    private static void Close(SettingsWindow window)
    {
        (window.DataContext as IDisposable)?.Dispose();
        window.Close();
    }

    /// <summary>ウィンドウの中身を描き、Windows 11 のウィンドウと同じ形のタイトルバーと枠を付ける。</summary>
    private FrameworkElement WindowFrame(Window window, string title)
    {
        var content = (FrameworkElement)window.Content;
        double width = content.ActualWidth;
        double height = content.ActualHeight;
        BitmapSource body = RenderVisual(content, width, height, Scale);

        var background = (Brush)app.FindResource("ApplicationBackgroundBrush");
        var foreground = (Brush)app.FindResource("TextFillColorPrimaryBrush");
        double s = Scale;

        var titleBar = new DockPanel { Height = 32 * s, Background = background, LastChildFill = true };
        var captionButtons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (string glyph in new[] { "\uE921", "\uE922", "\uE8BB" })
        {
            captionButtons.Children.Add(new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 10 * s,
                Foreground = foreground,
                Width = 46 * s,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        DockPanel.SetDock(captionButtons, Dock.Right);
        titleBar.Children.Add(captionButtons);
        var icon = new Image
        {
            Source = BitmapFrame.Create(new Uri("pack://application:,,,/WallpaperChanger;component/Resources/AppIcon.ico")),
            Width = 16 * s,
            Height = 16 * s,
            Margin = new Thickness(12 * s, 0, 10 * s, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        DockPanel.SetDock(icon, Dock.Left);
        titleBar.Children.Add(icon);
        titleBar.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = new FontFamily(english ? "Segoe UI" : "Yu Gothic UI"),
            FontSize = 12 * s,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        });

        double frameWidth = Math.Round(width * s);
        double frameHeight = Math.Round(height * s) + titleBar.Height;
        var stack = new DockPanel { Width = frameWidth, Height = frameHeight, Background = background };
        DockPanel.SetDock(titleBar, Dock.Top);
        stack.Children.Add(titleBar);
        stack.Children.Add(new Image { Source = body, Width = body.PixelWidth, Height = body.PixelHeight, Stretch = Stretch.Fill, VerticalAlignment = VerticalAlignment.Top });
        stack.Clip = new RectangleGeometry(new Rect(0, 0, frameWidth, frameHeight), 8 * s, 8 * s);

        return new Border
        {
            Width = frameWidth + 2,
            Height = frameHeight + 2,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius((8 * s) + 1),
            BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x60, 0, 0, 0) : Color.FromArgb(0x80, 0x60, 0x60, 0x60)),
            Child = stack,
        };
    }

    // ================= タスクトレイのメニュー =================

    private async Task<FrameworkElement> RenderTrayMenuAsync(IReadOnlyList<CurrentWallpaper> current)
    {
        var scheduler = Substitute.For<IWallpaperScheduler>();
        scheduler.Interval.Returns(TimeSpan.FromMinutes(30));
        var rotation = Substitute.For<IWallpaperRotationService>();
        rotation.GetCurrentWallpapersAsync(Arg.Any<CancellationToken>()).Returns(current);
        using var viewModel = new TrayIconViewModel(
            rotation, scheduler, new MemorySettingsService(new AppSettings()), Substitute.For<ISettingsWindowService>(),
            Substitute.For<IFolderLauncher>(), Substitute.For<IHostApplicationLifetime>(), new ImmediateDispatcher(),
            TimeProvider.System, NullLogger<TrayIconViewModel>.Instance);
        await viewModel.RefreshShownImagesCommand.ExecuteAsync(null);

        var menu = (ContextMenu)app.FindResource("TrayContextMenu");
        menu.DataContext = viewModel;
        if (_uiFont is not null)
        {
            menu.FontFamily = _uiFont;
        }
        menu.Placement = PlacementMode.Absolute;
        menu.HorizontalOffset = 0;
        menu.VerticalOffset = 0;
        menu.IsOpen = true;
        await FlushAsync();

        // 「この画像を表示しない」→ 16 分割のモニター、と 2 段の子メニューを開く
        MenuItem exclude = menu.Items.OfType<MenuItem>().First(i => Equals(i.Header, Strings.ExcludeImage));
        exclude.IsSubmenuOpen = true;
        await FlushAsync();
        var group = (MenuItem)exclude.ItemContainerGenerator.ContainerFromIndex(0);
        group.IsSubmenuOpen = true;
        await FlushAsync();

        // 項目に重ねる選択中の表示（マウスを重ねたときの色）
        exclude.Focus();
        group.Focus();
        await FlushAsync();

        FrameworkElement main = Snapshot(menu);
        FrameworkElement sub1 = Snapshot(SubmenuRoot(exclude));
        FrameworkElement sub2 = Snapshot(SubmenuRoot(group));

        double excludeTop = ItemTop(menu, exclude);
        double groupTop = ItemTop(SubmenuRoot(exclude), group);
        group.IsSubmenuOpen = false;
        exclude.IsSubmenuOpen = false;
        menu.IsOpen = false;
        await FlushAsync();

        // 実際の画面と同じ置き方にする。メニューはタスクバーの通知領域の上（画面の右下）に開き、
        // 子メニューは親の項目の高さに合わせて左側に開く。画面の下にはみ出す子メニューは、上にずらす
        double bottom = CanvasHeight - (48 * Scale) - (12 * Scale);
        double overlap = 4 * Scale;
        double x0 = CanvasWidth - (12 * Scale) - main.Width;
        double y0 = bottom - main.Height;
        double x1 = x0 - sub1.Width + overlap;
        double y1 = Math.Min(y0 + excludeTop - (4 * Scale), bottom - sub1.Height);
        double x2 = x1 - sub2.Width + overlap;
        double y2 = Math.Min(y1 + groupTop - (4 * Scale), bottom - sub2.Height);
        var canvas = new Canvas { Width = CanvasWidth, Height = CanvasHeight };
        Place(canvas, main, x0, y0);
        Place(canvas, sub1, x1, y1);
        Place(canvas, sub2, x2, y2);
        return canvas;

        static FrameworkElement SubmenuRoot(MenuItem item)
        {
            var popup = (Popup)item.Template.FindName("PART_Popup", item);
            return (FrameworkElement)popup.Child;
        }

        static double ItemTop(FrameworkElement menuRoot, MenuItem item) =>
            item.TransformToAncestor(menuRoot).Transform(new Point(0, 0)).Y * Scale;

        static void Place(Canvas canvas, FrameworkElement element, double x, double y)
        {
            Canvas.SetLeft(element, Math.Round(x));
            Canvas.SetTop(element, Math.Round(y));
            canvas.Children.Add(element);
        }
    }

    /// <summary>メニューなどの見た目を、拡大率に合わせて描いた画像にする。</summary>
    private static Image Snapshot(FrameworkElement element)
    {
        BitmapSource bitmap = RenderVisual(element, element.ActualWidth, element.ActualHeight, Scale);
        return new Image { Source = bitmap, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, Stretch = Stretch.Fill };
    }

    // ================= 壁紙 =================

    private static string[] Paths(string folder, string prefix, int count) =>
        [.. Enumerable.Range(1, count).Select(i => Path.Combine(folder, $"{prefix}-{i:00}.jpg"))];

    private void Generate(string[] paths, SceneSpec[] specs)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            _images[paths[i]] = WallpaperPainter.Paint(specs[i], 1920, 1080);
        }
    }

    /// <summary>アプリの分割表示と同じ配置（TileLayout、マスを埋める、間隔 4 px、間隔の色 #202020）で、モニターの大きさの 1 枚にする。</summary>
    private BitmapSource ComposeGrid(IReadOnlyList<string> images, int columns, int rows)
    {
        var tileStyle = new TileStyle();
        IReadOnlyList<PixelRect> tiles = TileLayout.CalculateTiles(CanvasWidth, CanvasHeight, columns, rows, tileStyle.Gap);
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(FromRgb(tileStyle.GapColor)), null, new Rect(0, 0, CanvasWidth, CanvasHeight));
            for (int i = 0; i < tiles.Count; i++)
            {
                BitmapSource source = _images[images[i]];
                PixelRect tile = tiles[i];
                ImagePlacement placement = TileLayout.CalculatePlacement(source.PixelWidth, source.PixelHeight, tile.Width, tile.Height, tileStyle.Fit);
                dc.PushClip(new RectangleGeometry(new Rect(tile.X, tile.Y, tile.Width, tile.Height)));
                dc.DrawImage(source, new Rect(
                    tile.X + placement.OffsetX - placement.Crop.X,
                    tile.Y + placement.OffsetY - placement.Crop.Y,
                    placement.ScaledWidth,
                    placement.ScaledHeight));
                dc.Pop();
            }
        }

        return RenderVisual(visual, CanvasWidth, CanvasHeight, 1);
    }

    private static Color FromRgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    // ================= 組み立てと保存 =================

    /// <summary>壁紙の上に、ウィンドウやメニューを置いた画面を作る（単位はピクセル）。</summary>
    private static FrameworkElement Desktop(BitmapSource wallpaper, FrameworkElement foreground, double left, double top, bool shadow = true)
    {
        var canvas = new Canvas { Width = CanvasWidth, Height = CanvasHeight };
        var background = new Image { Source = wallpaper, Width = CanvasWidth, Height = CanvasHeight, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(background, BitmapScalingMode.HighQuality);
        canvas.Children.Add(background);
        if (shadow)
        {
            foreground.Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 12, Direction = 270, Opacity = 0.55, Color = Colors.Black };
        }

        Canvas.SetLeft(foreground, Math.Round(left));
        Canvas.SetTop(foreground, Math.Round(top));
        canvas.Children.Add(foreground);
        return canvas;
    }

    private void Save(string name, FrameworkElement scene)
    {
        BitmapSource bitmap = RenderElement(scene, CanvasWidth, CanvasHeight);
        string path = Path.Combine(outDir, name + ".png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0)));
        using (FileStream stream = File.Create(path))
        {
            encoder.Save(stream);
        }

        Console.WriteLine($"  {path}");
    }

    /// <summary>画面に置いていない要素を、指定の大きさ（ピクセル = DIP）で描く。</summary>
    public static BitmapSource RenderElement(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// 表示中の要素を、拡大率を掛けた解像度で描く。
    /// VisualBrush を通して描き、親の中での位置（オフセット）に影響されないようにする。
    /// </summary>
    public static BitmapSource RenderVisual(Visual visual, double width, double height, double scale)
    {
        var drawing = new DrawingVisual();
        using (DrawingContext dc = drawing.RenderOpen())
        {
            var brush = new VisualBrush(visual) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Round(width * scale), (int)Math.Round(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return bitmap;
    }

    private static async Task FlushAsync()
    {
        for (int i = 0; i < 3; i++)
        {
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        DependencyObject? current = VisualTreeHelper.GetParent(element);
        while (current is not null and not T)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return current as T;
    }
}

/// <summary>メモリの上だけで動く設定（保存しない）。</summary>
internal sealed class MemorySettingsService(AppSettings initial) : ISettingsService
{
    public AppSettings Current { get; private set; } = initial;

    public string? LoadWarning => null;

    public event EventHandler<AppSettings>? SettingsChanged;

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
    {
        Current = update(Current);
        SettingsChanged?.Invoke(this, Current);
        return Task.CompletedTask;
    }

    public Task ExportAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ImportAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>UI スレッドの上で、その場で実行する。</summary>
internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}

// ================= 壁紙を描く =================

internal enum Terrain
{
    Lake,
    Forest,
    Ranges,
}

/// <summary>壁紙 1 枚の描き方。</summary>
/// <param name="SkyTop">空の上の色。</param>
/// <param name="SkyMid">空の中ほどの色。</param>
/// <param name="SkyHorizon">地平線の近くの色。</param>
/// <param name="AuroraLow">オーロラの下の縁（いちばん明るいところ）の色。null ならオーロラを描かない（夕暮れ）。</param>
/// <param name="AuroraMid">オーロラの中ほどの色。</param>
/// <param name="AuroraHigh">オーロラの上の色。</param>
/// <param name="Glow">地平線の光（夕日）の色。null なら描かない。</param>
internal sealed record SceneSpec(
    Terrain Terrain,
    int Seed,
    Color SkyTop,
    Color SkyMid,
    Color SkyHorizon,
    Color? AuroraLow,
    Color AuroraMid,
    Color AuroraHigh,
    Color? Glow,
    double StarDensity);

internal static class Scenes
{
    public static SceneSpec Green(Terrain terrain, int seed) => new(terrain, seed,
        C("#02040B"), C("#071A2C"), C("#0E3B47"), C("#5CFFA8"), C("#1ED38A"), C("#6A4DFF"), null, 1.0);

    public static SceneSpec Teal(Terrain terrain, int seed) => new(terrain, seed,
        C("#020510"), C("#08182F"), C("#0C3550"), C("#5CF6E6"), C("#27B5E8"), C("#3E5BFF"), null, 1.0);

    public static SceneSpec Purple(Terrain terrain, int seed) => new(terrain, seed,
        C("#05030D"), C("#140B2C"), C("#2B1C49"), C("#FF79D2"), C("#B455FF"), C("#4A3BFF"), null, 1.0);

    public static SceneSpec RedTop(Terrain terrain, int seed) => new(terrain, seed,
        C("#03040B"), C("#0C1630"), C("#1A3346"), C("#9BFF73"), C("#36E07E"), C("#FF4F7D"), null, 1.0);

    public static SceneSpec Ice(Terrain terrain, int seed) => new(terrain, seed,
        C("#02040C"), C("#0A1730"), C("#1B3552"), C("#B6F0FF"), C("#58AEFF"), C("#8A5CFF"), null, 1.1);

    public static SceneSpec Dusk(Terrain terrain, int seed, bool warm) => warm
        ? new(terrain, seed, C("#141B45"), C("#6D3E78"), C("#F5915A"), null, default, default, C("#FFD48A"), 0.25)
        : new(terrain, seed, C("#0B1638"), C("#3B4A8C"), C("#E88FA0"), null, default, default, C("#FFC6B0"), 0.35);

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}

internal static class WallpaperPainter
{
    public static BitmapSource Paint(SceneSpec spec, int w, int h)
    {
        var random = new Random(spec.Seed);
        double k = w / 1920.0;
        double horizon = spec.Terrain == Terrain.Lake ? h * 0.66 : h;

        // ---- 空（星・オーロラ・夕日）と遠くの山 ----
        var sky = new Grid { Width = w, Height = h };
        sky.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(
                [
                    new GradientStop(spec.SkyTop, 0),
                    new GradientStop(spec.SkyMid, 0.42),
                    new GradientStop(spec.SkyHorizon, spec.Terrain == Terrain.Lake ? 0.66 : 0.8),
                    new GradientStop(spec.SkyHorizon, 1),
                ], 90),
        });

        if (spec.Glow is { } glow)
        {
            double sunX = w * (0.3 + (random.NextDouble() * 0.4));
            double sunY = (spec.Terrain == Terrain.Lake ? horizon : h * 0.74) - (h * 0.02);
            sky.Children.Add(new Rectangle
            {
                Fill = new RadialGradientBrush(
                    [
                        new GradientStop(WithAlpha(glow, 0.95), 0),
                        new GradientStop(WithAlpha(glow, 0.45), 0.25),
                        new GradientStop(WithAlpha(glow, 0), 1),
                    ])
                {
                    MappingMode = BrushMappingMode.Absolute,
                    Center = new Point(sunX, sunY),
                    GradientOrigin = new Point(sunX, sunY),
                    RadiusX = w * 0.45,
                    RadiusY = h * 0.42,
                },
            });
        }

        sky.Children.Add(new Image { Source = Stars(w, spec.Terrain == Terrain.Lake ? horizon : h * 0.8, h, spec.StarDensity, k, random) });

        if (spec.AuroraLow is { } low)
        {
            BitmapSource aurora = Aurora(spec, low, w, h, k, random);
            sky.Children.Add(new Image { Source = aurora, Effect = new BlurEffect { Radius = 34 * k }, Opacity = 0.95 });
            sky.Children.Add(new Image { Source = aurora, Effect = new BlurEffect { Radius = 5 * k }, Opacity = 0.8 });
        }

        Color farColor = spec.AuroraLow is null
            ? Mix(Mix(spec.SkyHorizon, spec.SkyMid, 0.6), Colors.Black, 0.45)
            : Mix(spec.SkyHorizon, Colors.Black, 0.55);
        Color nearColor = spec.AuroraLow is null
            ? Mix(spec.SkyMid, Colors.Black, 0.72)
            : Mix(spec.SkyTop, Colors.Black, 0.5);

        switch (spec.Terrain)
        {
            case Terrain.Lake:
                sky.Children.Add(Range(w, horizon, horizon - (h * 0.2), horizon - (h * 0.08), farColor, random, 0.58));
                sky.Children.Add(Range(w, horizon, horizon - (h * 0.1), horizon - (h * 0.02), nearColor, random, 0.5));
                break;
            case Terrain.Forest:
                sky.Children.Add(Range(w, h, h * 0.52, h * 0.66, farColor, random, 0.58));
                sky.Children.Add(Range(w, h, h * 0.66, h * 0.76, Mix(farColor, nearColor, 0.5), random, 0.5));
                sky.Children.Add(Forest(w, h, h * 0.78, h * 0.86, nearColor, k, random));
                break;
            case Terrain.Ranges:
                sky.Children.Add(Range(w, h, h * 0.46, h * 0.62, Mix(farColor, spec.SkyHorizon, 0.35), random, 0.6));
                sky.Children.Add(Range(w, h, h * 0.58, h * 0.72, farColor, random, 0.56));
                sky.Children.Add(Range(w, h, h * 0.7, h * 0.82, Mix(farColor, nearColor, 0.55), random, 0.52));
                sky.Children.Add(Range(w, h, h * 0.82, h * 0.92, nearColor, random, 0.5));
                break;
        }

        BitmapSource upper = ScreenshotMaker.RenderElement(sky, w, h);
        if (spec.Terrain != Terrain.Lake)
        {
            return upper;
        }

        // ---- 湖（空と山を上下に映し、暗くして波の線を入れる） ----
        var scene = new Grid { Width = w, Height = h };
        scene.Children.Add(new Image { Source = upper });
        var reflection = new Image
        {
            Source = upper,
            RenderTransform = new ScaleTransform(1, -1, 0, horizon),
            Opacity = 0.62,
            Effect = new BlurEffect { Radius = 3 * k },
        };
        var water = new Grid { Clip = new RectangleGeometry(new Rect(0, horizon, w, h - horizon)) };
        water.Children.Add(new Rectangle { Fill = new SolidColorBrush(Mix(spec.SkyTop, Colors.Black, 0.3)) });
        water.Children.Add(reflection);
        var ripples = new DrawingVisual();
        using (DrawingContext dc = ripples.RenderOpen())
        {
            for (int i = 0; i < 260; i++)
            {
                double y = horizon + (Math.Pow(random.NextDouble(), 1.6) * (h - horizon));
                double length = (20 + (random.NextDouble() * 160)) * k * (1 + ((y - horizon) / (h - horizon)));
                double x = random.NextDouble() * w;
                var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(20 + random.Next(40)), 255, 255, 255)), Math.Max(1, k * 1.2));
                dc.DrawLine(pen, new Point(x, y), new Point(x + length, y));
            }
        }

        water.Children.Add(new Image { Source = ScreenshotMaker.RenderVisual(ripples, w, h, 1) });
        water.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(
                [
                    new GradientStop(Color.FromArgb(0x30, 0, 0, 0), horizon / h),
                    new GradientStop(Color.FromArgb(0xC0, 0, 0, 0), 1),
                ], 90),
        });
        scene.Children.Add(water);

        // 岸（地平線の細い影）
        scene.Children.Add(Range(w, horizon + (h * 0.012), horizon - (h * 0.004), horizon + (h * 0.004), Mix(spec.SkyTop, Colors.Black, 0.7), random, 0.5));
        return ScreenshotMaker.RenderElement(scene, w, h);
    }

    /// <summary>星。地平線に近いほど少なくする。</summary>
    private static BitmapSource Stars(int w, double bottom, int h, double density, double k, Random random)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            int count = (int)(900 * density * k * k);
            for (int i = 0; i < count; i++)
            {
                double y = Math.Pow(random.NextDouble(), 1.5) * bottom;
                double x = random.NextDouble() * w;
                double r = (0.4 + (Math.Pow(random.NextDouble(), 4) * 1.6)) * k;
                byte a = (byte)(60 + (random.NextDouble() * 195 * (1 - (y / bottom * 0.7))));
                Color c = random.NextDouble() < 0.2 ? Color.FromArgb(a, 200, 220, 255) : Color.FromArgb(a, 255, 255, 255);
                dc.DrawEllipse(new SolidColorBrush(c), null, new Point(x, y), r, r);
                if (r > 1.6 * k)
                {
                    var halo = new RadialGradientBrush(Color.FromArgb((byte)(a / 3), c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
                    dc.DrawEllipse(halo, null, new Point(x, y), r * 4, r * 4);
                }
            }
        }

        return ScreenshotMaker.RenderVisual(visual, w, h, 1);
    }

    /// <summary>
    /// オーロラ。細い縦の帯を並べ、下の縁を明るく、上に向かって薄くする。
    /// 帯ごとに明るさと高さを変えて、光の筋に見せる。
    /// </summary>
    private static BitmapSource Aurora(SceneSpec spec, Color low, int w, int h, double k, Random random)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            int bands = 2 + random.Next(2);
            for (int band = 0; band < bands; band++)
            {
                double baseY = h * (0.3 + (random.NextDouble() * 0.16) + (band * 0.04));
                var wave = new Noise(random, w / 3.5);
                var swirl = new Noise(random, w / 12.0);
                var rays = new Noise(random, 26 * k);
                var fine = new Noise(random, 7 * k);
                var heights = new Noise(random, 90 * k);
                double bandHeight = h * (0.2 + (random.NextDouble() * 0.16));
                double x0 = (-0.15 + (random.NextDouble() * 0.35)) * w;
                double x1 = (0.75 + (random.NextDouble() * 0.45)) * w;
                double intensity = band == 0 ? 1.0 : 0.55 + (random.NextDouble() * 0.3);
                double step = Math.Max(1, 1.5 * k);
                for (double x = Math.Max(0, x0); x < Math.Min(w, x1); x += step)
                {
                    double t = (x - x0) / (x1 - x0);
                    double envelope = Math.Pow(Math.Sin(Math.PI * t), 0.7);
                    double bottom = baseY + ((wave.At(x) - 0.5) * h * 0.3) + ((swirl.At(x) - 0.5) * h * 0.08);
                    double ray = 0.25 + (0.75 * ((0.65 * rays.At(x)) + (0.35 * fine.At(x))));
                    double height = bandHeight * (0.45 + (0.9 * heights.At(x)));
                    double a = Math.Clamp(envelope * ray * intensity, 0, 1);
                    var brush = new LinearGradientBrush(
                        [
                            new GradientStop(WithAlpha(spec.AuroraHigh, 0), 0),
                            new GradientStop(WithAlpha(spec.AuroraHigh, 0.28 * a), 0.3),
                            new GradientStop(WithAlpha(spec.AuroraMid, 0.55 * a), 0.68),
                            new GradientStop(WithAlpha(low, 0.95 * a), 0.94),
                            new GradientStop(WithAlpha(low, 0), 1),
                        ], 90);
                    brush.Freeze();
                    dc.DrawRectangle(brush, null, new Rect(x, bottom - height, step + 0.8, height));
                }
            }
        }

        return ScreenshotMaker.RenderVisual(visual, w, h, 1);
    }

    /// <summary>山並み（中点変位法で稜線を作り、下を塗りつぶす）。</summary>
    private static System.Windows.Shapes.Path Range(int w, double bottom, double highest, double lowest, Color color, Random random, double roughness)
    {
        const int Segments = 512;
        var ys = new double[Segments + 1];
        ys[0] = Lerp(highest, lowest, random.NextDouble());
        ys[Segments] = Lerp(highest, lowest, random.NextDouble());
        double displacement = (lowest - highest) * 0.9;
        for (int size = Segments; size > 1; size /= 2)
        {
            for (int i = size / 2; i < Segments; i += size)
            {
                ys[i] = ((ys[i - (size / 2)] + ys[i + (size / 2)]) / 2) + ((random.NextDouble() - 0.5) * displacement);
            }

            displacement *= roughness;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, bottom), true, true);
            for (int i = 0; i <= Segments; i++)
            {
                ctx.LineTo(new Point(w * i / (double)Segments, Math.Clamp(ys[i], highest - ((lowest - highest) * 0.3), bottom)), true, true);
            }

            ctx.LineTo(new Point(w, bottom), true, true);
        }

        geometry.Freeze();
        return new System.Windows.Shapes.Path
        {
            Data = geometry,
            Fill = new LinearGradientBrush(color, Mix(color, Colors.Black, 0.35), 90),
        };
    }

    /// <summary>針葉樹の林（丘の稜線に、高さの違う木を並べる）。</summary>
    private static System.Windows.Shapes.Path Forest(int w, int h, double highest, double lowest, Color color, double k, Random random)
    {
        var ridge = new Noise(random, w / 5.0);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            // 丘
            ctx.BeginFigure(new Point(0, h), true, true);
            for (double x = 0; x <= w; x += 8)
            {
                ctx.LineTo(new Point(x, Lerp(highest, lowest, ridge.At(x))), true, false);
            }

            ctx.LineTo(new Point(w, h), true, false);

            // 木
            for (double x = -10; x < w + 10; x += (5 + (random.NextDouble() * 12)) * k)
            {
                double ground = Lerp(highest, lowest, ridge.At(x)) + (6 * k);
                double height = (36 + (Math.Pow(random.NextDouble(), 1.5) * 90)) * k;
                double width = height * (0.22 + (random.NextDouble() * 0.08));
                int tiers = 6 + random.Next(4);
                ctx.BeginFigure(new Point(x, ground - height), true, true);
                for (int i = 1; i <= tiers; i++)
                {
                    double ty = ground - height + (height * i / tiers);
                    double tw = width * i / tiers;
                    ctx.LineTo(new Point(x + tw, ty), true, false);
                    if (i < tiers)
                    {
                        ctx.LineTo(new Point(x + (tw * 0.45), ty), true, false);
                    }
                }

                ctx.LineTo(new Point(x + (width * 0.08), ground), true, false);
                ctx.LineTo(new Point(x + (width * 0.08), ground + (10 * k)), true, false);
                ctx.LineTo(new Point(x - (width * 0.08), ground + (10 * k)), true, false);
                ctx.LineTo(new Point(x - (width * 0.08), ground), true, false);
                for (int i = tiers; i >= 1; i--)
                {
                    double ty = ground - height + (height * i / tiers);
                    double tw = width * i / tiers;
                    if (i < tiers)
                    {
                        ctx.LineTo(new Point(x - (tw * 0.45), ty), true, false);
                    }

                    ctx.LineTo(new Point(x - tw, ty), true, false);
                }
            }
        }

        geometry.Freeze();
        return new System.Windows.Shapes.Path { Data = geometry, Fill = new SolidColorBrush(color) };
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    private static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Lerp(a.R, b.R, t), (byte)Lerp(a.G, b.G, t), (byte)Lerp(a.B, b.B, t));
}

/// <summary>1 次元のなめらかな乱数（一定の間隔で決めた値を、なめらかにつなぐ）。</summary>
internal sealed class Noise(Random random, double spacing)
{
    private readonly Dictionary<long, double> _knots = [];

    public double At(double x)
    {
        double p = x / spacing;
        long i = (long)Math.Floor(p);
        double t = p - i;
        t = t * t * (3 - (2 * t));
        return (Knot(i) * (1 - t)) + (Knot(i + 1) * t);
    }

    private double Knot(long i)
    {
        if (!_knots.TryGetValue(i, out double value))
        {
            value = random.NextDouble();
            _knots[i] = value;
        }

        return value;
    }
}
