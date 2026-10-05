using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// 接続中モニターの情報を提供する。
/// </summary>
public interface IMonitorService
{
    /// <summary>現在接続中（デスクトップに割り当て済み）のモニター一覧を取得する。</summary>
    Task<IReadOnlyList<MonitorInfo>> GetMonitorsAsync(CancellationToken cancellationToken = default);
}
