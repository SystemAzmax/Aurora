using Microsoft.Extensions.Logging;

namespace WallpaperChanger.Infrastructure.Logging;

/// <summary>
/// ファイルへのログ出力の設定。テストでは保存先を一時フォルダに差し替える。
/// </summary>
public sealed class FileLoggerOptions
{
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperChanger",
        "logs");

    /// <summary>ログファイルの保存先。</summary>
    public string Directory { get; set; } = DefaultDirectory;

    /// <summary>ファイル名の接頭辞（例: wallpaperchanger-20261005.log）。</summary>
    public string FileNamePrefix { get; set; } = "wallpaperchanger-";

    /// <summary>この日数より古いログファイルを削除する。</summary>
    public int RetentionDays { get; set; } = 14;

    /// <summary>ファイルに書き込む最低のログレベル。</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>書き込み待ちにできる最大件数。超えた分は破棄する（アプリの動作を止めないため）。</summary>
    public int QueueCapacity { get; set; } = 10_000;
}
