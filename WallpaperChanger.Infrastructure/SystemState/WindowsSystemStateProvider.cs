using System.Runtime.InteropServices;
using WallpaperChanger.Core.Interfaces;

namespace WallpaperChanger.Infrastructure.SystemState;

/// <summary>
/// 全画面表示は <c>SHQueryUserNotificationState</c>、電源は <c>GetSystemPowerStatus</c> で調べる。
/// どちらも呼び出すたびに今の状態を返す軽い API のため、結果は保持しない。
/// </summary>
internal sealed class WindowsSystemStateProvider : ISystemStateProvider
{
    public bool IsFullScreenAppRunning() =>
        NativeMethods.SHQueryUserNotificationState(out QueryUserNotificationState state) >= 0
        && IsFullScreen(state);

    public bool IsOnBatteryPower() =>
        NativeMethods.GetSystemPowerStatus(out SystemPowerStatus status) && IsOnBattery(status);

    /// <summary>
    /// 全画面のアプリ（Direct3D の排他的な全画面を含む）やプレゼンテーション モードのときに true。
    /// 画面のロック中・スクリーンセーバーの表示中（<see cref="QueryUserNotificationState.NotPresent"/>）や、
    /// 集中モード（<see cref="QueryUserNotificationState.QuietTime"/>）は、作業の邪魔にならないため含めない。
    /// </summary>
    internal static bool IsFullScreen(QueryUserNotificationState state) =>
        state is QueryUserNotificationState.Busy
            or QueryUserNotificationState.RunningD3DFullScreen
            or QueryUserNotificationState.PresentationMode;

    /// <summary>AC 電源につながっていないとき true。つながっているか分からない（255）ときやバッテリーの無い PC では false。</summary>
    internal static bool IsOnBattery(SystemPowerStatus status) => status.ACLineStatus == 0;

    private static class NativeMethods
    {
        [DllImport("shell32.dll", ExactSpelling = true)]
        public static extern int SHQueryUserNotificationState(out QueryUserNotificationState state);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    }
}

/// <summary>QUERY_USER_NOTIFICATION_STATE（shellapi.h）。</summary>
internal enum QueryUserNotificationState
{
    NotPresent = 1,
    Busy = 2,
    RunningD3DFullScreen = 3,
    PresentationMode = 4,
    AcceptsNotifications = 5,
    QuietTime = 6,
    App = 7,
}

/// <summary>SYSTEM_POWER_STATUS（winbase.h）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemPowerStatus
{
    /// <summary>0: バッテリー、1: AC 電源、255: 不明。</summary>
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public int BatteryLifeTime;
    public int BatteryFullLifeTime;
}
