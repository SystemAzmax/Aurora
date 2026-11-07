using System.Globalization;
using System.Windows.Media;

namespace WallpaperChanger.UI.ViewModels;

/// <summary>
/// 分割表示の間隔・空きマスの色の選択肢。
/// </summary>
/// <param name="DisplayName">表示名。</param>
/// <param name="Rgb">色（0xRRGGBB）。</param>
public sealed record TileColorOption(string DisplayName, int Rgb)
{
    /// <summary>用意した色。</summary>
    public static IReadOnlyList<TileColorOption> Presets { get; } =
    [
        new("黒", 0x000000),
        new("濃い灰色", 0x202020),
        new("灰色", 0x808080),
        new("薄い灰色", 0xD0D0D0),
        new("白", 0xFFFFFF),
    ];

    /// <summary>選択肢の横に表示する色の見本。</summary>
    public Brush Brush
    {
        get
        {
            var brush = new SolidColorBrush(Color.FromRgb((byte)(Rgb >> 16), (byte)(Rgb >> 8), (byte)Rgb));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// 指定の色を含む選択肢の一覧。用意した色でなければ（設定ファイルを直接書き換えた場合など）、その色を末尾に加える。
    /// </summary>
    public static IReadOnlyList<TileColorOption> OptionsFor(int rgb) =>
        Presets.Any(p => p.Rgb == rgb)
            ? Presets
            : [.. Presets, new($"#{rgb.ToString("X6", CultureInfo.InvariantCulture)}", rgb)];
}
