using WallpaperChanger.Core.Models;

namespace WallpaperChanger.UI.ViewModels;

/// <summary>
/// 1 枚表示の表示方法の選択肢。<see cref="Fit"/> が null の選択肢は Windows の設定のまま表示する。
/// </summary>
public sealed record SingleImageFitOption(string DisplayName, string Description, ImageFit? Fit)
{
    public static SingleImageFitOption Windows { get; } = new(
        "Windows の設定に従う",
        "画像をそのまま壁紙にし、Windows の「個人用設定 > 背景」の調整方法で表示します（全モニター共通の設定です）。",
        null);

    public static IReadOnlyList<SingleImageFitOption> All { get; } =
    [
        Windows,
        new("画面を埋める",
            "画面全体を覆うように拡大縮小し、はみ出した部分は中央で切り抜きます。",
            ImageFit.Fill),
        new("全体を表示",
            "画像全体が画面に収まるように拡大縮小し、余白は黒で表示します。縦長の写真も切れずに表示できます。",
            ImageFit.Fit),
        new("引き伸ばす",
            "縦横比を変えて、画面いっぱいに引き伸ばします。",
            ImageFit.Stretch),
        new("中央に表示",
            "拡大縮小せずに中央に表示します。画面より大きい画像は中央を切り抜き、余白は黒で表示します。",
            ImageFit.Center),
    ];

    /// <summary>設定の値に当たる選択肢。</summary>
    public static SingleImageFitOption From(ImageFit? fit) => All.FirstOrDefault(o => o.Fit == fit) ?? Windows;
}
