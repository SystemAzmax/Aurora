using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Infrastructure.Resources;

namespace WallpaperChanger.Infrastructure.Startup;

/// <summary>
/// パッケージ（MSIX）で動くときに、マニフェストのスタートアップ タスク（windows.startupTask）で自動起動を切り替える。
/// </summary>
/// <remarks>
/// パッケージで動くアプリの HKCU への書き込みはパッケージ専用の場所に振り替えられるため、Run キーでは自動起動できない。
/// ユーザーがタスク マネージャーや Windows の設定で無効にした場合（<see cref="StartupTaskState.DisabledByUser"/>）は、
/// アプリからは有効に戻せないため、Windows の設定で有効にするよう案内する。
/// </remarks>
internal sealed partial class PackagedStartupRegistrationService(ILogger<PackagedStartupRegistrationService> logger)
    : IStartupRegistrationService
{
    /// <summary>Package.appxmanifest の desktop:StartupTask の TaskId と同じ値。</summary>
    internal const string TaskId = "AuroraWallpaperChangerStartup";

    private readonly ILogger<PackagedStartupRegistrationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public bool IsEnabled() =>
        GetTask().State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    public void Enable()
    {
        StartupTask task = GetTask();

        // パッケージで動くデスクトップ アプリでは、有効にするときに確認のダイアログは出ない
        StartupTaskState state = task.State == StartupTaskState.Disabled
            ? Wait(task.RequestEnableAsync().AsTask())
            : task.State;
        switch (state)
        {
            case StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy:
                LogEnabled(state);
                return;
            case StartupTaskState.DisabledByUser:
                throw new InvalidOperationException(Strings.StartupDisabledByUser);
            default:
                throw new InvalidOperationException(Strings.StartupDisabledByPolicy);
        }
    }

    public void Disable()
    {
        StartupTask task = GetTask();
        if (task.State == StartupTaskState.Enabled)
        {
            task.Disable();
        }

        LogDisabled();
    }

    /// <summary>スタートアップ タスクは実行ファイルの場所をパッケージが管理するため、登録し直すことはない。</summary>
    public bool UpdateRegisteredPathIfNeeded() => false;

    private static StartupTask GetTask()
    {
        try
        {
            return Wait(StartupTask.GetAsync(TaskId).AsTask());
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            // マニフェストにタスクが無いなど。画面で理由を伝えられるよう、自動起動の失敗として扱う
            throw new InvalidOperationException(
                InfrastructureLocalization.Format(Strings.StartupTaskUnavailableFormat, ex.Message), ex);
        }
    }

    /// <summary>
    /// 設定画面のチェックの切り替えから同期的に呼ばれるため、完了を待つ。
    /// 完了の通知は UI スレッドを使わないため、UI スレッドで待っても止まらない。
    /// </summary>
    private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

    [LoggerMessage(Level = LogLevel.Information, Message = "Windows 起動時の自動起動を有効にしました（スタートアップ タスク: {State}）")]
    private partial void LogEnabled(StartupTaskState state);

    [LoggerMessage(Level = LogLevel.Information, Message = "Windows 起動時の自動起動を無効にしました（スタートアップ タスク）")]
    private partial void LogDisabled();
}
