using WallpaperChanger.Core.Models;
using WallpaperChanger.UI.Resources;

namespace WallpaperChanger.UI.ViewModels;

/// <summary>
/// 壁紙の切り替え結果のうち、ユーザーに伝えるべき「変更されなかった理由」を文章にする。
/// </summary>
internal static class WallpaperChangeMessages
{
    /// <summary>
    /// 期待どおりに変更されなかったモニターの説明。伝えることが無ければ null（呼び出し元の成功の文言を使う）。
    /// 壁紙フォルダを登録していないモニターは意図して使っていないことが多いため、他のモニターが変わった場合は伝えない。
    /// </summary>
    public static string? DescribeProblems(WallpaperChangeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Monitors.Count == 0)
        {
            return Strings.NoTargetMonitor;
        }

        bool anyChanged = result.AnyChanged;
        var reasons = new List<string>();
        AddReason(reasons, result, WallpaperChangeStatus.NoImages, Strings.ReasonNoImages);
        if (!anyChanged)
        {
            AddReason(reasons, result, WallpaperChangeStatus.NoPreviousWallpaper, Strings.ReasonNoPreviousWallpaper);
            AddReason(reasons, result, WallpaperChangeStatus.NoFolders, Strings.ReasonNoFolders);
        }

        if (reasons.Count == 0)
        {
            return null;
        }

        string summary = anyChanged ? Strings.SomeWallpapersNotChanged : Strings.WallpapersNotChanged;
        return summary + Strings.ProblemSentenceSeparator + string.Join(Strings.ReasonSeparator, reasons);
    }

    /// <summary>切り替え中の進み具合の説明。切り替えが終わっていれば null。</summary>
    public static string? DescribeProgress(WallpaperChangeProgressEventArgs progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (progress.CurrentMonitor is not { } monitor)
        {
            return null;
        }

        return progress.TotalCount == 1
            ? Localizer.Format(Strings.ChangingMonitorWallpaperFormat, monitor.DisplayName)
            : Localizer.Format(
                Strings.ChangingWallpaperProgressFormat, progress.TotalCount, progress.CompletedCount + 1, monitor.DisplayName);
    }

    private static void AddReason(List<string> reasons, WallpaperChangeResult result, WallpaperChangeStatus status, string reason)
    {
        IReadOnlyList<MonitorInfo> monitors = result.MonitorsWith(status);
        if (monitors.Count > 0)
        {
            reasons.Add(Localizer.Format(
                Strings.ReasonWithMonitorsFormat, reason, string.Join(Strings.ListSeparator, monitors.Select(m => m.DisplayName))));
        }
    }
}
