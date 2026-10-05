namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// ログファイルの保存先を提供する（設定画面の「ログフォルダを開く」などで使う）。
/// </summary>
public interface ILogDirectoryProvider
{
    /// <summary>ログファイルを保存するフォルダのフルパス。</summary>
    string LogDirectory { get; }
}
