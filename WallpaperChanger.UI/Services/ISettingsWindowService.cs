namespace WallpaperChanger.UI.Services;

/// <summary>
/// 設定画面の表示を管理する。
/// </summary>
public interface ISettingsWindowService
{
    /// <summary>設定画面を表示する。既に表示中なら前面に出す。UI スレッドから呼び出すこと。</summary>
    void Show();

    /// <summary>設定画面が表示中なら閉じる。UI スレッドから呼び出すこと。</summary>
    void Close();
}
