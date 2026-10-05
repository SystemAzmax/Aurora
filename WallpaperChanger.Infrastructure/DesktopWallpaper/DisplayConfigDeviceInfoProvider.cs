using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WallpaperChanger.Infrastructure.DesktopWallpaper.Interop;

namespace WallpaperChanger.Infrastructure.DesktopWallpaper;

/// <summary>
/// QueryDisplayConfig / DisplayConfigGetDeviceInfo で表示デバイスの情報を取得する。
/// </summary>
internal sealed partial class DisplayConfigDeviceInfoProvider(ILogger<DisplayConfigDeviceInfoProvider> logger)
    : IDisplayDeviceInfoProvider
{
    private readonly ILogger<DisplayConfigDeviceInfoProvider> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public IReadOnlyDictionary<string, DisplayDeviceInfo> GetActiveDisplays()
    {
        (DisplayConfigPathInfo[] paths, DisplayConfigModeInfo[] modes) = QueryActivePaths();

        var result = new Dictionary<string, DisplayDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (DisplayConfigPathInfo path in paths)
        {
            var request = new DisplayConfigTargetDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = NativeMethods.DisplayConfigDeviceInfoGetTargetName,
                    Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                    AdapterId = path.TargetInfo.AdapterId,
                    Id = path.TargetInfo.Id,
                },
            };

            int error = NativeMethods.DisplayConfigGetDeviceInfo(ref request);
            if (error != NativeMethods.ErrorSuccess || string.IsNullOrEmpty(request.MonitorDevicePath))
            {
                LogTargetNameFailed(path.TargetInfo.Id, error);
                continue;
            }

            (int width, int height) = GetSourceResolution(path, modes);
            result[request.MonitorDevicePath] = new DisplayDeviceInfo(
                request.MonitorDevicePath,
                (request.MonitorFriendlyDeviceName ?? string.Empty).Trim(),
                width,
                height,
                request.OutputTechnology == NativeMethods.DisplayConfigOutputTechnologyInternal);
        }

        return result;
    }

    private static (DisplayConfigPathInfo[] Paths, DisplayConfigModeInfo[] Modes) QueryActivePaths()
    {
        int error;
        DisplayConfigPathInfo[] paths;
        DisplayConfigModeInfo[] modes;
        uint pathCount;
        uint modeCount;

        // バッファ取得から取得までの間に構成が変わると ERROR_INSUFFICIENT_BUFFER になるため再試行する
        do
        {
            error = NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QdcOnlyActivePaths, out pathCount, out modeCount);
            if (error != NativeMethods.ErrorSuccess)
            {
                throw new Win32Exception(error, "GetDisplayConfigBufferSizes に失敗しました。");
            }

            paths = new DisplayConfigPathInfo[pathCount];
            modes = new DisplayConfigModeInfo[modeCount];
            error = NativeMethods.QueryDisplayConfig(
                NativeMethods.QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        }
        while (error == NativeMethods.ErrorInsufficientBuffer);

        if (error != NativeMethods.ErrorSuccess)
        {
            throw new Win32Exception(error, "QueryDisplayConfig に失敗しました。");
        }

        return (paths[..(int)pathCount], modes[..(int)modeCount]);
    }

    private static (int Width, int Height) GetSourceResolution(DisplayConfigPathInfo path, DisplayConfigModeInfo[] modes)
    {
        uint index = path.SourceInfo.ModeInfoIdx;
        if (index < modes.Length && modes[index].InfoType == NativeMethods.DisplayConfigModeInfoTypeSource)
        {
            DisplayConfigSourceMode source = modes[index].SourceMode;
            return ((int)source.Width, (int)source.Height);
        }

        return (0, 0);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "表示デバイス名の取得に失敗しました (TargetId: {TargetId}, Error: {Error})")]
    private partial void LogTargetNameFailed(uint targetId, int error);
}
