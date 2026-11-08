using WallpaperChanger.Core.Models;

namespace WallpaperChanger.UI.ViewModels;

/// <summary>
/// 表示言語の選択肢。表示名は、今の表示言語が読めない人にも分かるよう、その言語自身の名前にする。
/// </summary>
public sealed record LanguageOption(AppLanguage Language, string DisplayName)
{
    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        new(AppLanguage.Japanese, "日本語"),
        new(AppLanguage.English, "English"),
    ];

    /// <summary>設定の値に当たる選択肢。</summary>
    public static LanguageOption From(AppLanguage language) => All.FirstOrDefault(o => o.Language == language) ?? All[0];
}
