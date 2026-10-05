using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Core.Services;
using WallpaperChanger.Infrastructure.FileSystem;
using WallpaperChanger.Tests.Fakes;

namespace WallpaperChanger.Tests.Integration;

/// <summary>
/// 実際のファイル列挙と選択戦略を組み合わせ、4 分割で選ばれる画像を検証する（COM・合成はフェイク）。
/// </summary>
public sealed class GridSelectionIntegrationTests : IDisposable
{
    private static readonly MonitorInfo Monitor = new("MON", 0, "Monitor", new MonitorBounds(0, 0, 1920, 1080), 1920, 1080);

    private readonly TempDirectory _temp = new();
    private readonly IWallpaperComposer _composer = Substitute.For<IWallpaperComposer>();
    private readonly List<IReadOnlyList<string?>> _composed = [];

    public GridSelectionIntegrationTests()
    {
        _composer.ComposeAsync(Arg.Do<WallpaperCompositionRequest>(r => _composed.Add(r.ImagePaths)), Arg.Any<CancellationToken>())
            .Returns(_ => _temp.CreateFile($"composite{_composed.Count}.jpg"));
    }

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(ImageSelectionMode.Random)]
    [InlineData(ImageSelectionMode.Sequential)]
    public async Task 共通フォルダ4つに各2枚あれば4マスとも別の画像になる(ImageSelectionMode mode)
    {
        string[] folders = [.. Enumerable.Range(1, 4).Select(i => Path.Combine(_temp.Path, $"0{i}"))];
        foreach (string folder in folders)
        {
            _temp.CreateFile(Path.Combine(folder, "a.jpg"));
            _temp.CreateFile(Path.Combine(folder, "b.jpg"));
        }

        MonitorSettings monitor = folders.Aggregate(MonitorSettings.CreateDefault("MON"), (m, f) => m.AddFolder(f))
            with { Layout = WallpaperLayout.Grid2x2, SelectionMode = mode };
        using WallpaperRotationService sut = CreateSut(new AppSettings().WithMonitor(monitor));

        for (int i = 0; i < 20; i++)
        {
            await sut.NextAsync("MON", TestContext.Current.CancellationToken);
        }

        Assert.Equal(20, _composed.Count);
        Assert.All(_composed, images =>
        {
            Assert.Equal(4, images.Count);
            Assert.Equal(4, images.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        });
    }

    private WallpaperRotationService CreateSut(AppSettings settings)
    {
        var monitors = Substitute.For<IMonitorService>();
        monitors.GetMonitorsAsync(Arg.Any<CancellationToken>()).Returns([Monitor]);

        return new WallpaperRotationService(
            monitors,
            Substitute.For<IWallpaperService>(),
            new FileSystemImageProvider(NullLogger<FileSystemImageProvider>.Instance),
            _composer,
            new InMemorySettingsService(settings),
            [new RandomImageSelector(), new SequentialImageSelector()],
            NullLogger<WallpaperRotationService>.Instance);
    }
}
