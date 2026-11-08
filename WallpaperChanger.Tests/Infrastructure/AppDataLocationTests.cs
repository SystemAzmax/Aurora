using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Infrastructure;
using WallpaperChanger.Infrastructure.History;
using WallpaperChanger.Infrastructure.Imaging;
using WallpaperChanger.Infrastructure.Logging;
using WallpaperChanger.Infrastructure.Settings;
using WallpaperChanger.Infrastructure.Startup;

namespace WallpaperChanger.Tests.Infrastructure;

/// <summary>
/// <see cref="AppDataLocation"/> のテスト。テストはパッケージなしで実行されるため、exe 版の保存先になることを確かめる。
/// </summary>
public class AppDataLocationTests
{
    private static readonly string ExeDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperChanger");

    [Fact]
    public void パッケージなしで実行した場合はLocalAppDataのフォルダーに保存する()
    {
        Assert.False(AppDataLocation.IsPackaged);
        Assert.Equal(ExeDataDirectory, AppDataLocation.Directory);
    }

    [Fact]
    public void 設定と履歴と合成画像とログは同じフォルダーに保存する()
    {
        Assert.Equal(Path.Combine(ExeDataDirectory, "settings.json"), SettingsStorageOptions.DefaultFilePath);
        Assert.Equal(Path.Combine(ExeDataDirectory, "history.json"), HistoryStorageOptions.DefaultFilePath);
        Assert.Equal(Path.Combine(ExeDataDirectory, "Composites"), WallpaperCompositionOptions.DefaultOutputDirectory);
        Assert.Equal(Path.Combine(ExeDataDirectory, "logs"), FileLoggerOptions.DefaultDirectory);
    }

    [Fact]
    public void パッケージなしで実行した場合は自動起動をRunキーで登録する()
    {
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddWallpaperChangerInfrastructure();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<RegistryStartupRegistrationService>(provider.GetRequiredService<IStartupRegistrationService>());
    }
}
