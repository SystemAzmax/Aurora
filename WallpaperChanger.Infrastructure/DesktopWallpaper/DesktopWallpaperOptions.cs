namespace WallpaperChanger.Infrastructure.DesktopWallpaper;

/// <summary>
/// IDesktopWallpaper の呼び出しに関する設定。テストでは短い時間に差し替える。
/// </summary>
public sealed class DesktopWallpaperOptions
{
    /// <summary>
    /// 1 回の呼び出しの制限時間（STA スレッドで実行が始まってから）。
    /// エクスプローラーが応答しない場合に、壁紙の切り替えやアプリの終了が止まったままにならないようにする。
    /// 大きな画像の設定には数秒かかることがあるため、余裕を持たせる。
    /// </summary>
    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
