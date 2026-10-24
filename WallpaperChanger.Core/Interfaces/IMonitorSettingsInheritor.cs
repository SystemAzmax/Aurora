using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// 接続先のポートを変えたモニター（ID が変わったモニター）に、それまでの設定と履歴を引き継ぐ。
/// </summary>
public interface IMonitorSettingsInheritor
{
    /// <summary>
    /// 接続中のモニターのうち、壁紙フォルダを設定しておらず、同じ型番の接続していないモニターの設定が 1 つだけあるものに、
    /// その設定と履歴を移す。同じ型番のモニターが複数あって対応が決まらない場合は引き継がない。
    /// あわせて、接続中のモニターの名前を設定に記録する。
    /// </summary>
    /// <param name="connectedMonitors">接続中の全モニター。</param>
    /// <returns>設定を引き継いだモニター。</returns>
    Task<IReadOnlyList<MonitorInfo>> InheritAsync(IReadOnlyList<MonitorInfo> connectedMonitors, CancellationToken cancellationToken = default);
}
