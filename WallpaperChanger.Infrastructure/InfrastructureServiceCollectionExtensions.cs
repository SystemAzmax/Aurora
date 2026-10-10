using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Infrastructure.DesktopWallpaper;
using WallpaperChanger.Infrastructure.FileSystem;
using WallpaperChanger.Infrastructure.Imaging;
using WallpaperChanger.Infrastructure.Settings;
using WallpaperChanger.Infrastructure.Startup;

namespace WallpaperChanger.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Infrastructure 層のサービスを登録する。
    /// STA スレッドを所有する COM ホストと、現在の設定を保持する設定サービスのみ Singleton とする。
    /// </summary>
    public static IServiceCollection AddWallpaperChangerInfrastructure(
        this IServiceCollection services,
        Action<SettingsStorageOptions>? configureSettingsStorage = null,
        Action<WallpaperCompositionOptions>? configureComposition = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<SettingsStorageOptions>();
        if (configureSettingsStorage is not null)
        {
            optionsBuilder.Configure(configureSettingsStorage);
        }

        services.AddOptions<StartupRegistrationOptions>();
        services.AddOptions<ImageSourceLimits>();
        services.AddOptions<DesktopWallpaperOptions>();

        var compositionOptions = services.AddOptions<WallpaperCompositionOptions>();
        if (configureComposition is not null)
        {
            compositionOptions.Configure(configureComposition);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<DesktopWallpaperComHost>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();

        services.AddTransient<IDisplayDeviceInfoProvider, DisplayConfigDeviceInfoProvider>();
        services.AddTransient<IMonitorService, DesktopWallpaperMonitorService>();
        services.AddTransient<IDisplayChangeNotifier, SystemEventsDisplayChangeNotifier>();
        services.AddTransient<IWallpaperService, DesktopWallpaperService>();
        services.AddTransient<IImageProvider, FileSystemImageProvider>();
        services.AddTransient<IWallpaperComposer, WicWallpaperComposer>();
        services.AddTransient<IStartupRegistrationService, RegistryStartupRegistrationService>();

        return services;
    }
}
