using WallpaperChanger.Core.Interfaces;
using WallpaperChanger.Core.Models;

namespace WallpaperChanger.Core.Services;

/// <summary>
/// 現在表示している最後の画像の次から、パス順に count 枚を選ぶ。末尾の次は先頭に戻る。
/// </summary>
public sealed class SequentialImageSelector : IImageSelector
{
    public ImageSelectionMode Mode => ImageSelectionMode.Sequential;

    public IReadOnlyList<string> SelectNext(IReadOnlyList<string> images, IReadOnlyList<string> currentImages, int count)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(currentImages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        if (images.Count == 0)
        {
            return [];
        }

        // 現在の組のうち候補に含まれる最後の画像を基準にする（フォルダ変更で消えた画像は無視）
        int lastIndex = -1;
        for (int i = currentImages.Count - 1; i >= 0 && lastIndex < 0; i--)
        {
            lastIndex = ImageListSearch.IndexOf(images, currentImages[i]);
        }

        var selected = new string[count];
        for (int i = 0; i < count; i++)
        {
            selected[i] = images[(lastIndex + 1 + i) % images.Count];
        }

        return selected;
    }
}
