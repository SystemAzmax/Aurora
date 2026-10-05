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
}
