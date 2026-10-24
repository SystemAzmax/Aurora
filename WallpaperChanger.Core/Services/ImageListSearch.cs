namespace WallpaperChanger.Core.Services;

internal static class ImageListSearch
{
    /// <summary>パスを大文字小文字を区別せずに検索する。見つからない場合は -1。</summary>
    public static int IndexOf(IReadOnlyList<string> images, string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath))
        {
            return -1;
        }

        for (int i = 0; i < images.Count; i++)
        {
            if (string.Equals(images[i], imagePath, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 整列済みの一覧で、パス順（大文字小文字無視）で指定のパスより前にある最後の画像の位置。無ければ -1。
    /// </summary>
    public static int IndexOfPreceding(IReadOnlyList<string> images, string imagePath)
    {
        int index = -1;
        for (int i = 0; i < images.Count && StringComparer.OrdinalIgnoreCase.Compare(images[i], imagePath) < 0; i++)
        {
            index = i;
        }

        return index;
    }
}
