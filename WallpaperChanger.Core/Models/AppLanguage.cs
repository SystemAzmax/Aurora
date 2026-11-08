namespace WallpaperChanger.Core.Models;

/// <summary>画面・通知の表示言語。設定ファイルに無ければ既定値（日本語）になるよう、日本語を 0 にする。</summary>
public enum AppLanguage
{
    /// <summary>日本語（既定）。</summary>
    Japanese = 0,

    /// <summary>英語。</summary>
    English = 1,
}
