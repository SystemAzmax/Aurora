namespace WallpaperChanger.Core.Models;

/// <summary>
/// フォルダ一覧の追加・削除（正規化と重複排除）。
/// </summary>
internal static class FolderList
{
    public static IReadOnlyList<string> Add(IReadOnlyList<string> folders, string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        string normalized = Normalize(folder);
        return folders.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? folders : [.. folders, normalized];
    }

    public static IReadOnlyList<string> Remove(IReadOnlyList<string> folders, string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        string normalized = Normalize(folder);
        return [.. folders.Where(f => !string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase))];
    }

    private static string Normalize(string folder) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
}
