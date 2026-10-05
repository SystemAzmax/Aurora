using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Services;

namespace WallpaperChanger.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Core 層のサービスを登録する。
    /// 状態（履歴・タイマー）を持つものだけを Singleton とし、状態を持たない戦略は Transient とする。
    /// </summary>
    public static IServiceCollection AddWallpaperChangerCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services.AddTransient<IImageSelector, RandomImageSelector>();
        services.AddTransient<IImageSelector, SequentialImageSelector>();

        services.AddSingleton<IWallpaperRotationService, WallpaperRotationService>();
        services.AddSingleton<IWallpaperScheduler, WallpaperScheduler>();
        services.AddSingleton<IMonitorConfigurationWatcher, MonitorConfigurationWatcher>();

        return services;
    }
}
