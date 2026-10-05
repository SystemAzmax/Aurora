using System.Runtime.InteropServices;

namespace WallpaperChanger.Infrastructure.DesktopWallpaper.Interop;

/// <summary>
/// wingdi.h / winuser.h の Display Configuration API。
/// モニターのフレンドリ名と解像度を取得するために使用する（壁紙の設定には使用しない）。
/// </summary>
internal static class NativeMethods
{
    public const uint QdcOnlyActivePaths = 0x00000002;
    public const int ErrorSuccess = 0;
    public const int ErrorInsufficientBuffer = 122;
    public const uint DisplayConfigModeInfoTypeSource = 1;
    public const uint DisplayConfigDeviceInfoGetTargetName = 2;
    public const uint DisplayConfigOutputTechnologyInternal = 0x80000000;

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);
}

[StructLayout(LayoutKind.Sequential)]
internal struct Luid
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigRational
{
    public uint Numerator;
    public uint Denominator;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathSourceInfo
{
    public Luid AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathTargetInfo
{
    public Luid AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public uint OutputTechnology;
    public uint Rotation;
    public uint Scaling;
    public DisplayConfigRational RefreshRate;
    public uint ScanLineOrdering;
    public int TargetAvailable;
    public uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathInfo
{
    public DisplayConfigPathSourceInfo SourceInfo;
    public DisplayConfigPathTargetInfo TargetInfo;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfig2DRegion
{
    public uint Cx;
    public uint Cy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigVideoSignalInfo
{
    public ulong PixelRate;
    public DisplayConfigRational HSyncFreq;
    public DisplayConfigRational VSyncFreq;
    public DisplayConfig2DRegion ActiveSize;
    public DisplayConfig2DRegion TotalSize;
    public uint VideoStandard;
    public uint ScanLineOrdering;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PointL
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigSourceMode
{
    public uint Width;
    public uint Height;
    public uint PixelFormat;
    public PointL Position;
}

/// <summary>DISPLAYCONFIG_MODE_INFO (64 bytes)。union 部分は FieldOffset で重ねる。</summary>
[StructLayout(LayoutKind.Explicit)]
internal struct DisplayConfigModeInfo
{
    [FieldOffset(0)]
    public uint InfoType;

    [FieldOffset(4)]
    public uint Id;

    [FieldOffset(8)]
    public Luid AdapterId;

    [FieldOffset(16)]
    public DisplayConfigVideoSignalInfo TargetMode;

    [FieldOffset(16)]
    public DisplayConfigSourceMode SourceMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigDeviceInfoHeader
{
    public uint Type;
    public uint Size;
    public Luid AdapterId;
    public uint Id;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigTargetDeviceName
{
    public DisplayConfigDeviceInfoHeader Header;
    public uint Flags;
    public uint OutputTechnology;
    public ushort EdidManufactureId;
    public ushort EdidProductCodeId;
    public uint ConnectorInstance;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string MonitorFriendlyDeviceName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string MonitorDevicePath;
}
