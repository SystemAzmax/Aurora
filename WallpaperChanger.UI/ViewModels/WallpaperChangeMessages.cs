using WallpaperChanger.Core.Models;

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
            return "対象のモニターが見つからないため、壁紙は変更されませんでした。";
        }

        bool anyChanged = result.AnyChanged;
        var reasons = new List<string>();
        AddReason(reasons, result, WallpaperChangeStatus.NoImages, "壁紙フォルダに対応する画像がありません");
        if (!anyChanged)
        {
            AddReason(reasons, result, WallpaperChangeStatus.NoPreviousWallpaper, "これ以上前の壁紙はありません");
            AddReason(reasons, result, WallpaperChangeStatus.NoFolders, "壁紙フォルダが登録されていません");
        }

        if (reasons.Count == 0)
        {
            return null;
        }

        string summary = anyChanged ? "一部のモニターの壁紙は変更されませんでした。" : "壁紙は変更されませんでした。";
        return $"{summary}{string.Join(" / ", reasons)}";
    }

    private static void AddReason(List<string> reasons, WallpaperChangeResult result, WallpaperChangeStatus status, string reason)
    {
        IReadOnlyList<MonitorInfo> monitors = result.MonitorsWith(status);
        if (monitors.Count > 0)
        {
            reasons.Add($"{reason}（{string.Join("、", monitors.Select(m => m.DisplayName))}）");
        }
    }
}
