namespace WallpaperChanger.UI.Services;

/// <summary>
/// 設定の書き出し先・読み込むファイルを選ぶダイアログを表示する。
/// </summary>
public interface ISettingsFileDialogService
{
    /// <summary>設定の書き出し先を選ばせる。キャンセル時は null。</summary>
    /// <param name="defaultFileName">はじめに表示するファイル名。</param>
    string? PickExportPath(string defaultFileName);

    /// <summary>読み込む設定のファイルを選ばせる。キャンセル時は null。</summary>
    string? PickImportPath();
}
