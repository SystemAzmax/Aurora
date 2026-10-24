namespace WallpaperChanger.Infrastructure.History;

/// <summary>
/// 壁紙の履歴ファイルの保存先。切り替えのたびに書き込むため、設定ファイルとは分ける。テストでは一時フォルダに差し替える。
/// </summary>
public sealed class HistoryStorageOptions
{
    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperChanger",
        "history.json");

    public string FilePath { get; set; } = DefaultFilePath;
}
