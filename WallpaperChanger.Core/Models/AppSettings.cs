using System.Text.Json.Serialization;

namespace WallpaperChanger.Core.Models;

/// <summary>
/// アプリケーション全体の設定。不変オブジェクトとして扱い、変更は with 式で行う。
/// </summary>
public sealed record AppSettings
{
    public const int MinIntervalMinutes = 1;
    public const int MaxIntervalMinutes = 24 * 60;
    public const int DefaultIntervalMinutes = 30;

    /// <summary>自動切り替え間隔（分）。1 分～24 時間。</summary>
    public int IntervalMinutes { get; init; } = DefaultIntervalMinutes;

    /// <summary>自動切り替えを一時停止しているか。</summary>
    public bool IsPaused { get; init; }

    /// <summary>モニターごとの設定。</summary>
    public IReadOnlyList<MonitorSettings> Monitors { get; init; } = [];

    /// <summary>表示しない画像（全モニター共通）。壁紙フォルダにあっても候補にしない。</summary>
    public IReadOnlyList<string> ExcludedImages { get; init; } = [];

    [JsonIgnore]
    public TimeSpan Interval => TimeSpan.FromMinutes(IntervalMinutes);

    public static bool IsValidInterval(int minutes) =>
        minutes is >= MinIntervalMinutes and <= MaxIntervalMinutes;

    /// <summary>指定モニターの設定を取得する。未登録の場合は既定値を返す。</summary>
    public MonitorSettings GetMonitor(string monitorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);

        return Monitors.FirstOrDefault(m => IsSameMonitor(m.MonitorId, monitorId))
            ?? MonitorSettings.CreateDefault(monitorId);
    }

    /// <summary>指定モニターの設定を追加または置き換えた新しい設定を返す。</summary>
    public AppSettings WithMonitor(MonitorSettings monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        return this with
        {
            Monitors = [.. Monitors.Where(m => !IsSameMonitor(m.MonitorId, monitor.MonitorId)), monitor],
        };
    }

    /// <summary>
    /// モニターの設定を別のモニター ID に移した新しい設定を返す（接続先のポートを変えて ID が変わったモニター用）。
    /// 移し先に設定があれば置き換える。移し元に設定が無ければ何もしない。
    /// </summary>
    public AppSettings MoveMonitor(string fromMonitorId, string toMonitorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromMonitorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toMonitorId);

        if (Monitors.FirstOrDefault(m => IsSameMonitor(m.MonitorId, fromMonitorId)) is not { } source)
        {
            return this;
        }

        return this with
        {
            Monitors =
            [
                .. Monitors.Where(m => !IsSameMonitor(m.MonitorId, fromMonitorId) && !IsSameMonitor(m.MonitorId, toMonitorId)),
                source with { MonitorId = toMonitorId },
            ],
        };
    }

    /// <summary>接続中のモニターの名前を、そのモニターの設定に記録した新しい設定を返す（設定の無いモニターは追加しない）。</summary>
    public AppSettings RecordMonitorNames(IEnumerable<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        Dictionary<string, string> names = monitors
            .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().DisplayName, StringComparer.OrdinalIgnoreCase);
        return this with
        {
            Monitors =
            [
                .. Monitors.Select(m => names.TryGetValue(m.MonitorId, out string? name) && m.DisplayName != name
                    ? m with { DisplayName = name }
                    : m),
            ],
        };
    }

    /// <summary>画像を表示しない画像に加えた新しい設定を返す。</summary>
    public AppSettings AddExcludedImage(string imagePath) =>
        this with { ExcludedImages = PathList.Add(ExcludedImages, imagePath) };

    /// <summary>画像を表示しない画像から外した新しい設定を返す。</summary>
    public AppSettings RemoveExcludedImage(string imagePath) =>
        this with { ExcludedImages = PathList.Remove(ExcludedImages, imagePath) };

    /// <summary>表示しない画像か（大文字小文字無視）。</summary>
    public bool IsExcluded(string imagePath) =>
        ExcludedImages.Contains(imagePath, StringComparer.OrdinalIgnoreCase);

    public AppSettings WithInterval(int minutes)
    {
        if (!IsValidInterval(minutes))
        {
            throw new ArgumentOutOfRangeException(
                nameof(minutes), minutes, $"切り替え間隔は {MinIntervalMinutes}～{MaxIntervalMinutes} 分の範囲で指定してください。");
        }

        return this with { IntervalMinutes = minutes };
    }

    /// <summary>読み込んだ設定の不正値を補正する。</summary>
    public AppSettings Normalize() => this with
    {
        IntervalMinutes = Math.Clamp(IntervalMinutes, MinIntervalMinutes, MaxIntervalMinutes),
        Monitors = [.. (Monitors ?? []).Where(m => !string.IsNullOrWhiteSpace(m.MonitorId))
            .Select(m => m.Normalize())],
        ExcludedImages = [.. (ExcludedImages ?? []).Where(p => !string.IsNullOrWhiteSpace(p))],
    };

    private static bool IsSameMonitor(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
