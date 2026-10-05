namespace WallpaperChanger.Core.Models;

/// <summary>
/// 壁紙として扱う画像形式。
/// </summary>
public static class SupportedImageFormats
{
    public static IReadOnlySet<string> Extensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path));
}
