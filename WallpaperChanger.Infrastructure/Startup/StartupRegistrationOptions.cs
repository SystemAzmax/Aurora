namespace WallpaperChanger.Infrastructure.Startup;

/// <summary>
/// 自動起動の登録先。テストでは HKCU 配下の一時キーに差し替える。
/// </summary>
public sealed class StartupRegistrationOptions
{
    /// <summary>HKEY_CURRENT_USER からの Run キーのパス。</summary>
    public string RunKeyPath { get; set; } = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// HKEY_CURRENT_USER からの StartupApproved\Run キーのパス。
    /// タスク マネージャーの「スタートアップ アプリ」での有効/無効がここに記録される。
    /// </summary>
    public string StartupApprovedKeyPath { get; set; } = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>登録名。</summary>
    public string ValueName { get; set; } = "WallpaperChanger";

    /// <summary>起動する実行ファイル。null の場合は現在のプロセスの実行ファイル。</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>この配下の実行ファイルは自動起動に登録しない（後で消えるため）。テストでは差し替える。</summary>
    public string TemporaryDirectory { get; set; } = Path.GetTempPath();
}
