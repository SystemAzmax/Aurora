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
    };

    private static bool IsSameMonitor(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
