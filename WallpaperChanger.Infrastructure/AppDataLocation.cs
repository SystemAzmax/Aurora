using System.Runtime.InteropServices;
using Windows.Storage;

namespace WallpaperChanger.Infrastructure;

/// <summary>
/// アプリのデータ（設定・履歴・合成画像・ログ）の保存先。
/// </summary>
/// <remarks>
/// パッケージ（MSIX）で動くデスクトップ アプリが %LOCALAPPDATA% に新しく作るファイルは、パッケージ専用の場所に振り替えられ、
/// ほかのプロセスからは見えない。合成画像を壁紙にすると、壁紙を描くエクスプローラーが画像を読めず、
/// 「ログフォルダを開く」でも空のフォルダが開いてしまう。
/// そのため、パッケージで動くときは、ほかのプロセスからも見えるパッケージのローカル フォルダー
/// （%LOCALAPPDATA%\Packages\&lt;パッケージ ファミリ名&gt;\LocalState）に保存する。アンインストールすると削除される。
/// パッケージなしで動くときは、今までどおり %LOCALAPPDATA%\WallpaperChanger に保存する。
/// </remarks>
public static class AppDataLocation
{
    /// <summary>GetCurrentPackageFullName が、パッケージなしで動いているときに返すエラー。</summary>
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    /// <summary>パッケージ（MSIX）で動いているか。</summary>
    public static bool IsPackaged { get; } = DetectPackaged();

    /// <summary>アプリのデータを保存するフォルダー。</summary>
    public static string Directory { get; } = IsPackaged
        ? ApplicationData.Current.LocalFolder.Path
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperChanger");

    private static bool DetectPackaged()
    {
        // パッケージ名の長さだけを問い合わせる。パッケージありなら、バッファーが足りないというエラーになる
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) != APPMODEL_ERROR_NO_PACKAGE;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, char[]? packageFullName);
}
