namespace WallpaperChanger.Core.Models;

/// <summary>
/// モニター構成の変化の内容。
/// </summary>
public sealed class MonitorConfigurationChangedEventArgs(
    IReadOnlyList<MonitorInfo> added,
    IReadOnlyList<MonitorInfo> removed,
    IReadOnlyList<MonitorInfo> resized,
    IReadOnlyList<MonitorInfo> current) : EventArgs
{
    /// <summary>新しく接続されたモニター。</summary>
    public IReadOnlyList<MonitorInfo> Added { get; } = added;

    /// <summary>切断されたモニター（変化前の情報）。</summary>
    public IReadOnlyList<MonitorInfo> Removed { get; } = removed;

    /// <summary>解像度が変わったモニター（変化後の情報）。</summary>
    public IReadOnlyList<MonitorInfo> Resized { get; } = resized;

    /// <summary>変化後の全モニター。</summary>
    public IReadOnlyList<MonitorInfo> Current { get; } = current;
}
