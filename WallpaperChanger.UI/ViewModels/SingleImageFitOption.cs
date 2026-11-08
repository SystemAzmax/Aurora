using WallpaperChanger.Core.Models;
using WallpaperChanger.UI.Resources;

namespace WallpaperChanger.UI.ViewModels;

/// <summary>
/// 1 枚表示の表示方法の選択肢。<see cref="Fit"/> が null の選択肢は Windows の設定のまま表示する。
/// </summary>
public sealed record SingleImageFitOption(string DisplayName, string Description, ImageFit? Fit)
{
    public static SingleImageFitOption Windows { get; } = new(
        Strings.SingleImageFitWindows, Strings.SingleImageFitWindowsDescription, null);

    public static IReadOnlyList<SingleImageFitOption> All { get; } =
    [
        Windows,
        new(Strings.SingleImageFitFill, Strings.SingleImageFitFillDescription, ImageFit.Fill),
        new(Strings.SingleImageFitFit, Strings.SingleImageFitFitDescription, ImageFit.Fit),
        new(Strings.SingleImageFitStretch, Strings.SingleImageFitStretchDescription, ImageFit.Stretch),
        new(Strings.SingleImageFitCenter, Strings.SingleImageFitCenterDescription, ImageFit.Center),
    ];

    /// <summary>設定の値に当たる選択肢。</summary>
    public static SingleImageFitOption From(ImageFit? fit) => All.FirstOrDefault(o => o.Fit == fit) ?? Windows;
}
