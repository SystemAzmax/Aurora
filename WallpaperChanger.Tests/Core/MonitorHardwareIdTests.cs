using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Tests.Core;

public class MonitorHardwareIdTests
{
    [Theory]
    [InlineData(@"\\?\DISPLAY#GSM7666#5&14b0cd61&0&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", "GSM7666")]
    [InlineData(@"\\?\display#gsm7666#5&14b0cd61&0&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", "GSM7666")]
    [InlineData(@"\\?\DISPLAY#Default_Monitor#5&14b0cd61&0&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", null)]
    [InlineData(@"\\?\DISPLAY##5&14b0cd61&0&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", null)]
    [InlineData("MON1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void モニターのIDから型番を取り出す(string? monitorId, string? expected)
    {
        Assert.Equal(expected, MonitorHardwareId.FromMonitorId(monitorId));
    }

    [Fact]
    public void 接続先だけが違うモニターは同じ型番になる()
    {
        string port1 = @"\\?\DISPLAY#GSM7666#5&14b0cd61&0&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        string port2 = @"\\?\DISPLAY#GSM7666#5&10ece667&0&UID24832#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

        Assert.Equal(MonitorHardwareId.FromMonitorId(port1), MonitorHardwareId.FromMonitorId(port2));
    }
}
