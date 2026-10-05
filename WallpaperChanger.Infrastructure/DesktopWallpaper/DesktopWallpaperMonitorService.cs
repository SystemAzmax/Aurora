using System.ComponentModel;
using Microsoft.Extensions.Logging;
using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;
using WallpaperChanger.Infrastructure.DesktopWallpaper.Interop;

namespace WallpaperChanger.Infrastructure.DesktopWallpaper;

/// <summary>
/// IDesktopWallpaper でモニターを列挙し、Display Configuration API の情報で名前と解像度を補完する。
/// </summary>
internal sealed partial class DesktopWallpaperMonitorService(
    DesktopWallpaperComHost comHost,
    IDisplayDeviceInfoProvider displayDeviceInfoProvider,
    ILogger<DesktopWallpaperMonitorService> logger) : IMonitorService
{
    private readonly DesktopWallpaperComHost _comHost = comHost ?? throw new ArgumentNullException(nameof(comHost));
    private readonly IDisplayDeviceInfoProvider _displayDeviceInfoProvider =
        displayDeviceInfoProvider ?? throw new ArgumentNullException(nameof(displayDeviceInfoProvider));
    private readonly ILogger<DesktopWallpaperMonitorService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<MonitorInfo>> GetMonitorsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<(uint Index, string Id, NativeRect Rect)> attached =
            await _comHost.InvokeAsync(EnumerateAttachedMonitors, cancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, DisplayDeviceInfo> displays = GetDisplayDevicesOrEmpty();

        // 画面の配置どおり（左→右、上→下）に並べる
        return
        [
            .. attached
                .OrderBy(m => m.Rect.Left)
                .ThenBy(m => m.Rect.Top)
                .Select((m, order) => CreateMonitorInfo(m.Index, m.Id, m.Rect, order + 1, displays)),
        ];
    }

    private List<(uint Index, string Id, NativeRect Rect)> EnumerateAttachedMonitors(IDesktopWallpaper wallpaper)
    {
        uint count = wallpaper.GetMonitorDevicePathCount();
        var monitors = new List<(uint, string, NativeRect)>((int)count);

        for (uint i = 0; i < count; i++)
        {
            string id = wallpaper.GetMonitorDevicePathAt(i);

            // 過去に接続されていたがデスクトップに割り当てられていないモニターも列挙されるため除外する
            int hr = wallpaper.GetMonitorRECT(id, out NativeRect rect);
            if (hr != 0)
            {
                LogMonitorNotAttached(id, hr);
                continue;
            }

            monitors.Add((i, id, rect));
        }

        return monitors;
    }

    private IReadOnlyDictionary<string, DisplayDeviceInfo> GetDisplayDevicesOrEmpty()
    {
        try
        {
            return _displayDeviceInfoProvider.GetActiveDisplays();
        }
        catch (Win32Exception ex)
        {
            // 名前と解像度は補助情報のため、取得できなくても既定の表示名と領域サイズで継続する
            LogDisplayInfoFailed(ex);
            return new Dictionary<string, DisplayDeviceInfo>();
        }
    }

    private static MonitorInfo CreateMonitorInfo(
        uint index,
        string id,
        NativeRect rect,
        int number,
        IReadOnlyDictionary<string, DisplayDeviceInfo> displays)
    {
        var bounds = new MonitorBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
        displays.TryGetValue(id, out DisplayDeviceInfo? display);

        // 内蔵パネルは EDID にモデル名を持たないことが多い
        string name = display switch
        {
            { FriendlyName.Length: > 0 } => display.FriendlyName,
            { IsInternal: true } => "内蔵ディスプレイ",
            _ => $"ディスプレイ {number}",
        };

        int width = display is { Width: > 0 } ? display.Width : bounds.Width;
        int height = display is { Height: > 0 } ? display.Height : bounds.Height;

        return new MonitorInfo(id, (int)index, name, bounds, width, height);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "デスクトップに割り当てられていないモニターを除外しました: {MonitorId} (HRESULT: 0x{HResult:X8})")]
    private partial void LogMonitorNotAttached(string monitorId, int hresult);

    [LoggerMessage(Level = LogLevel.Warning, Message = "モニター名・解像度の取得に失敗したため既定の表示名を使用します")]
    private partial void LogDisplayInfoFailed(Exception exception);
}
