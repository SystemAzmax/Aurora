namespace WallpaperChanger.Core.Models;

/// <summary>
/// 壁紙の切り替えの進み具合。モニターを 1 台ずつ処理するため、台数が多いと終わるまで時間がかかる。
/// </summary>
/// <param name="completedCount">処理を終えたモニターの数。</param>
/// <param name="totalCount">対象のモニターの数。</param>
/// <param name="currentMonitor">処理中のモニター。すべて終えた（失敗・キャンセルを含む）ときは null。</param>
public sealed class WallpaperChangeProgressEventArgs(int completedCount, int totalCount, MonitorInfo? currentMonitor) : EventArgs
{
    public int CompletedCount { get; } = completedCount;

    public int TotalCount { get; } = totalCount;

    public MonitorInfo? CurrentMonitor { get; } = currentMonitor;

    /// <summary>切り替えが終わったか。</summary>
    public bool IsFinished => CurrentMonitor is null;
}
