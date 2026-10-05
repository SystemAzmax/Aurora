namespace WallpaperChanger.Core.Models;

/// <summary>
/// 次の壁紙の選び方。
/// </summary>
public enum ImageSelectionMode
{
    /// <summary>ランダム選択。</summary>
    Random = 0,

    /// <summary>ファイルパス順の順送り選択。</summary>
    Sequential = 1,
}
