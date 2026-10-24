namespace WallpaperChanger.Core.Models;

/// <summary>
/// モニターの ID（デバイスパス）に含まれる型番（ハードウェア ID）。
/// </summary>
/// <remarks>
/// IDesktopWallpaper のモニター ID は「\\?\DISPLAY#GSM7666#5&amp;14b0cd61&amp;0&amp;UID4355#{...}」の形で、
/// 2 つ目の要素が EDID のメーカー ID と製品コード、3 つ目が接続先（ポート）ごとの識別子になる。
/// 接続先を変えると 3 つ目だけが変わるため、2 つ目で同じ型番のモニターかを判断する。
/// </remarks>
public static class MonitorHardwareId
{
    /// <summary>EDID を読めなかったモニターに付く型番。別のモニターでも同じになるため、型番として扱わない。</summary>
    private const string GenericMonitor = "Default_Monitor";

    /// <summary>モニターの ID から型番を取り出す（大文字にそろえる）。デバイスパスの形でない場合や、型番が分からない場合は null。</summary>
    public static string? FromMonitorId(string? monitorId)
    {
        if (string.IsNullOrWhiteSpace(monitorId))
        {
            return null;
        }

        string[] parts = monitorId.Split('#');
        if (parts.Length < 3
            || !parts[0].EndsWith("DISPLAY", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parts[1])
            || parts[1].Equals(GenericMonitor, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return parts[1].ToUpperInvariant();
    }
}
