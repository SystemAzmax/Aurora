namespace WallpaperChanger.Core.Models;

/// <summary>
/// 1 回の切り替えで 1 モニターに表示した画像の組（履歴の 1 件）。
/// </summary>
public sealed record WallpaperSelection
{
    public WallpaperSelection(WallpaperLayout layout, IReadOnlyList<string?> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (!images.Any(i => i is not null))
        {
            throw new ArgumentException("画像を 1 枚以上指定してください。", nameof(images));
        }

        Layout = layout;
        Images = images;
    }

    public WallpaperLayout Layout { get; }

    /// <summary>
    /// 表示した画像（左上から右方向、次の行へ、の順）。
    /// null は画像を置かないマス（黒で表示）を表す。
    /// </summary>
    public IReadOnlyList<string?> Images { get; }

    /// <summary>実際に表示している画像（空きマスを除く）。</summary>
    public IEnumerable<string> ShownImages => Images.OfType<string>();

    /// <summary>合成せずに 1 枚の画像をそのまま壁紙にできるか。</summary>
    public bool IsSingleImage => Layout == WallpaperLayout.SingleImage && Images is [not null];

    public static WallpaperSelection ForSingleImage(string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        return new(WallpaperLayout.SingleImage, [imagePath]);
    }

    /// <summary>レイアウトと画像（大文字小文字無視）が同じか。</summary>
    public bool HasSameContent(WallpaperSelection? other) =>
        other is not null
        && other.Layout == Layout
        && other.Images.SequenceEqual(Images, StringComparer.OrdinalIgnoreCase);
}
