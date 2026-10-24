namespace WallpaperChanger.Core.Models;

/// <summary>
/// フォルダ・画像のパス一覧の追加・削除（正規化と重複排除）。
/// </summary>
internal static class PathList
{
    public static IReadOnlyList<string> Add(IReadOnlyList<string> paths, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string normalized = Normalize(path);
        return paths.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? paths : [.. paths, normalized];
    }

    public static IReadOnlyList<string> Remove(IReadOnlyList<string> paths, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string normalized = Normalize(path);
        return [.. paths.Where(p => !string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase))];
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
