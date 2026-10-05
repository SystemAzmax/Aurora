using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WallpaperChanger.Core;
using WallpaperChanger.Infrastructure;
using WallpaperChanger.Infrastructure.Logging;

namespace WallpaperChanger.UI;

/// <summary>
/// エントリーポイント兼コンポジションルート。
/// WPF の App と Generic Host を組み合わせ、ホストの開始・終了を WPF のライフサイクルに合わせる。
/// </summary>
internal static partial class Program
{
    private const string AppName = "壁紙チェンジャー";
    private const string SingleInstanceMutexName = @"Local\WallpaperChanger.SingleInstance.7B0E3C4A";
    private static readonly TimeSpan HostStopTimeout = TimeSpan.FromSeconds(5);

    [STAThread]
    public static int Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "壁紙チェンジャーは既に起動しています。\nタスクトレイのアイコンから操作してください。",
                AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }

        var app = new App();
        app.InitializeComponent();

        using IHost host = CreateHost(args, app.Dispatcher);
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program).FullName!);
        RegisterGlobalExceptionHandlers(app, logger);

        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        app.Startup += async (_, _) => await RunHostAsync(host, app, logger);
        app.SessionEnding += (_, _) => lifetime.StopApplication();

        int exitCode = app.Run();

        // サインアウト時などは WPF が先に終了するため、ホストの終了処理（自動切り替えの停止・ログの書き出し等）の完了を待つ。
        // トレイの「終了」から終了した場合は既に完了しており、すぐに戻る。
        StopHost(host, logger);
        return exitCode;
    }

    private static void StopHost(IHost host, ILogger logger)
    {
        // UI スレッドの SynchronizationContext は既に停止しているため、継続をスレッドプールで実行させる
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            if (!host.StopAsync().Wait(HostStopTimeout))
            {
                LogHostStopTimeout(logger, HostStopTimeout);
            }
        }
        catch (AggregateException ex)
        {
            LogHostFailed(logger, ex);
        }
    }

    private static IHost CreateHost(string[] args, Dispatcher dispatcher)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ApplicationName = "WallpaperChanger",
            // スタートアップ登録などで作業フォルダが異なっても appsettings.json を見つけられるようにする
            ContentRootPath = AppContext.BaseDirectory,
        });

        // DI の登録漏れ・スコープ違反を起動時に検出する
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        // 配布先での調査用に、ログを %LOCALAPPDATA%\WallpaperChanger\logs にも保存する
        builder.Logging.AddWallpaperChangerFile();

        builder.Services
            .AddWallpaperChangerCore()
            .AddWallpaperChangerInfrastructure()
            .AddWallpaperChangerUI(dispatcher);

        return builder.Build();
    }

    /// <summary>
    /// ホストを開始し、IHostApplicationLifetime.StopApplication（トレイの「終了」）まで待機してから WPF を終了する。
    /// </summary>
    private static async Task RunHostAsync(IHost host, Application app, ILogger logger)
    {
        int exitCode = 0;
        try
        {
            await host.StartAsync();
            await host.WaitForShutdownAsync();
        }
        catch (Exception ex)
        {
            LogHostFailed(logger, ex);
            MessageBox.Show($"エラーが発生したため終了します。\n\n{ex.Message}", AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            exitCode = 1;
        }

        app.Shutdown(exitCode);
    }

    private static void RegisterGlobalExceptionHandlers(Application app, ILogger logger)
    {
        // 想定外の例外は記録したうえで既定の動作（プロセス終了）に任せる
        app.DispatcherUnhandledException += (_, e) => LogUnhandledException(logger, e.Exception, "UI スレッド");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogUnhandledException(logger, ex, "AppDomain");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) => LogUnhandledException(logger, e.Exception, "未監視の Task");
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "アプリケーションの実行中にエラーが発生しました")]
    private static partial void LogHostFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "終了処理が {Timeout} 以内に完了しませんでした")]
    private static partial void LogHostStopTimeout(ILogger logger, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Critical, Message = "ハンドルされない例外が発生しました ({Source})")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception, string source);
}
