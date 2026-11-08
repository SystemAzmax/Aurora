using WallpaperChanger.UI.Resources;

namespace WallpaperChanger.UI.ViewModels;

/// <summary>
/// 切り替え間隔の単位の選択肢。
/// </summary>
public sealed record IntervalUnitOption(string DisplayName, int MinutesPerUnit)
{
    public static IntervalUnitOption Minutes { get; } = new(Strings.IntervalUnitMinutes, 1);

    public static IntervalUnitOption Hours { get; } = new(Strings.IntervalUnitHours, 60);

    public static IReadOnlyList<IntervalUnitOption> All { get; } = [Minutes, Hours];

    /// <summary>分数を「値 + 単位」に分解する。60 で割り切れる場合は時間単位にする。</summary>
    public static (int Value, IntervalUnitOption Unit) FromMinutes(int minutes) =>
        minutes % Hours.MinutesPerUnit == 0
            ? (minutes / Hours.MinutesPerUnit, Hours)
            : (minutes, Minutes);

    /// <summary>表示用の文字列（例: 「1 時間 30 分」）に変換する。</summary>
    public static string Format(TimeSpan interval)
    {
        int totalMinutes = (int)interval.TotalMinutes;
        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;

        return (hours, minutes) switch
        {
            (0, _) => Localizer.Format(Strings.IntervalMinutesFormat, minutes),
            (_, 0) => Localizer.Format(Strings.IntervalHoursFormat, hours),
            _ => Localizer.Format(Strings.IntervalHoursMinutesFormat, hours, minutes),
        };
    }
}
