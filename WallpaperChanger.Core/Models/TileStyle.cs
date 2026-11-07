namespace WallpaperChanger.Core.Models;

/// <summary>
/// 分割表示の見た目（マスへの収め方・間隔・色）。不変オブジェクトとして扱い、変更は with 式で行う。
/// </summary>
/// <remarks>
/// 設定ファイルのソース生成による読み込みでは、ファイルに無い init プロパティは初期値ではなく既定値（0 など）になる。
/// この設定を追加する前の設定ファイルでも既定の見た目になるよう、<see cref="MonitorSettings"/> では 1 つのオブジェクトにまとめ、
/// ファイルに無ければ（null なら）<see cref="MonitorSettings.Normalize"/> で既定値に置き換える。
/// </remarks>
public sealed record TileStyle
{
    /// <summary>マスの間隔の既定値（ピクセル）。</summary>
    public const int DefaultGap = 4;

    /// <summary>マスの間隔の上限（ピクセル）。</summary>
    public const int MaxGap = 32;

    /// <summary>マスの間隔の色の既定値（0xRRGGBB、濃い灰色）。</summary>
    public const int DefaultGapColor = 0x202020;

    /// <summary>空きマス・余白の色の既定値（0xRRGGBB、黒）。</summary>
    public const int DefaultEmptyColor = 0x000000;

    /// <summary>各画像をマスに収める方法。</summary>
    public ImageFit Fit { get; init; } = ImageFit.Fill;

    /// <summary>マスの間隔（ピクセル、0～<see cref="MaxGap"/>）。</summary>
    public int Gap { get; init; } = DefaultGap;

    /// <summary>マスの間隔の色（0xRRGGBB）。</summary>
    public int GapColor { get; init; } = DefaultGapColor;

    /// <summary>フォルダ未設定のマスと、全体表示で画像が覆わない余白の色（0xRRGGBB）。</summary>
    public int EmptyColor { get; init; } = DefaultEmptyColor;

    /// <summary>範囲外の値を補正する。</summary>
    internal TileStyle Normalize() => this with
    {
        Fit = Enum.IsDefined(Fit) ? Fit : ImageFit.Fill,
        Gap = Math.Clamp(Gap, 0, MaxGap),
        GapColor = GapColor & 0xFFFFFF,
        EmptyColor = EmptyColor & 0xFFFFFF,
    };
}
