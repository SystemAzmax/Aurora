namespace WallpaperChanger.Core.Interfaces;

/// <summary>
/// Windows へのサインイン時にアプリを自動起動するための登録を管理する。
/// 登録状態は OS 側（レジストリ）を正とし、アプリの設定ファイルには保存しない。
/// </summary>
public interface IStartupRegistrationService
{
    /// <summary>
    /// 自動起動が有効か。登録されていても、タスク マネージャーの「スタートアップ アプリ」で無効にされている場合は false。
    /// </summary>
    bool IsEnabled();

    /// <summary>自動起動を有効にする。タスク マネージャーで無効にされていた場合も有効に戻す。</summary>
    void Enable();

    /// <summary>自動起動を無効にする（登録を削除する）。</summary>
    void Disable();

    /// <summary>
    /// 登録済みの実行ファイルのパスが現在のものと異なる場合（アプリを移動・更新した場合など）に更新する。
    /// 未登録の場合は何もしない。
    /// </summary>
    /// <returns>更新した場合は true。</returns>
    bool UpdateRegisteredPathIfNeeded();
}
