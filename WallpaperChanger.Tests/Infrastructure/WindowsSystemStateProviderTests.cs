using WallpaperChanger.Infrastructure.SystemState;

namespace WallpaperChanger.Tests.Infrastructure;

public class WindowsSystemStateProviderTests
{
    [Theory]
    [InlineData(QueryUserNotificationState.Busy, true)]
    [InlineData(QueryUserNotificationState.RunningD3DFullScreen, true)]
    [InlineData(QueryUserNotificationState.PresentationMode, true)]
    [InlineData(QueryUserNotificationState.AcceptsNotifications, false)]
    [InlineData(QueryUserNotificationState.NotPresent, false)] // ロック中・スクリーンセーバーの表示中
    [InlineData(QueryUserNotificationState.QuietTime, false)]
    [InlineData(QueryUserNotificationState.App, false)]
    internal void 全画面のアプリとプレゼンテーションのときだけ全画面とみなす(QueryUserNotificationState state, bool expected)
    {
        Assert.Equal(expected, WindowsSystemStateProvider.IsFullScreen(state));
    }

    [Theory]
    [InlineData((byte)0, true)]
    [InlineData((byte)1, false)]
    [InlineData((byte)255, false)] // 不明
    public void AC電源につながっていないときだけバッテリー駆動とみなす(byte acLineStatus, bool expected)
    {
        Assert.Equal(expected, WindowsSystemStateProvider.IsOnBattery(new SystemPowerStatus { ACLineStatus = acLineStatus }));
    }

    [Fact]
    public void 実際の状態を例外なく取得できる()
    {
        var sut = new WindowsSystemStateProvider();

        // 値は実行環境によるため、呼び出せることだけを確かめる
        _ = sut.IsFullScreenAppRunning();
        _ = sut.IsOnBatteryPower();
    }
}
