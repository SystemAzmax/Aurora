using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WallpaperChanger.Infrastructure.Imaging;

/// <summary>
/// EXIF の Orientation（撮影時のカメラの向き）に従って画像を回転・反転する。
/// WPF の BitmapImage は Orientation を反映しないため、スマートフォンで縦向きに撮った写真などが横倒しになるのを防ぐ。
/// </summary>
public static class ExifOrientation
{
    /// <summary>補正が不要な向き（Orientation = 1）。</summary>
    public const int Normal = 1;

    private const string OrientationQuery = "System.Photo.Orientation";

    /// <summary>
    /// 画像の Orientation（1～8）を読み取る。メタデータを持たない形式（BMP など）や、値が無い・不正な場合は <see cref="Normal"/>。
    /// </summary>
    public static int Read(BitmapFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        try
        {
            return frame.Metadata is BitmapMetadata metadata
                   && metadata.ContainsQuery(OrientationQuery)
                   && metadata.GetQuery(OrientationQuery) is ushort value
                   && value is >= 1 and <= 8
                ? value
                : Normal;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // 形式によってはメタデータの読み取り自体に対応していない。向きは補正せずに表示する
            return Normal;
        }
    }

    /// <summary>補正すると縦横が入れ替わる（90° / 270° 回転を含む）か。</summary>
    public static bool SwapsDimensions(int orientation) => orientation is >= 5 and <= 8;

    /// <summary>
    /// Orientation に従って回転・反転した画像を返す（Freeze 済み）。補正が不要な場合は元の画像をそのまま返す。
    /// </summary>
    public static BitmapSource Apply(BitmapSource source, int orientation)
    {
        ArgumentNullException.ThrowIfNull(source);

        Transform? transform = CreateTransform(orientation);
        if (transform is null)
        {
            return source;
        }

        var transformed = new TransformedBitmap(source, transform);
        transformed.Freeze();
        return transformed;
    }

    /// <summary>
    /// 保存されている向きから表示する向きへの変換。WPF の回転は時計回りで、TransformGroup は先頭から順に適用される。
    /// </summary>
    private static Transform? CreateTransform(int orientation) => orientation switch
    {
        2 => new ScaleTransform(-1, 1),                                        // 左右反転
        3 => new RotateTransform(180),                                         // 180° 回転
        4 => new ScaleTransform(1, -1),                                        // 上下反転
        5 => Group(new RotateTransform(90), new ScaleTransform(-1, 1)),        // 転置（左上と右下を結ぶ線で反転）
        6 => new RotateTransform(90),                                          // 時計回りに 90° 回転
        7 => Group(new RotateTransform(90), new ScaleTransform(1, -1)),        // 反転置（右上と左下を結ぶ線で反転）
        8 => new RotateTransform(270),                                         // 時計回りに 270° 回転
        _ => null,
    };

    private static TransformGroup Group(params Transform[] transforms)
    {
        var group = new TransformGroup();
        foreach (Transform transform in transforms)
        {
            group.Children.Add(transform);
        }

        return group;
    }
}
