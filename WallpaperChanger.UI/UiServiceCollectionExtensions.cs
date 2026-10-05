using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.UI.Services;
using WallpaperChanger.UI.ViewModels;

namespace WallpaperChanger.UI;

public static class UiServiceCollectionExtensions
{
    /// <summary>
    /// UI 層のサービスと ViewModel を登録する。
    /// アプリ常駐中に 1 つだけ存在する UI 資源（トレイアイコン・設定画面の管理）のみ Singleton とする。
    /// </summary>
    public static IServiceCollection AddWallpaperChangerUI(this IServiceCollection services, Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dispatcher);

        services.AddSingleton<IUiDispatcher>(new WpfUiDispatcher(dispatcher));
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<ISettingsWindowService, SettingsWindowService>();

        services.AddTransient<IFolderPickerService, FolderPickerService>();
        services.AddTransient<IImagePreviewLoader, ImagePreviewLoader>();
        services.AddTransient<IFolderLauncher, ExplorerFolderLauncher>();

        // 表示中の番号ウィンドウを保持するため、設定画面ごとのスコープで 1 つ。画面を閉じると番号も閉じる。
        services.AddScoped<IMonitorIdentifierService, MonitorIdentifierService>();

        // TrayIconViewModel は TrayIconService が 1 度だけ解決する。SettingsViewModel は画面ごとのスコープで生成・破棄する。
        services.AddTransient<TrayIconViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddHostedService<ApplicationHostedService>();

        return services;
    }
}
