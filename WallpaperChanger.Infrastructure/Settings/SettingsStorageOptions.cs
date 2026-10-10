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

    /// <summary>
    /// 読み込みに失敗したときに試す回数（初回を含む）。
    /// ウイルス対策ソフトのスキャンなどで一時的にロックされている場合に備える。
    /// </summary>
    public int ReadAttempts { get; set; } = 3;

    /// <summary>読み込みを再試行するまでの待ち時間。</summary>
    public TimeSpan ReadRetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);
}
