namespace WallpaperChanger.Infrastructure.Settings;

/// <summary>
/// 設定ファイルの保存先。テストでは一時フォルダに差し替える。
/// </summary>
public sealed class SettingsStorageOptions
{
    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperChanger",
        "settings.json");

    public string FilePath { get; set; } = DefaultFilePath;
}
